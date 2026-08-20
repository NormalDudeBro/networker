using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using System.Diagnostics;
using Networker.Core.Llm;

namespace Networker.Core.Agent;

public sealed record AgentResult(string Summary, IReadOnlyList<AgentActivity> Activities);

public sealed class AgentOrchestrator
{
    private const int MaximumToolCalls = 30;
    private const int MaximumConsecutiveFailures = 10;
    private readonly Func<IReadOnlyList<LlmMessage>, CancellationToken, Task<LlmResponse>> _complete;
    private readonly AgentFileSystem _files = new();
    private readonly CommandRunner _commands = new();
    private readonly List<LlmMessage> _messages = new() { LlmMessage.System(SystemPrompt) };
    private IReadOnlyList<AgentPlanItem>? _latestPlan;

    public AgentOrchestrator(Func<IReadOnlyList<LlmMessage>, CancellationToken, Task<LlmResponse>> complete) => _complete = complete;

    public event Action<AgentActivity>? Activity;

    public async Task<AgentResult> RunAsync(string goal, string? clientContext = null, CancellationToken cancellationToken = default)
    {
        var activities = new List<AgentActivity>();
        string request = string.IsNullOrWhiteSpace(clientContext)
            ? goal
            : clientContext + "\n\nUser request:\n" + goal;
        _messages.Add(LlmMessage.User(request));
        int failures = 0;
        string? previousCallHash = null;
        int repeatedCalls = 0;
        _latestPlan = null;
        for (int call = 0; call < MaximumToolCalls; call++)
        {
            Record(new AgentActivity("thinking", "", Kind: "thinking", State: "running"));
            LlmResponse response = await _complete(_messages, cancellationToken).ConfigureAwait(false);
            Record(new AgentActivity("thinking", "", Kind: "thinking", State: "completed"));
            _messages.Add(LlmMessage.Assistant(response.Content));
            AgentInstruction instruction;
            try { instruction = Parse(response.Content); }
            catch (Exception ex)
            {
                failures++;
                Record(new AgentActivity("protocol", ex.Message, true, Kind: "error", State: "error"));
                _messages.Add(LlmMessage.User("Tool error: Return exactly one valid JSON instruction object."));
                if (failures >= MaximumConsecutiveFailures) break;
                continue;
            }

            if (instruction.Action.Equals("finish", StringComparison.OrdinalIgnoreCase))
            {
                // Re-emit the last-known plan snapshot so the plan row settles
                // with the final per-item statuses and the running spinner stops.
                if (_latestPlan is not null)
                    Record(new AgentActivity("plan", "Plan", Kind: "activity", State: "completed", Plan: _latestPlan));
                Record(new AgentActivity("finish", instruction.Summary ?? "Agent completed.", Kind: "text", State: "completed"));
                return new AgentResult(instruction.Summary ?? "Agent completed.", activities);
            }

            string callHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(instruction))));
            repeatedCalls = callHash == previousCallHash ? repeatedCalls + 1 : 0;
            previousCallHash = callHash;
            if (repeatedCalls >= 2)
            {
                failures++;
                Record(new AgentActivity("protocol", "Repeated identical tool call denied.", true, Kind: "error", State: "error"));
                _messages.Add(LlmMessage.User("Tool error: Repeated identical call denied. Inspect the prior result and choose a different action or finish."));
                continue;
            }

            try
            {
                string output = await RunAndRecordAsync(instruction, call, Record, cancellationToken).ConfigureAwait(false);
                failures = 0;
                _messages.Add(LlmMessage.User("Tool result:\n" + output));
            }
            catch (Exception ex)
            {
                failures++;
                Record(new AgentActivity(instruction.Action, ex.Message, true, Kind: "error", State: "error"));
                _messages.Add(LlmMessage.User("Tool error: " + ex.Message));
                if (failures >= MaximumConsecutiveFailures) break;
            }
        }
        throw new InvalidOperationException("Agent stopped after reaching its bounded action/failure limit.");

        void Record(AgentActivity item) { activities.Add(item); Activity?.Invoke(item); }
    }

    /// <summary>
    /// Executes an instruction while emitting structured lifecycle events so the
    /// UI can show a running tool row, a verdict word, and a duration.
    /// </summary>
    private async Task<string> RunAndRecordAsync(AgentInstruction instruction, int call, Action<AgentActivity> record, CancellationToken cancellationToken)
    {
        string action = instruction.Action.ToLowerInvariant();
        switch (action)
        {
            case "list":
            case "read":
            {
                string output = await ExecuteAsync(instruction, cancellationToken).ConfigureAwait(false);
                record(new AgentActivity(action, instruction.Path ?? string.Empty, Kind: "activity", State: "completed"));
                return output;
            }
            case "delete":
            {
                string output = Delete(instruction);
                record(new AgentActivity("delete", instruction.Path ?? string.Empty, Kind: "edit", State: "completed", Path: instruction.Path));
                return output;
            }
            case "write":
            {
                string callId = $"write-{call}";
                record(new AgentActivity("write", instruction.Path ?? string.Empty, Kind: "edit", State: "running", Path: instruction.Path, CallId: callId));
                string output = Write(instruction);
                record(new AgentActivity("write", instruction.Path ?? string.Empty, Kind: "edit", State: "completed", Path: instruction.Path, CallId: callId, Output: instruction.Content));
                return output;
            }
            case "command":
            {
                string callId = $"cmd-{call}";
                string label = FormatCommand(instruction);
                var command = new AgentCommand(
                    Required(instruction.Executable, "executable"),
                    instruction.Arguments ?? Array.Empty<string>(),
                    instruction.TimeoutSeconds ?? 120);
                record(new AgentActivity("command", label, Kind: "tool", State: "running", CallId: callId,
                    CommandLine: label, IsTerminalStyle: true));
                long started = Stopwatch.GetTimestamp();
                AgentCommandResult result = await _commands.RunAsync(command, new CommandOutputStreamer(record, callId), cancellationToken).ConfigureAwait(false);
                double seconds = Stopwatch.GetElapsedTime(started).TotalSeconds;
                string verdict = result.TimedOut ? "stopped" : result.ExitCode == 0 ? "done" : $"exit {result.ExitCode}";
                string output = JsonSerializer.Serialize(result);
                record(new AgentActivity("command", label, Kind: "tool", State: "completed", CallId: callId,
                    Output: result.StandardOutput, Verdict: verdict, DurationSeconds: seconds, CommandLine: label,
                    ExitCode: result.ExitCode, IsTerminalStyle: true));
                return output;
            }
            case "plan":
            {
                _latestPlan = instruction.Plan;
                // Emits running so the plan row stays live (spinner on) while the
                // agent works through it; the final snapshot at finish settles it.
                record(new AgentActivity("plan", "Plan", Kind: "activity", State: "running", Plan: _latestPlan));
                return "Plan recorded.";
            }
            default:
            {
                string output = await ExecuteAsync(instruction, cancellationToken).ConfigureAwait(false);
                record(new AgentActivity(action, instruction.Path ?? instruction.Executable ?? output, Kind: "tool", State: "completed"));
                return output;
            }
        }
    }

    /// <summary>
    /// Bridges live <see cref="CommandOutputChunk"/> events onto the activity stream
    /// in the shape the UI already consumes: <c>command-output</c> / <c>IsStreaming</c>
    /// activities that append onto the matching command block.
    /// </summary>
    private sealed class CommandOutputStreamer : IProgress<CommandOutputChunk>
    {
        private readonly Action<AgentActivity> _record;
        private readonly string _callId;

        public CommandOutputStreamer(Action<AgentActivity> record, string callId)
        {
            _record = record;
            _callId = callId;
        }

        public void Report(CommandOutputChunk chunk)
            => _record(new AgentActivity("command-output", chunk.Text, Kind: "tool", State: "running", CallId: _callId, IsStreaming: true));
    }

    private static string FormatCommand(AgentInstruction instruction)
    {
        var parts = new List<string>(1 + (instruction.Arguments?.Length ?? 0)) { instruction.Executable ?? "command" };
        if (instruction.Arguments is not null) parts.AddRange(instruction.Arguments);
        return string.Join(' ', parts);
    }

    private async Task<string> ExecuteAsync(AgentInstruction instruction, CancellationToken cancellationToken)
    {
        return instruction.Action.ToLowerInvariant() switch
        {
            "list" => JsonSerializer.Serialize(_files.List(instruction.Path ?? string.Empty)),
            "read" => _files.ReadText(Required(instruction.Path, "path")),
            "write" => Write(instruction),
            "delete" => Delete(instruction),
            "command" => JsonSerializer.Serialize(await _commands.RunAsync(new AgentCommand(
                Required(instruction.Executable, "executable"), instruction.Arguments ?? Array.Empty<string>(),
                instruction.TimeoutSeconds ?? 120), null, cancellationToken).ConfigureAwait(false)),
            _ => throw new InvalidOperationException($"Unknown agent action '{instruction.Action}'."),
        };
    }

    private string Write(AgentInstruction instruction) { _files.WriteText(Required(instruction.Path, "path"), instruction.Content ?? string.Empty); return "File written."; }
    private string Delete(AgentInstruction instruction) { _files.DeleteFile(Required(instruction.Path, "path")); return "File deleted."; }
    private static string Required(string? value, string name) => string.IsNullOrWhiteSpace(value) ? throw new InvalidOperationException($"Agent instruction requires {name}.") : value;

    public static AgentInstruction Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 1_100_000 || text.Trim() != text)
            throw new InvalidOperationException("Model must return one bounded JSON instruction without surrounding text.");
        using JsonDocument document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 8, CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false });
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("Agent instruction must be a JSON object.");
        string action = RequiredString(document.RootElement, "action").ToLowerInvariant();
        HashSet<string> allowed = action switch
        {
            "list" or "read" or "delete" => new(StringComparer.Ordinal) { "action", "path" },
            "write" => new(StringComparer.Ordinal) { "action", "path", "content" },
            "command" => new(StringComparer.Ordinal) { "action", "executable", "arguments", "timeoutSeconds" },
            "plan" => new(StringComparer.Ordinal) { "action", "plan" },
            "finish" => new(StringComparer.Ordinal) { "action", "summary" },
            _ => throw new InvalidOperationException($"Unknown agent action '{action}'."),
        };
        foreach (JsonProperty property in document.RootElement.EnumerateObject())
            if (!allowed.Contains(property.Name)) throw new InvalidOperationException($"Unexpected field '{property.Name}' in {action} instruction.");

        var instruction = JsonSerializer.Deserialize<AgentInstruction>(text, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = false, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow })
            ?? throw new InvalidOperationException("Model returned an empty instruction.");
        ValidateInstruction(instruction);
        return instruction with { Action = action, Plan = instruction.Plan?.Select(item => item with { Status = NormalizePlanStatus(item.Status) }).ToArray() };
    }

    private static string NormalizePlanStatus(string status) => status.ToLowerInvariant().Trim() switch
    {
        "in_progress" or "in-progress" or "in progress" or "running" => "in_progress",
        "completed" or "complete" or "done" => "completed",
        "failed" or "error" => "failed",
        "skipped" => "skipped",
        "cancelled" or "canceled" => "skipped",
        _ => "pending",
    };

    public sealed record AgentInstruction(string Action, string? Path = null, string? Content = null, string? Executable = null, string[]? Arguments = null, int? TimeoutSeconds = null, string? Summary = null, AgentPlanItem[]? Plan = null);

    private static string RequiredString(JsonElement element, string name) => element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
        ? value.GetString()! : throw new InvalidOperationException($"Agent instruction requires string field '{name}'.");

    private static void ValidateInstruction(AgentInstruction instruction)
    {
        switch (instruction.Action.ToLowerInvariant())
        {
            case "list": AgentFileSystem.ResolvePath(instruction.Path ?? string.Empty); break;
            case "read" or "delete": AgentFileSystem.ResolvePath(Required(instruction.Path, "path")); break;
            case "write":
                AgentFileSystem.ResolvePath(Required(instruction.Path, "path"));
                if (instruction.Content is null || instruction.Content.Length > 1_048_576) throw new InvalidOperationException("Write content is missing or too large.");
                break;
            case "command":
                if (instruction.Arguments is null) throw new InvalidOperationException("Command requires an argument array.");
                Required(instruction.Executable, "executable");
                if (instruction.TimeoutSeconds is < 1 or > 900) throw new InvalidOperationException("Command timeout must be between 1 and 900 seconds.");
                break;
            case "plan":
                if (instruction.Plan is null || instruction.Plan.Length == 0 || instruction.Plan.Length > 64)
                    throw new InvalidOperationException("Plan requires 1-64 items.");
                foreach (AgentPlanItem item in instruction.Plan)
                {
                    if (string.IsNullOrWhiteSpace(item.Title) || item.Title.Length > 512) throw new InvalidOperationException("Plan item title is missing or too long.");
                    if (item.Status.Length > 32) throw new InvalidOperationException("Plan item status is too long.");
                }
                break;
            case "finish": if (string.IsNullOrWhiteSpace(instruction.Summary) || instruction.Summary.Length > 8192) throw new InvalidOperationException("Finish requires a bounded summary."); break;
        }
    }

    private const string SystemPrompt = """
        You are a command-capable assistant. Return exactly one JSON object per turn, without markdown.
        Available actions:
        {"action":"list","path":"C:\\path\\to\\directory"}
        {"action":"read","path":"C:\\path\\to\\file"}
        {"action":"write","path":"C:\\path\\to\\file","content":"complete new file content"}
        {"action":"delete","path":"C:\\path\\to\\file"}
        {"action":"command","executable":"cmd.exe","arguments":["/c","echo hello"],"timeoutSeconds":120}
        {"action":"plan","plan":[{"title":"step one","status":"in_progress"},{"title":"step two","status":"pending"}]}
        {"action":"finish","summary":"concise result and verification"}
        Inspect before editing. Absolute paths are allowed; relative paths resolve from the current Windows user profile. Commands and file tools run globally as the current user and may access the network.
        For multi-step goals, first return a plan of at most 8 ordered steps, then work through them, updating each step's status (pending / in_progress / completed / failed / skipped) as you go.
        """;
}
