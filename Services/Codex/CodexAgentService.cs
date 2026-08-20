using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Networker.Core.Agent;
using Networker.Core.Codex;

namespace networker.Services.Codex;

/// <summary>
/// Continuous command-capable Assist turns via official codex-app-server.
/// </summary>
public sealed class CodexAgentService
{
    private readonly ICodexAppServerClient _client;
    private readonly CodexAccountService _account;
    private readonly object _sync = new();
    private CancellationTokenSource? _activeRun;
    private string? _activeThreadId;
    private string? _activeTurnId;
    private readonly HashSet<string> _reasoningItemsWithDeltas = new(StringComparer.Ordinal);

    public CodexAgentService(ICodexAppServerClient client, CodexAccountService account)
    {
        _client = client;
        _account = account;
    }

    public event Action<AgentActivity>? Activity;

    public async Task<AgentResult> RunAsync(string goal, string? clientContext = null, CancellationToken cancellationToken = default)
    {
        if (!_account.Account.IsConnected || !string.Equals(_account.Account.AuthMode, "chatgpt", StringComparison.Ordinal))
            throw new InvalidOperationException(_account.Account.IsConnected
                ? "Codex must be signed in with ChatGPT before using Assist."
                : _account.Account.Message);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_sync)
        {
            if (_activeRun is not null) throw new InvalidOperationException("An agent run is already active.");
            _activeRun = linked;
        }

