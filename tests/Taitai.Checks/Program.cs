using System.Text.Json;
using Taitai.Core;

int count = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception("FAILED: " + name); Console.WriteLine("PASS " + name); count++; }
QuotaSnapshot Parse(string json) { using var doc = JsonDocument.Parse(json); return QuotaParser.Parse(doc.RootElement, DateTimeOffset.UtcNow); }
var quota = Parse("""{"rateLimits":{"primary":{"usedPercent":99}},"rateLimitsByLimitId":{"codex":{"planType":"plus","primary":{"usedPercent":37,"windowDurationMins":300,"resetsAt":1792675785},"secondary":{"usedPercent":22,"windowDurationMins":10080,"resetsAt":1793244315}}}}""");
Check(quota.Windows[0].Remaining == 63 && quota.Windows[1].Remaining == 78, "live buckets override legacy and convert used to remaining");
Check(quota.Windows[0].DurationLabel == "5小时" && quota.Windows[1].DurationLabel == "每周", "duration labels come from API windows");
Check(Parse("""{"rateLimits":{"primary":null,"secondary":{"usedPercent":15}}}""").Windows.Count == 1, "missing primary is unavailable, not zero");
Check(Parse("""{"rateLimits":{"primary":{"usedPercent":-12},"secondary":{"usedPercent":130}}}""").Windows.Select(w => w.Remaining).SequenceEqual(new[] { 100d, 0d }), "out-of-range quota bounded");
try { Parse("""{"rateLimits":{"primary":{"usedPercent":null}}}"""); throw new Exception("missing value accepted"); } catch (InvalidDataException) { count++; Console.WriteLine("PASS no numeric data rejects fabricated allowance"); }
Check(QuotaParser.Countdown(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow).Contains("等待同步"), "expired reset never assumes refreshed allowance");
Check(QuotaParser.Countdown(null, DateTimeOffset.UtcNow) == "重置时间未知", "missing reset stays unknown");
var nullable = Parse("""{"rateLimits":{"primary":{"usedPercent":10,"windowDurationMins":null,"resetsAt":null}}}""");
Check(nullable.Windows.Single().ResetAt is null && nullable.Windows.Single().Minutes == 0, "nullable reset and duration are tolerated");
var now = DateTimeOffset.UtcNow;
string Event(string outer, string type, string? name = null) => JsonSerializer.Serialize(new { timestamp = now, type = outer, payload = new { type, name } });
var state = new SessionState(); state.Apply(Event("event_msg", "task_started"));
Check(state.State == PetActivity.Thinking, "task starts thinking");
state.Apply(Event("response_item", "function_call", "request_user_input_async")); state.Apply(Event("response_item", "function_call_output"));
Check(state.State == PetActivity.Waiting, "accepted async question remains waiting");
state.Apply(Event("event_msg", "user_message")); Check(state.State == PetActivity.Thinking, "new user input resumes work");
state.Apply(Event("response_item", "custom_tool_call", "exec")); Check(state.State == PetActivity.Tool, "actual tool call animates busy");
state.Apply("{broken"); Check(state.State == PetActivity.Tool, "partial event does not change status");
Check(ActivityWatcher.Aggregate(new[] { state }, now.AddMinutes(4)).IsStale, "old unfinished task becomes explicitly stale");
state.Apply(Event("event_msg", "task_complete")); Check(ActivityWatcher.Aggregate(new[] { state }, now).State == PetActivity.Complete, "completed task celebrates briefly");
Check(ActivityWatcher.Aggregate(new[] { state }, now.AddMinutes(1)).State == PetActivity.Idle, "completed animation returns idle");
var waiting = new SessionState(); waiting.Apply(Event("response_item", "function_call", "request_user_input"));
var busy = new SessionState(); busy.Apply(Event("event_msg", "task_started"));
var combined = ActivityWatcher.Aggregate(new[] { waiting, busy }, now);
Check(combined.State == PetActivity.Waiting && combined.ActiveCount == 2, "multi-session waiting remains visible with correct count");
var fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", Guid.NewGuid().ToString("N"));
var day = now.ToLocalTime();
var directory = Path.Combine(fixture, day.ToString("yyyy"), day.ToString("MM"), day.ToString("dd"));
Directory.CreateDirectory(directory);
var file = Path.Combine(directory, "rollout-fixture.jsonl");
File.WriteAllText(file, Event("event_msg", "task_started") + "\n");
var watcher = new ActivityWatcher(fixture);
Check(watcher.Poll(now).State == PetActivity.Thinking, "watcher reads a real session file");
var complete = Event("event_msg", "task_complete");
File.AppendAllText(file, complete[..(complete.Length / 2)]);
Check(watcher.Poll(now).State == PetActivity.Thinking, "incomplete appended record preserves prior state");
File.AppendAllText(file, complete[(complete.Length / 2)..] + "\n");
Check(watcher.Poll(now).State == PetActivity.Complete, "next append completes buffered record");
File.WriteAllText(file, Event("event_msg", "task_started") + "\n");
Check(watcher.Poll(now).State == PetActivity.Thinking, "truncated session file restarts its cursor");
try { await new CodexQuotaClient().ReadAsync(null, new CancellationToken(true)); throw new Exception("cancel ignored"); } catch (OperationCanceledException) { count++; Console.WriteLine("PASS canceled quota request never starts a process"); }
var plugins = Path.Combine(fixture, "plugins"); Directory.CreateDirectory(plugins);
File.WriteAllText(Path.Combine(plugins, "pack.json"), """{"entries":[{"text":"morning","when":"morning","action":"wave","cooldownSeconds":60},{"text":"night","when":"night"},{"text":"unsafe action","action":"execute"},{"text":"low","when":"low-quota"}]}""");
File.WriteAllText(Path.Combine(plugins, "broken.json"), "{broken");
var idle = new IdleInteractions(42); idle.Reload(plugins);
Check(idle.EntryCount == 3 && idle.IgnoredFiles == 1, "interaction pack ignores malformed files and unsupported actions");
Check(idle.Pick(new(14, false), now) is null, "conditional chatter stays quiet outside its conditions");
Check(idle.Pick(new(8, false), now)?.Text == "morning", "morning dialogue respects local-hour condition");
Check(idle.Pick(new(8, false), now.AddSeconds(10)) is null, "cooldown prevents repeated idle dialogue");
Check(idle.Pick(new(8, false), now.AddSeconds(61))?.Text == "morning", "small eligible packs resume after cooldown rather than staying blocked forever");
Check(idle.Pick(new(14, true), now)?.Text == "low", "low-quota dialogue only becomes eligible when allowance is low");
var fallback = new IdleInteractions(1); fallback.Reload(Path.Combine(fixture, "absent"));
Check(fallback.EntryCount == 3 && fallback.Pick(new(14, false), now) is not null, "missing interaction packs retain usable built-in dialogue");
var chatId = "11111111-1111-4111-8111-111111111111";
Check(ChatLink.Parse("codex://threads/" + chatId)?.ThreadId == chatId, "existing desktop chat links preserve their thread identity");
Check(ChatLink.Parse("https://chatgpt.com/c/22222222-2222-4222-8222-222222222222") is { IsLocal: false }, "ChatGPT history links remain cloud chats rather than borrowing local status");
Check(new[] { "file:///C:/Windows/system32/cmd.exe", "https://chatgpt.com.evil.test/c/" + chatId, "https://user@chatgpt.com/c/" + chatId, "codex://threads/new?prompt=send", "javascript:alert(1)", "codex://threads/" + chatId + "?prompt=send" }.All(x => ChatLink.Parse(x) is null), "chat links reject executable schemes, lookalike domains, user info and message query parameters");
var identified = new SessionState(); identified.Identify(chatId, "会话标题"); identified.Apply(Event("event_msg", "task_started")); identified.Apply(Event("response_item", "function_call", "mcp__node_repl__js"));
var withTitle = ActivityWatcher.Aggregate(new[] { identified }, now);
Check(withTitle.ThreadId == chatId && withTitle.Title == "会话标题" && withTitle.ToolName == "mcp__node_repl__js", "status cards carry the selected thread title and actual tool name");
identified.Apply(Event("event_msg", "user_message"));
Check(identified.ToolName is null, "new user turns clear the previous tool label");
var namedHome = Path.Combine(fixture, "named-home"); var namedRoot = Path.Combine(namedHome, "sessions"); var namedDay = Path.Combine(namedRoot, day.ToString("yyyy"), day.ToString("MM"), day.ToString("dd")); Directory.CreateDirectory(namedDay);
File.WriteAllText(Path.Combine(namedHome, "session_index.jsonl"), JsonSerializer.Serialize(new { id = chatId, thread_name = "固定的本机聊天" }) + "\n");
var namedFile = Path.Combine(namedDay, "rollout-" + chatId + ".jsonl"); File.WriteAllText(namedFile, Event("event_msg", "task_started") + "\n");
var namedWatcher = new ActivityWatcher(namedRoot); var pinned = namedWatcher.Poll(now, chatId);
Check(pinned.ThreadId == chatId && pinned.Title == "固定的本机聊天" && pinned.State == PetActivity.Thinking, "watcher resolves names from local index and binds exactly one thread");
var missingId = "22222222-2222-4222-8222-222222222222"; var notConnected = namedWatcher.Poll(now, missingId);
Check(notConnected.ThreadId == missingId && notConnected.IsStale && notConnected.ActiveCount == 0 && notConnected.Label == "未接入聊天状态", "unavailable pinned chats never display another thread's activity");
var childId = "33333333-3333-4333-8333-333333333333";
File.WriteAllText(Path.Combine(namedDay, "rollout-" + childId + ".jsonl"), JsonSerializer.Serialize(new { type = "session_meta", payload = new { id = childId, source = new { subagent = new { parent_thread_id = chatId } } } }) + "\n" + new string('x', 1_050_000) + "\n" + Event("response_item", "function_call", "request_user_input") + "\n");
var onlyMain = new ActivityWatcher(namedRoot); onlyMain.Poll(now);
Check(onlyMain.Recent.All(x => x.ThreadId != childId), "subagent activity stays out of recent-chat selection even when its metadata is outside the tailed megabyte");
if (args.Contains("--live"))
{
    var live = await new CodexQuotaClient().ReadAsync(null, CancellationToken.None);
    Check(live.Windows.Count > 0, "live Codex handshake and quota read");
    Console.WriteLine(JsonSerializer.Serialize(new { at = live.FetchedAt, windows = live.Windows.Select(w => new { w.Bucket, w.Minutes, w.UsedPercent, w.Remaining, w.ResetAt }) }));
}
Console.WriteLine($"{count} checks passed");
