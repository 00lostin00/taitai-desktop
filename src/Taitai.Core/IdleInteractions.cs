using System.Text.Json;

namespace Taitai.Core;

public sealed record IdleEntry(string Text, string Action, int Weight, string When, int CooldownSeconds, string Key);
public sealed record IdleContext(int Hour, bool LowQuota);

public sealed class IdleInteractions
{
    static readonly HashSet<string> Actions = new() { "idle", "wave", "jump", "double-jump", "think", "review", "look-up", "look-left", "look-right", "shy" };
    static readonly HashSet<string> Conditions = new() { "always", "morning", "night", "low-quota" };
    readonly Random random;
    readonly Queue<string> recent = new();
    readonly Dictionary<string, DateTimeOffset> used = new();
    readonly List<IdleEntry> entries = new();
    public int PackCount { get; private set; }
    public int EntryCount => entries.Count;
    public int IgnoredFiles { get; private set; }
    public IdleInteractions(int? seed = null) => random = seed is int value ? new(value) : new();

    public void Reload(string directory)
    {
        entries.Clear(); recent.Clear(); PackCount = 0; IgnoredFiles = 0;
        if (Directory.Exists(directory))
            foreach (var file in Directory.EnumerateFiles(directory, "*.json").Take(20))
            {
                try
                {
                    if (new FileInfo(file).Length > 65536) { IgnoredFiles++; continue; }
                    using var document = JsonDocument.Parse(File.ReadAllText(file));
                    var root = document.RootElement;
                    if (root.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.False) continue;
                    if (!root.TryGetProperty("entries", out var list) || list.ValueKind != JsonValueKind.Array) { IgnoredFiles++; continue; }
                    var before = entries.Count;
                    foreach (var item in list.EnumerateArray().Take(200))
                    {
                        string Get(string name, string fallback) => item.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? fallback : fallback;
                        var text = Get("text", "").Replace('\n', ' ').Replace('\r', ' ').Trim();
                        var action = Get("action", "idle"); var when = Get("when", "always");
                        if (text.Length is 0 or > 42 || !Actions.Contains(action) || !Conditions.Contains(when)) continue;
                        var weight = item.TryGetProperty("weight", out var w) && w.ValueKind == JsonValueKind.Number && w.TryGetInt32(out var ww) ? Math.Clamp(ww, 1, 20) : 1;
                        var cooldown = item.TryGetProperty("cooldownSeconds", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetInt32(out var cc) ? Math.Clamp(cc, 0, 86400) : 120;
                        entries.Add(new(text, action, weight, when, cooldown, Path.GetFileName(file) + "/" + text));
                    }
                    if (entries.Count > before) PackCount++;
                }
                catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException) { IgnoredFiles++; }
            }
        if (entries.Count == 0)
        {
            entries.Add(new("我在这里，慢慢来就好。", "wave", 1, "always", 120, "fallback/1"));
            entries.Add(new("偷偷蹦一下，没人发现吧。", "jump", 1, "always", 300, "fallback/2"));
            entries.Add(new("今天的毛线没有打结，真不错。", "idle", 1, "always", 120, "fallback/3"));
        }
    }

    public IdleEntry? Pick(IdleContext context, DateTimeOffset now)
    {
        var eligible = entries.Where(e => Matches(e.When, context) && (!used.TryGetValue(e.Key, out var last) || now - last >= TimeSpan.FromSeconds(e.CooldownSeconds))).ToArray();
        var candidates = eligible.Where(e => !recent.Contains(e.Text)).ToArray();
        if (candidates.Length == 0) candidates = eligible;
        if (candidates.Length == 0) return null;
        var ticket = random.Next(candidates.Sum(e => e.Weight));
        var choice = candidates[^1];
        foreach (var entry in candidates) { ticket -= entry.Weight; if (ticket < 0) { choice = entry; break; } }
        used[choice.Key] = now; recent.Enqueue(choice.Text);
        while (recent.Count > Math.Min(4, Math.Max(1, entries.Count - 1))) recent.Dequeue();
        return choice;
    }
    static bool Matches(string when, IdleContext context) => when switch
    {
        "morning" => context.Hour is >= 6 and < 11,
        "night" => context.Hour >= 23 || context.Hour < 5,
        "low-quota" => context.LowQuota,
        _ => true
    };
    public TimeSpan NextDelay(string frequency) => frequency switch
    {
        "quiet" => TimeSpan.FromSeconds(random.Next(240, 481)),
        "lively" => TimeSpan.FromSeconds(random.Next(45, 91)),
        _ => TimeSpan.FromSeconds(random.Next(90, 211))
    };
}
