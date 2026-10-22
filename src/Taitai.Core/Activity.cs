using System.Text;
using System.Text.Json;

namespace Taitai.Core;

public enum PetActivity { Idle, Thinking, Tool, Waiting, Complete, Failed }
public sealed record ActivitySnapshot(PetActivity State, int ActiveCount, DateTimeOffset LastEvent, bool IsStale, string Label)
{
    public string? ThreadId { get; init; }
    public string? Title { get; init; }
    public string? ToolName { get; init; }
}

public sealed class SessionState
{
    public PetActivity State { get; private set; } = PetActivity.Idle;
    public DateTimeOffset LastEvent { get; private set; }
    public bool IsSubagent { get; private set; }
    public string? Id { get; private set; }
    public string? Title { get; private set; }
    public string? ToolName { get; private set; }
    public void Identify(string? id, string? title = null) { if (Guid.TryParse(id, out var value)) Id = value.ToString(); if (!string.IsNullOrWhiteSpace(title)) Title = title; }
    bool waitingForInput;

    public void Apply(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!root.TryGetProperty("payload", out var p) || p.ValueKind != JsonValueKind.Object) return;
            var outer = Text(root, "type");
            var type = Text(p, "type");
            if (outer == "session_meta") Identify(Text(p, "id"));
            if (outer == "session_meta" && p.TryGetProperty("source", out var source))
                IsSubagent = source.ValueKind == JsonValueKind.Object && source.TryGetProperty("subagent", out _);
            PetActivity? next = null;
            if (outer == "event_msg")
                next = type switch
                {
                    "task_started" or "turn_started" or "user_message" => PetActivity.Thinking,
                    "task_complete" or "turn_complete" or "turn_completed" => PetActivity.Complete,
                    "turn_aborted" or "task_failed" or "error" => PetActivity.Failed,
                    "approval_request" or "request_user_input" => PetActivity.Waiting,
                    "agent_reasoning" => PetActivity.Thinking,
                    _ => null
                };
            if (outer == "response_item")
            {
                var name = Text(p, "name");
                if (type is "function_call" or "custom_tool_call")
                {
                    ToolName = name.Length <= 96 ? name : name[..96];
                    waitingForInput = name.Contains("request_user_input", StringComparison.OrdinalIgnoreCase) || name.Contains("request_permissions", StringComparison.OrdinalIgnoreCase);
                    next = waitingForInput ? PetActivity.Waiting : PetActivity.Tool;
                }
                if (type is "function_call_output" or "custom_tool_call_output") next = waitingForInput ? PetActivity.Waiting : PetActivity.Thinking;
                if (type == "reasoning") next = PetActivity.Thinking;
                if (type == "message" && Text(p, "role") == "user") next = PetActivity.Thinking;
                if (type == "message" && Text(p, "role") == "assistant" && Text(p, "channel") == "final") next = PetActivity.Complete;
            }
            if (next is null) return;
            if (type is "user_message" or "task_started" or "turn_started" || type == "message" && Text(p, "role") == "user") ToolName = null;
            if (next != PetActivity.Waiting) waitingForInput = false;
            State = next.Value;
            LastEvent = root.TryGetProperty("timestamp", out var time) && time.ValueKind == JsonValueKind.String && time.TryGetDateTimeOffset(out var at) ? at : DateTimeOffset.UtcNow;
        }
        catch (JsonException) { /* A partial record is never a status change. */ }
    }

    static string Text(JsonElement e, string field) => e.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}

public sealed class ActivityWatcher
{
    sealed class Cursor
    {
        public long Offset;
        public DateTime WriteTime;
        public string Partial = "";
        public readonly SessionState State = new();
    }
    readonly string sessionsRoot;
    readonly Dictionary<string, Cursor> cursors = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> titles = new(StringComparer.OrdinalIgnoreCase);
    DateTimeOffset nextIndexRead;
    public IReadOnlyList<ActivitySnapshot> Recent { get; private set; } = Array.Empty<ActivitySnapshot>();
    public ActivityWatcher(string root) => sessionsRoot = root;