        Action<CodexNotification>? handler = null;
        var summary = new StringBuilder();
        try
        {
            _reasoningItemsWithDeltas.Clear();
            await _client.StartAsync(linked.Token).ConfigureAwait(false);
            string threadId = await EnsureThreadAsync(linked.Token).ConfigureAwait(false);
            _activeThreadId = threadId;

            var turnDone = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            handler = notification => HandleNotification(notification, threadId, summary, turnDone);
            _client.Notification += handler;

            string input = string.IsNullOrWhiteSpace(clientContext)
                ? goal
                : clientContext + "\n\nUser request:\n" + goal;
            object turnParams = CodexProtocolPayloads.AgentTurnStart(
                threadId,
                input,
                AppSettings.SelectedModel,
                AppSettings.CodexReasoningEffort);

            JsonElement started = await _client.RequestAsync("turn/start", turnParams, linked.Token).ConfigureAwait(false);
            _activeTurnId = OptionalString(started, "turnId")
                ?? (started.TryGetProperty("turn", out JsonElement turn) ? OptionalString(turn, "id") : null);

            using CancellationTokenRegistration registration = linked.Token.Register(() =>
            {
                _ = InterruptQuietAsync(threadId, _activeTurnId);
            });

            string terminal = await turnDone.Task.WaitAsync(linked.Token).ConfigureAwait(false);
            if (terminal is "interrupted")
                throw new OperationCanceledException(linked.Token);
            if (terminal is "failed")
                throw new InvalidOperationException(summary.Length > 0 ? summary.ToString() : "Codex agent turn failed.");

            return new AgentResult(
                summary.Length > 0 ? summary.ToString().Trim() : "Agent run completed.",
                Array.Empty<AgentActivity>());
        }
        finally
        {
            if (handler is not null) _client.Notification -= handler;
            lock (_sync)
            {
                _activeRun = null;
                _activeThreadId = null;
                _activeTurnId = null;
                _reasoningItemsWithDeltas.Clear();
            }
        }
    }

    public void Stop()
    {
        string? threadId;
        string? turnId;
        lock (_sync)
        {
            _activeRun?.Cancel();
            threadId = _activeThreadId;
            turnId = _activeTurnId;
        }

        if (threadId is not null && turnId is not null)
            _ = InterruptQuietAsync(threadId, turnId);
    }

    public void ResetConversation()
    {
        AppSettings.CodexAssistThreadId = string.Empty;
        AppSettings.CodexAssistModel = string.Empty;
    }

    private async Task<string> EnsureThreadAsync(CancellationToken cancellationToken)
    {
        string model = AppSettings.SelectedModel;
        string existing = AppSettings.CodexAssistThreadId;
        if (!string.Equals(AppSettings.CodexAssistModel, model, StringComparison.Ordinal))
        {
            existing = string.Empty;
            ResetConversation();
        }

        if (!string.IsNullOrWhiteSpace(existing))
        {
            try
            {
                JsonElement resumed = await _client.RequestAsync("thread/resume", new { threadId = existing }, cancellationToken).ConfigureAwait(false);
                string? resumedId = OptionalString(resumed, "threadId")
                    ?? (resumed.TryGetProperty("thread", out JsonElement resumedThread) ? OptionalString(resumedThread, "id") : null);
                if (!string.IsNullOrWhiteSpace(resumedId)) return resumedId!;
            }
            catch
            {
                ResetConversation();
            }
        }

        object start = CodexProtocolPayloads.AgentThreadStart(model);

        Emit("thread", "Starting command-capable Codex Assist session.");
        JsonElement created = await _client.RequestAsync("thread/start", start, cancellationToken).ConfigureAwait(false);
        string threadId = OptionalString(created, "threadId")
            ?? (created.TryGetProperty("thread", out JsonElement thread) ? OptionalString(thread, "id") : null)
            ?? throw new CodexProtocolException("Codex did not return an agent thread.");
        AppSettings.CodexAssistThreadId = threadId;
        AppSettings.CodexAssistModel = model;
        return threadId;
    }

    private void HandleNotification(
        CodexNotification notification,
        string threadId,
        StringBuilder summary,
        TaskCompletionSource<string> turnDone)
    {
        try
        {
            if (!MatchesThread(notification.Params, threadId)) return;

            switch (notification.Method)
            {
                case "item/agentMessage/delta":
                {
                    string? delta = OptionalString(notification.Params, "delta");
                    if (!string.IsNullOrEmpty(delta))
                    {
                        summary.Append(delta);
                        Emit("agent-message", delta, Kind: "text", State: "running", IsStreaming: true);
                    }
                    break;
                }
                case "item/reasoning/delta":
                case "item/reasoning/summaryTextDelta":
                {
                    string? delta = OptionalString(notification.Params, "delta");
                    if (!string.IsNullOrEmpty(delta))
                    {
                        string? itemId = OptionalString(notification.Params, "itemId")
                            ?? OptionalString(notification.Params, "item_id");
                        if (itemId is not null) _reasoningItemsWithDeltas.Add(itemId);
                        Emit("thinking", delta, Kind: "thinking", State: "running", IsStreaming: true, CallId: itemId);
                    }
                    break;
                }
                case "item/started":
                case "item/completed":
                {
                    HandleItemNotification(notification.Params, completed: notification.Method == "item/completed");
                    break;
                }
                case "item/commandExecution/outputDelta":
                {
                    string? delta = OptionalString(notification.Params, "delta");
                    if (!string.IsNullOrEmpty(delta) && delta.Length <= 500)
                    {
                        string? itemId = OptionalString(notification.Params, "itemId")
                            ?? OptionalString(notification.Params, "item_id");
                        Emit("command-output", Truncate(delta, 500), Kind: "tool", State: "running", IsStreaming: true, CallId: itemId);
                    }
                    break;
                }
                case "item/fileChange/patchUpdated":
                {
                    string path = OptionalString(notification.Params, "path")
                        ?? OptionalNestedString(notification.Params, "item", "path")
                        ?? "file";
                    string? itemId = OptionalString(notification.Params, "itemId")
                        ?? OptionalString(notification.Params, "item_id")
                        ?? OptionalNestedString(notification.Params, "item", "id");
                    Emit("file-change", path, Kind: "edit", State: "running", Path: path, CallId: itemId);
                    break;
                }
                case "turn/completed":
                {
                    string? status = OptionalString(notification.Params, "status")
                        ?? OptionalNestedString(notification.Params, "turn", "status")
                        ?? "completed";
                    Emit("turn", status, IsError: status is "failed" or "error", Kind: "turn", State: "completed");
                    if (status is "completed" or "interrupted" or "failed")
                        turnDone.TrySetResult(status);
                    else
                        turnDone.TrySetResult("completed");
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            turnDone.TrySetException(ex);
        }
    }

    /// <summary>
    /// Maps Codex item notifications onto structured activity events (thinking,
    /// tool, command, edit, text) that the chat surface renders as turn blocks.
    /// </summary>
    private void HandleItemNotification(JsonElement parameters, bool completed)
    {
        string state = completed ? "completed" : "running";
        string type = OptionalNestedString(parameters, "item", "type") ?? OptionalString(parameters, "type") ?? "item";
        string? itemId = OptionalNestedString(parameters, "item", "id")
            ?? OptionalString(parameters, "itemId")
            ?? OptionalString(parameters, "item_id");

        switch (type)
        {
            case "reasoning":
            {
                // Some app-server versions provide the readable reasoning only
                // on item/completed instead of streaming summaryTextDelta.
                string reasoning = ExtractReasoningSummary(parameters);
                if (completed && reasoning.Length > 0
                    && (itemId is null || !_reasoningItemsWithDeltas.Contains(itemId)))
                {
                    Emit("thinking", reasoning, Kind: "thinking", State: "running", IsStreaming: true, CallId: itemId);
                }
                Emit("thinking", "", Kind: "thinking", State: state, CallId: itemId);
                break;
            }
            case "agentMessage":
            case "agent_message":
                Emit("agent-message", "", Kind: "text", State: state, CallId: itemId);
                break;
            case "toolCall":
            case "tool_call":
            {
                string title = OptionalNestedString(parameters, "item", "title")
                    ?? OptionalNestedString(parameters, "item", "name")
                    ?? "tool call";
                Emit("tool-call", title, Kind: "tool", State: state, CallId: itemId);
                break;
            }
            case "commandExecution":
            case "command_execution":
            {
                string command = DescribeCommand(parameters);
                if (completed)
                {
                    string status = OptionalNestedString(parameters, "item", "status") ?? "completed";
                    int? exitCode = OptionalInt(parameters, "exitCode")
                        ?? OptionalNestedInt(parameters, "item", "exitCode")
                        ?? OptionalInt(parameters, "exit_code")
                        ?? OptionalNestedInt(parameters, "item", "exit_code");
                    string verdict = status switch
                    {
                        "failed" => exitCode is int failedCode ? $"exit {failedCode}" : "failed",
                        "interrupted" => "stopped",
                        _ when exitCode is int code && code != 0 => $"exit {code}",
                        _ => "done",
                    };
                    // Best-effort stdout capture from the protocol when present (some
                    // app-server builds attach it to the completed command item).
                    string? output = OptionalNestedString(parameters, "item", "output")
                        ?? OptionalNestedString(parameters, "item", "aggregatedOutput")
                        ?? OptionalNestedString(parameters, "item", "output_text")
                        ?? OptionalNestedString(parameters, "item", "stdout");
                    int? durationMs = OptionalNestedInt(parameters, "item", "durationMs");
                    Emit("command", command, Kind: "tool", State: "completed", CallId: itemId,
                        Verdict: verdict, Output: output, CommandLine: command, ExitCode: exitCode,
                        DurationSeconds: durationMs is int milliseconds ? milliseconds / 1000d : null,
                        IsTerminalStyle: true);
                }
                else
                {
                    Emit("command", command, Kind: "tool", State: "running", CallId: itemId,
                        CommandLine: command, IsTerminalStyle: true);
                }
                break;
            }
            case "todos":
            case "plan":
            {
                // Codex todo/plan items map onto the same plan protocol as the local
                // orchestrator so the UI renders one coalesced PlanBlock per turn.
                AgentPlanItem[]? plan = ExtractPlan(parameters);
                Emit("plan", "Plan", Kind: "activity", State: state, Plan: plan, CallId: itemId);
                break;
            }
            case "fileChange":
            case "file_change":
            {
                string path = OptionalNestedString(parameters, "item", "path") ?? OptionalString(parameters, "path") ?? "file";
                string? diff = OptionalNestedString(parameters, "item", "diff");
                int? additions = null;
                int? deletions = null;
                if (diff is not null) ComputeDiffStats(diff, out additions, out deletions);
                Emit("file-change", path, Kind: "edit", State: state, Output: diff, Path: path, CallId: itemId, Additions: additions, Deletions: deletions);
                break;
            }
            case "userMessage":
            case "user_message":
                break;
            default:
                Emit("item", type, Kind: "activity", State: state, CallId: itemId);
                break;
        }
    }

    private void Emit(
        string action,
        string detail,
        bool IsError = false,
        string? Kind = null,
        string? State = null,
        string? Output = null,
        string? Path = null,
        string? CallId = null,
        int? Additions = null,
        int? Deletions = null,
        string? Verdict = null,
        AgentPlanItem[]? Plan = null,
        bool IsStreaming = false,
        string? CommandLine = null,
        int? ExitCode = null,
        double? DurationSeconds = null,
        bool IsTerminalStyle = false)
        => Activity?.Invoke(new AgentActivity(action, detail, IsError, Kind, State, Output, Path, CallId,
            Additions, Deletions, Verdict, DurationSeconds, IsStreaming, CommandLine, ExitCode,
            IsTerminalStyle, Plan));

    /// <summary>
    /// Best-effort extraction of a codex-app-server <c>todos</c> item into the plan
    /// protocol. Returns null when the payload has no usable todo array.
    /// </summary>
    private static AgentPlanItem[]? ExtractPlan(JsonElement parameters)
    {
        JsonElement item = parameters.TryGetProperty("item", out JsonElement nested) && nested.ValueKind == JsonValueKind.Object
            ? nested
            : parameters;
        if (!item.TryGetProperty("todos", out JsonElement todos) || todos.ValueKind != JsonValueKind.Array) return null;
        var items = new List<AgentPlanItem>();
        foreach (JsonElement entry in todos.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) continue;
            string? title = OptionalString(entry, "title");
            string status = OptionalString(entry, "status") ?? "pending";
            if (!string.IsNullOrWhiteSpace(title)) items.Add(new AgentPlanItem(title, status));
        }
        return items.Count > 0 ? items.ToArray() : null;
    }

    private static string DescribeCommand(JsonElement parameters)
    {
        JsonElement item = parameters.TryGetProperty("item", out JsonElement nested) && nested.ValueKind == JsonValueKind.Object
            ? nested
            : parameters;
        if (item.TryGetProperty("command", out JsonElement command))
        {
            if (command.ValueKind == JsonValueKind.String) return Truncate(command.GetString() ?? "command", 200);
            if (command.ValueKind == JsonValueKind.Array)
            {
                var parts = new List<string>();
                foreach (JsonElement element in command.EnumerateArray())
                {
                    if (element.ValueKind == JsonValueKind.String) parts.Add(element.GetString()!);
                    else if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("text", out JsonElement text) && text.ValueKind == JsonValueKind.String)
                        parts.Add(text.GetString()!);
                }
                if (parts.Count > 0) return Truncate(string.Join(' ', parts), 200);
            }
        }
        return OptionalString(parameters, "command") ?? "command";
    }

    private static string ExtractReasoningSummary(JsonElement parameters)
    {
        JsonElement item = parameters.TryGetProperty("item", out JsonElement nested)
            && nested.ValueKind == JsonValueKind.Object ? nested : parameters;
        if (!item.TryGetProperty("summary", out JsonElement summary) || summary.ValueKind != JsonValueKind.Array)
            return string.Empty;

        var parts = new List<string>();
        foreach (JsonElement entry in summary.EnumerateArray())
        {
            if (entry.ValueKind == JsonValueKind.Object
                && entry.TryGetProperty("text", out JsonElement text)
                && text.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(text.GetString()))
            {
                parts.Add(text.GetString()!);
            }
        }
        return string.Join("\n\n", parts);
    }

    private static int? OptionalInt(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(name, out JsonElement value)
           && value.ValueKind == JsonValueKind.Number
           && value.TryGetInt32(out int number)
            ? number
            : null;

    private static int? OptionalNestedInt(JsonElement element, string parent, string name)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(parent, out JsonElement nested)
            ? OptionalInt(nested, name)
            : null;

    private static void ComputeDiffStats(string diff, out int? additions, out int? deletions)
    {
        int added = 0;
        int deleted = 0;
        foreach (string line in diff.Split('\n'))
        {
            if (line.StartsWith("+++", StringComparison.Ordinal) || line.StartsWith("---", StringComparison.Ordinal)) continue;
            if (line.StartsWith("+", StringComparison.Ordinal)) added++;
            else if (line.StartsWith("-", StringComparison.Ordinal)) deleted++;
        }
        additions = added > 0 ? added : null;
        deletions = deleted > 0 ? deleted : null;
    }

    private async Task InterruptQuietAsync(string threadId, string? turnId)
    {
        if (string.IsNullOrWhiteSpace(turnId)) return;
        try
        {
            await _client.RequestAsync("turn/interrupt", new { threadId, turnId }).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private static bool MatchesThread(JsonElement parameters, string threadId)
    {
        string? value = OptionalString(parameters, "threadId")
            ?? OptionalNestedString(parameters, "thread", "id")
            ?? OptionalNestedString(parameters, "turn", "threadId");
        return value is null || value == threadId;
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max] + "…";

    private static string? OptionalString(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(name, out JsonElement value)
           && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? OptionalNestedString(JsonElement element, string parent, string name)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(parent, out JsonElement nested)
            ? OptionalString(nested, name)
            : null;
}
