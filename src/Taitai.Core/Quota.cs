using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Taitai.Core;

public sealed record QuotaWindow(string Bucket, string Label, double UsedPercent, int Minutes, DateTimeOffset? ResetAt)
{
    public double Remaining => Math.Clamp(100 - UsedPercent, 0, 100);
    public string DurationLabel => Minutes >= 10080 ? "每周" : Minutes >= 1440 ? $"{Minutes / 1440}天" : Minutes >= 60 ? $"{Minutes / 60}小时" : Minutes > 0 ? $"{Minutes}分钟" : Label;
    public string NotificationKey => $"{Bucket}/{Label}/{ResetAt?.ToUnixTimeSeconds()}";
}

public sealed record QuotaSnapshot(IReadOnlyList<QuotaWindow> Windows, DateTimeOffset FetchedAt, string? Plan);

public static class QuotaParser
{
    public static QuotaSnapshot Parse(JsonElement result, DateTimeOffset fetchedAt)
    {
        var windows = new List<QuotaWindow>();
        string? plan = null;
        if (result.TryGetProperty("rateLimitsByLimitId", out var byId) && byId.ValueKind == JsonValueKind.Object && byId.EnumerateObject().Any())
        {
            foreach (var bucket in byId.EnumerateObject().OrderBy(p => p.Name == "codex" ? 0 : 1))
                ReadBucket(bucket.Name, bucket.Value);
        }
        else if (result.TryGetProperty("rateLimits", out var legacy) && legacy.ValueKind == JsonValueKind.Object)
            ReadBucket("codex", legacy);
        if (windows.Count == 0) throw new InvalidDataException("Codex 没有返回可用额度；请确认已使用 ChatGPT 账号登录。");
        return new(windows, fetchedAt, plan);

        void ReadBucket(string id, JsonElement bucket)
        {
            if (bucket.ValueKind != JsonValueKind.Object) return;
            if (bucket.TryGetProperty("planType", out var p) && p.ValueKind == JsonValueKind.String) plan ??= p.GetString();
            var title = id == "codex" ? "Codex" : id;
            if (bucket.TryGetProperty("limitName", out var n) && n.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(n.GetString())) title = n.GetString()!;
            foreach (var slot in new[] { "primary", "secondary" })
            {
                if (!bucket.TryGetProperty(slot, out var w) || w.ValueKind != JsonValueKind.Object || !w.TryGetProperty("usedPercent", out var u) || u.ValueKind != JsonValueKind.Number || !u.TryGetDouble(out var used) || !double.IsFinite(used)) continue;
                var minutes = w.TryGetProperty("windowDurationMins", out var m) && m.ValueKind == JsonValueKind.Number && m.TryGetInt32(out var mm) ? Math.Max(0, mm) : 0;
                DateTimeOffset? reset = null;
                if (w.TryGetProperty("resetsAt", out var r) && r.ValueKind == JsonValueKind.Number && r.TryGetInt64(out var epoch))
                    try { reset = DateTimeOffset.FromUnixTimeSeconds(epoch); } catch (ArgumentOutOfRangeException) { }
                windows.Add(new(title, slot, Math.Clamp(used, 0, 100), minutes, reset));
            }
        }
    }

    public static string Countdown(DateTimeOffset? at, DateTimeOffset now)
    {
        if (at is null) return "重置时间未知";
        var span = at.Value - now;
        if (span <= TimeSpan.Zero) return "已到重置时间，等待同步";
        if (span.TotalDays >= 1) return $"{(int)span.TotalDays}天 {span.Hours}小时后重置";
        if (span.TotalHours >= 1) return $"{(int)span.TotalHours}小时 {span.Minutes}分钟后重置";
        return $"{Math.Max(1, (int)Math.Ceiling(span.TotalMinutes))}分钟后重置";
    }
}

public sealed class CodexQuotaClient
{
    public static string? FindBinary(string? configured = null)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return IsCli(configured) ? configured : null;
        var candidates = new List<string>();
        foreach (var process in Process.GetProcessesByName("codex"))
        {
            using (process) try { if (process.MainModule?.FileName is string f) candidates.Add(f); } catch { }
        }
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            candidates.Add(Path.Combine(dir, "codex.exe"));
            // npm's native vendor binary avoids a shell and survives paths with spaces.
            candidates.Add(Path.Combine(dir, "node_modules", "@openai", "codex", "node_modules", "@openai", "codex-win32-x64", "vendor", "x86_64-pc-windows-msvc", "bin", "codex.exe"));
            candidates.Add(Path.Combine(dir, "node_modules", "@openai", "codex", "node_modules", "@openai", "codex-win32-arm64", "vendor", "aarch64-pc-windows-msvc", "bin", "codex.exe"));
        }
        return candidates.FirstOrDefault(IsCli);
    }

    static bool IsCli(string path)
    {
        if (!File.Exists(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return false;
        var dir = Path.GetDirectoryName(path)!;
        return !new[] { "resources.pak", "icudtl.dat", "chrome_100_percent.pak" }.Any(name => File.Exists(Path.Combine(dir, name)));
    }

    public async Task<QuotaSnapshot> ReadAsync(string? configured, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var binary = FindBinary(configured) ?? throw new FileNotFoundException("找不到 Codex 命令行。请在设置中选择 codex.exe。");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(18));
        var token = deadline.Token;
        using var process = new Process
        {
            StartInfo = new(binary, "app-server")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false),
                WorkingDirectory = Path.GetDirectoryName(binary)!
            }
        };
        try
        {
            if (!process.Start()) throw new IOException("无法启动 Codex 额度查询。");
            // Drain errors but do not save or display credentials or raw server logs.
            var drain = process.StandardError.ReadToEndAsync(token);
            await Send(new { id = 1, method = "initialize", @params = new { clientInfo = new { name = "taitai_desktop", title = "Taitai Desktop", version = "0.3.0" } } });
            await Response(1);
            await Send(new { method = "initialized" });
            await Send(new { id = 2, method = "account/rateLimits/read" });
            var result = await Response(2);
            return QuotaParser.Parse(result, DateTimeOffset.UtcNow);

            async Task Send(object value) { await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(value).AsMemory(), token); await process.StandardInput.FlushAsync(token); }
            async Task<JsonElement> Response(int id)
            {
                while (await process.StandardOutput.ReadLineAsync(token) is string line)
                {
                    JsonDocument doc;
                    try { doc = JsonDocument.Parse(line); } catch (JsonException) { continue; }
                    using (doc)
                    {
                        var root = doc.RootElement;
                        if (!root.TryGetProperty("id", out var responseId) || !responseId.TryGetInt32(out var number) || number != id) continue;
                        if (root.TryGetProperty("error", out _)) throw new IOException("Codex 暂时无法提供额度，请检查账号登录或网络后重试。");
                        if (root.TryGetProperty("result", out var value)) return value.Clone();
                        throw new InvalidDataException("Codex 返回的数据不完整。");
                    }
                }
                throw new IOException("Codex 额度服务提前退出。");
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new TimeoutException("额度查询超时；保留上次数据，稍后自动重试。"); }
        finally
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
        }
    }
}