    public ActivitySnapshot Poll(DateTimeOffset now, string? preferredId = null)
    {
        if (!Directory.Exists(sessionsRoot)) return new(PetActivity.Idle, 0, default, true, "未找到本机会话");
        if (now >= nextIndexRead)
        {
            nextIndexRead = now.AddSeconds(15);
            try
            {
                var index = Path.Combine(Path.GetDirectoryName(sessionsRoot)!, "session_index.jsonl");
                if (File.Exists(index) && new FileInfo(index).Length < 4_000_000)
                    using (var input = new FileStream(index, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var reader = new StreamReader(input))
                        while (reader.ReadLine() is string line)
                            try { using var doc = JsonDocument.Parse(line); var root = doc.RootElement; if (root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String && root.TryGetProperty("thread_name", out var title) && title.ValueKind == JsonValueKind.String) titles[id.GetString()!] = title.GetString() ?? ""; } catch (JsonException) { }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        var files = new List<FileInfo>();
        for (var day = 0; day < 3; day++)
        {
            var date = now.ToLocalTime().Date.AddDays(-day);
            var dir = Path.Combine(sessionsRoot, date.ToString("yyyy"), date.ToString("MM"), date.ToString("dd"));
            if (Directory.Exists(dir))
                try { files.AddRange(new DirectoryInfo(dir).EnumerateFiles("*.jsonl")); } catch (IOException) { }
        }
        foreach (var file in files.Where(f => f.LastWriteTimeUtc > now.UtcDateTime.AddHours(-3)).OrderByDescending(f => f.LastWriteTimeUtc).Take(24))
        {
            try
            {
                if (!cursors.TryGetValue(file.FullName, out var cursor))
                {
                    cursor = new(); cursors[file.FullName] = cursor;
                    var stem = Path.GetFileNameWithoutExtension(file.Name);
                    if (stem.Length >= 36) cursor.State.Identify(stem[^36..]);
                }
                if (cursor.State.Id is string known && titles.TryGetValue(known, out var knownTitle)) cursor.State.Identify(known, knownTitle);
                if (cursor.Offset == file.Length && cursor.WriteTime == file.LastWriteTimeUtc) continue;
                using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (cursor.Offset == 0)
                {
                    using (var headerReader = new StreamReader(stream, Encoding.UTF8, true, 8192, leaveOpen: true))
                        if (headerReader.ReadLine() is string header) cursor.State.Apply(header);
                    stream.Seek(0, SeekOrigin.Begin);
                }
                if (stream.Length < cursor.Offset) { cursor.Offset = 0; cursor.Partial = ""; }
                bool skipPartial = cursor.Offset == 0 && stream.Length > 1_000_000;
                stream.Seek(skipPartial ? stream.Length - 1_000_000 : cursor.Offset, SeekOrigin.Begin);
                using var reader = new StreamReader(stream, Encoding.UTF8, true, 8192, leaveOpen: true);
                if (skipPartial) reader.ReadLine();
                var text = cursor.Partial + reader.ReadToEnd();
                cursor.Offset = stream.Position;
                cursor.WriteTime = file.LastWriteTimeUtc;
                var lines = text.Split('\n');
                cursor.Partial = lines[^1].Length <= 2_000_000 ? lines[^1] : "";
                foreach (var line in lines.AsSpan(0, lines.Length - 1)) cursor.State.Apply(line);
                if (cursor.State.Id is string id && titles.TryGetValue(id, out var title)) cursor.State.Identify(id, title);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        var cutoff = now.AddHours(-3);
        foreach (var key in cursors.Where(kv => kv.Value.State.LastEvent < cutoff).Select(kv => kv.Key).ToArray()) cursors.Remove(key);
        var main = cursors.Values.Select(c => c.State).Where(s => !s.IsSubagent).ToArray();
        Recent = main.Where(s => s.Id is not null).OrderByDescending(s => s.LastEvent).Take(8).Select(s => Aggregate(new[] { s }, now)).ToArray();
        if (preferredId is not null)
        {
            var selected = main.Where(s => string.Equals(s.Id, preferredId, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (selected.Length == 0) return new(PetActivity.Idle, 0, default, true, "未接入聊天状态") { ThreadId = preferredId, Title = titles.GetValueOrDefault(preferredId) };
            return Aggregate(selected, now);
        }
        return Aggregate(main, now);
    }

    public static ActivitySnapshot Aggregate(IEnumerable<SessionState> sessions, DateTimeOffset now)
    {
        var states = sessions.Where(s => s.LastEvent != default).ToArray();
        var busy = states.Where(s => s.State is PetActivity.Thinking or PetActivity.Tool or PetActivity.Waiting && now - s.LastEvent < (s.State == PetActivity.Waiting ? TimeSpan.FromMinutes(15) : TimeSpan.FromMinutes(3))).ToArray();
        var waiting = busy.FirstOrDefault(s => s.State == PetActivity.Waiting);
        var current = waiting ?? busy.OrderByDescending(s => s.LastEvent).FirstOrDefault() ?? states.OrderByDescending(s => s.LastEvent).FirstOrDefault();
        ActivitySnapshot Snapshot(PetActivity state, int active, DateTimeOffset time, bool stale, string label) => new(state, active, time, stale, label) { ThreadId = current?.Id, Title = current?.Title, ToolName = current?.ToolName };
        if (current is null) return new(PetActivity.Idle, 0, default, true, "等待 Codex 会话");
        var age = now - current.LastEvent;
        if (busy.Length == 0)
        {
            if (current.State == PetActivity.Complete && age < TimeSpan.FromSeconds(12)) return Snapshot(PetActivity.Complete, 0, current.LastEvent, false, "这一轮完成了");
            if (current.State == PetActivity.Failed && age < TimeSpan.FromSeconds(18)) return Snapshot(PetActivity.Failed, 0, current.LastEvent, false, "任务中断或出错");
            bool stale = current.State is PetActivity.Tool or PetActivity.Thinking or PetActivity.Waiting;
            return Snapshot(PetActivity.Idle, 0, current.LastEvent, stale, stale ? "状态未更新" : "陪你待着");
        }
        var label = current.State switch { PetActivity.Waiting => "等你回应", PetActivity.Tool => "正在执行工具", _ => "正在思考" };
        if (busy.Length > 1) label += $" · {busy.Length} 个会话";
        return Snapshot(current.State, busy.Length, current.LastEvent, false, label);
    }
}
