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
                    if (!string.IsNullOrEmpty(delta)) summary.Append(delta);
                    break;
                }
                case "item/started":
                case "item/completed":
                {
                    string detail = DescribeItem(notification.Params);
                    if (!string.IsNullOrWhiteSpace(detail))
                        Emit(notification.Method == "item/started" ? "item-start" : "item", detail);
                    break;
                }
                case "item/commandExecution/outputDelta":
                {
                    string? delta = OptionalString(notification.Params, "delta");
                    if (!string.IsNullOrEmpty(delta) && delta.Length <= 500)
                        Emit("command-output", Truncate(delta, 500));
                    break;
                }
                case "item/fileChange/patchUpdated":
                {
                    string path = OptionalString(notification.Params, "path")
                        ?? OptionalNestedString(notification.Params, "item", "path")
                        ?? "file";
                    Emit("file-change", path);
                    break;
                }
                case "turn/completed":
                {
                    string? status = OptionalString(notification.Params, "status")
                        ?? OptionalNestedString(notification.Params, "turn", "status")
                        ?? "completed";
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

    private void Emit(string action, string detail, bool isError = false)
        => Activity?.Invoke(new AgentActivity(action, detail, isError));

    private static string DescribeItem(JsonElement parameters)
    {
        if (parameters.TryGetProperty("item", out JsonElement item) && item.ValueKind == JsonValueKind.Object)
        {
            string type = OptionalString(item, "type") ?? "item";
            string? command = OptionalString(item, "command") ?? OptionalString(item, "path");
            return string.IsNullOrWhiteSpace(command) ? type : $"{type}: {Truncate(command, 200)}";
        }

        return OptionalString(parameters, "type") ?? "item";
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
