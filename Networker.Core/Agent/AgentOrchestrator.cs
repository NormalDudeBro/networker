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
    private readonly WorkspaceService _workspace;
    private readonly CommandRunner _commands;

    public AgentOrchestrator(Func<IReadOnlyList<LlmMessage>, CancellationToken, Task<LlmResponse>> complete, WorkspaceService workspace)
    {
        _complete = complete;
        _workspace = workspace;
        _commands = new CommandRunner(workspace);
    }

    public event Action<AgentActivity>? Activity;

    public async Task<AgentResult> RunAsync(string goal, CancellationToken cancellationToken = default)
    {
        var activities = new List<AgentActivity>();
        var messages = new List<LlmMessage>
        {
            LlmMessage.System(SystemPrompt),
            LlmMessage.User($"Workspace: {_workspace.Root}\nGoal: {goal}"),
        };
        int failures = 0;
        string? previousCallHash = null;
        int repeatedCalls = 0;
        for (int call = 0; call < MaximumToolCalls; call++)
        {
            Record(new AgentActivity("thinking", "", Kind: "thinking", State: "running"));
            LlmResponse response = await _complete(messages, cancellationToken).ConfigureAwait(false);
            Record(new AgentActivity("thinking", "", Kind: "thinking", State: "completed"));
            messages.Add(LlmMessage.Assistant(response.Content));
            AgentInstruction instruction;
            try { instruction = Parse(response.Content); }
            catch (Exception ex)
            {
                failures++;
                Record(new AgentActivity("protocol", ex.Message, true, Kind: "error", State: "error"));
                messages.Add(LlmMessage.User("Tool error: Return exactly one valid JSON instruction object."));
                if (failures >= MaximumConsecutiveFailures) break;
                continue;
            }

            if (instruction.Action.Equals("finish", StringComparison.OrdinalIgnoreCase))
            {
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
                messages.Add(LlmMessage.User("Tool error: Repeated identical call denied. Inspect the prior result and choose a different action or finish."));
                continue;
            }

            try
            {
                string output = await RunAndRecordAsync(instruction, call, Record, cancellationToken).ConfigureAwait(false);
                failures = 0;
                messages.Add(LlmMessage.User("Tool result:\n" + output));
            }
            catch (Exception ex)
            {
                failures++;
                Record(new AgentActivity(instruction.Action, ex.Message, true, Kind: "error", State: "error"));
                messages.Add(LlmMessage.User("Tool error: " + ex.Message));
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
                    instruction.WorkingDirectory ?? string.Empty,
                    instruction.TimeoutSeconds ?? 120);
                record(new AgentActivity("command", label, Kind: "tool", State: "running", Path: command.WorkingDirectory, CallId: callId));
                long started = Stopwatch.GetTimestamp();
                AgentCommandResult result = await _commands.RunAsync(command, cancellationToken).ConfigureAwait(false);
                double seconds = Stopwatch.GetElapsedTime(started).TotalSeconds;
                string verdict = result.TimedOut ? "stopped" : result.ExitCode == 0 ? "done" : $"exit {result.ExitCode}";
                string output = JsonSerializer.Serialize(result);
                record(new AgentActivity("command", label, Kind: "tool", State: "completed", CallId: callId, Output: result.StandardOutput, Verdict: verdict, DurationSeconds: seconds));
                return output;
            }
            default:
            {
                string output = await ExecuteAsync(instruction, cancellationToken).ConfigureAwait(false);
                record(new AgentActivity(action, instruction.Path ?? instruction.Executable ?? output, Kind: "tool", State: "completed"));
                return output;
            }
        }
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
            "list" => JsonSerializer.Serialize(_workspace.List(instruction.Path ?? string.Empty)),
            "read" => _workspace.ReadText(Required(instruction.Path, "path")),
            "write" => Write(instruction),
            "delete" => Delete(instruction),
            "command" => JsonSerializer.Serialize(await _commands.RunAsync(new AgentCommand(
                Required(instruction.Executable, "executable"), instruction.Arguments ?? Array.Empty<string>(),
                instruction.WorkingDirectory ?? string.Empty, instruction.TimeoutSeconds ?? 120), cancellationToken).ConfigureAwait(false)),
            _ => throw new InvalidOperationException($"Unknown agent action '{instruction.Action}'."),
        };
    }

    private string Write(AgentInstruction instruction) { _workspace.WriteText(Required(instruction.Path, "path"), instruction.Content ?? string.Empty); return "File written."; }
    private string Delete(AgentInstruction instruction) { _workspace.DeleteFile(Required(instruction.Path, "path")); return "File deleted."; }
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
            "command" => new(StringComparer.Ordinal) { "action", "executable", "arguments", "workingDirectory", "timeoutSeconds" },
            "finish" => new(StringComparer.Ordinal) { "action", "summary" },
            _ => throw new InvalidOperationException($"Unknown agent action '{action}'."),
        };
        foreach (JsonProperty property in document.RootElement.EnumerateObject())
            if (!allowed.Contains(property.Name)) throw new InvalidOperationException($"Unexpected field '{property.Name}' in {action} instruction.");

        var instruction = JsonSerializer.Deserialize<AgentInstruction>(text, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = false, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow })
            ?? throw new InvalidOperationException("Model returned an empty instruction.");
        ValidateInstruction(instruction);
        return instruction with { Action = action };
    }

    public sealed record AgentInstruction(string Action, string? Path = null, string? Content = null, string? Executable = null, string[]? Arguments = null, string? WorkingDirectory = null, int? TimeoutSeconds = null, string? Summary = null);

    private static string RequiredString(JsonElement element, string name) => element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
        ? value.GetString()! : throw new InvalidOperationException($"Agent instruction requires string field '{name}'.");

    private static void ValidateInstruction(AgentInstruction instruction)
    {
        switch (instruction.Action.ToLowerInvariant())
        {
            case "list": WorkspaceService.ValidateRelativePath(instruction.Path ?? string.Empty, allowEmpty: true); break;
            case "read" or "delete": WorkspaceService.ValidateRelativePath(Required(instruction.Path, "path")); break;
            case "write":
                WorkspaceService.ValidateRelativePath(Required(instruction.Path, "path"));
                if (instruction.Content is null || instruction.Content.Length > 1_048_576) throw new InvalidOperationException("Write content is missing or too large.");
                break;
            case "command":
                if (instruction.Arguments is null) throw new InvalidOperationException("Command requires an argument array.");
                new CommandPolicy().Validate(new AgentCommand(Required(instruction.Executable, "executable"), instruction.Arguments, instruction.WorkingDirectory ?? string.Empty, instruction.TimeoutSeconds ?? 120));
                break;
            case "finish": if (string.IsNullOrWhiteSpace(instruction.Summary) || instruction.Summary.Length > 8192) throw new InvalidOperationException("Finish requires a bounded summary."); break;
        }
    }

    private const string SystemPrompt = """
        You are a bounded coding agent. Return exactly one JSON object per turn, without markdown.
        Available actions:
        {"action":"list","path":"relative/directory"}
        {"action":"read","path":"relative/file"}
        {"action":"write","path":"relative/file","content":"complete new file content"}
        {"action":"delete","path":"relative/file"}
        {"action":"command","executable":"dotnet","arguments":["test"],"workingDirectory":"","timeoutSeconds":120}
        {"action":"finish","summary":"concise result and verification"}
        Inspect before editing. Use only relative workspace paths. Commands run as the current user and may access the network. Do not request shells or command strings.
        """;
}
