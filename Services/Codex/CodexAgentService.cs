using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Networker.Core.Agent;
using Networker.Core.Codex;

namespace networker.Services.Codex;

/// <summary>
/// Workspace Agent turns via official codex-app-server with workspace-write sandbox
/// and approvalPolicy never. Network remains an explicit workspace capability.
/// </summary>
public sealed class CodexAgentService
{
    private readonly ICodexAppServerClient _client;
    private readonly CodexAccountService _account;
    private readonly object _sync = new();
    private CancellationTokenSource? _activeRun;
    private string? _activeThreadId;
    private string? _activeTurnId;

    public CodexAgentService(ICodexAppServerClient client, CodexAccountService account)
    {
        _client = client;
        _account = account;
    }

    public event Action<AgentActivity>? Activity;

    public async Task<AgentResult> RunAsync(string workspacePath, string goal, CancellationToken cancellationToken = default)
    {
        if (!_account.Account.IsConnected || !string.Equals(_account.Account.AuthMode, "chatgpt", StringComparison.Ordinal))
            throw new InvalidOperationException(_account.Account.IsConnected
                ? "Codex must be signed in with ChatGPT before Agent mode."
                : _account.Account.Message);

        string[] protectedRoots =
        {
            AppSettings.GetLocalDataDirectory(),
            AppContext.BaseDirectory,
            Path.Combine(AppSettings.GetLocalDataDirectory(), "Codex"),
        };
        using var workspace = new WorkspaceService(workspacePath, protectedRoots);
        string cwd = workspace.Root;

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
            await _client.StartAsync(linked.Token).ConfigureAwait(false);
            string threadId = await StartThreadAsync(cwd, linked.Token).ConfigureAwait(false);
            _activeThreadId = threadId;

            var turnDone = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            handler = notification => HandleNotification(notification, threadId, summary, turnDone);
            _client.Notification += handler;

            bool network = IsNetworkEnabledFor(cwd);
            object turnParams = CodexProtocolPayloads.AgentTurnStart(
                threadId,
                goal,
                AppSettings.SelectedModel,
                AppSettings.CodexReasoningEffort,
                cwd,
                network);

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

    private async Task<string> StartThreadAsync(string cwd, CancellationToken cancellationToken)
    {
        bool network = IsNetworkEnabledFor(cwd);
        object start = CodexProtocolPayloads.AgentThreadStart(AppSettings.SelectedModel, cwd, network);

        Emit("thread", $"Starting Codex agent in {cwd} (sandbox=workspace-write, network={(network ? "enabled" : "restricted")}).");
        JsonElement created = await _client.RequestAsync("thread/start", start, cancellationToken).ConfigureAwait(false);
        return OptionalString(created, "threadId")
            ?? (created.TryGetProperty("thread", out JsonElement thread) ? OptionalString(thread, "id") : null)
            ?? throw new CodexProtocolException("Codex did not return an agent thread.");
    }

    private static bool IsNetworkEnabledFor(string cwd)
        => AppSettings.CodexAgentNetworkEnabled
           && string.Equals(AppSettings.CodexAgentAuthorizedWorkspace, cwd, StringComparison.OrdinalIgnoreCase);

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
                {
                    string? delta = OptionalString(notification.Params, "delta");
                    if (!string.IsNullOrEmpty(delta))
                    {
                        string? itemId = OptionalString(notification.Params, "item_id");
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
                        string? itemId = OptionalString(notification.Params, "item_id");
                        Emit("command-output", Truncate(delta, 500), Kind: "tool", State: "running", IsStreaming: true, CallId: itemId);
                    }
                    break;
                }
                case "item/fileChange/patchUpdated":
                {
                    string path = OptionalString(notification.Params, "path")
                        ?? OptionalNestedString(notification.Params, "item", "path")
                        ?? "file";
                    string? itemId = OptionalString(notification.Params, "item_id")
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
        string? itemId = OptionalNestedString(parameters, "item", "id") ?? OptionalString(parameters, "item_id");

        switch (type)
        {
            case "reasoning":
                Emit("thinking", "", Kind: "thinking", State: state, CallId: itemId);
                break;
            case "agent_message":
                Emit("agent-message", "", Kind: "text", State: state, CallId: itemId);
                break;
            case "tool_call":
            {
                string title = OptionalNestedString(parameters, "item", "title")
                    ?? OptionalNestedString(parameters, "item", "name")
                    ?? "tool call";
                Emit("tool-call", title, Kind: "tool", State: state, CallId: itemId);
                break;
            }
            case "command_execution":
            {
                string command = DescribeCommand(parameters);
                if (completed)
                {
                    string status = OptionalNestedString(parameters, "item", "status") ?? "completed";
                    int? exitCode = OptionalInt(parameters, "exit_code") ?? OptionalNestedInt(parameters, "item", "exit_code");
                    string verdict = status switch
                    {
                        "failed" => exitCode is int failedCode ? $"exit {failedCode}" : "failed",
                        "interrupted" => "stopped",
                        _ when exitCode is int code && code != 0 => $"exit {code}",
                        _ => "done",
                    };
                    Emit("command", command, Kind: "tool", State: "completed", CallId: itemId, Verdict: verdict);
                }
                else
                {
                    Emit("command", command, Kind: "tool", State: "running", CallId: itemId);
                }
                break;
            }
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
        bool IsStreaming = false)
        => Activity?.Invoke(new AgentActivity(action, detail, IsError, Kind, State, Output, Path, CallId, Additions, Deletions, Verdict, null, IsStreaming));

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
