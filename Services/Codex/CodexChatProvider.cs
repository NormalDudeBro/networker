using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Networker.Core.Codex;
using Networker.Core.Llm;

namespace networker.Services.Codex;

/// <summary>
/// Ordinary chat adapter over official codex-app-server threads/turns.
/// Does not handle OAuth tokens or API keys.
/// </summary>
public sealed class CodexChatProvider : ILlmProvider
{
    private readonly ICodexAppServerClient _client;
    private readonly CodexAccountService _account;
    private readonly SemaphoreSlim _turnLock = new(1, 1);
    private string _model;
    private string? _activeTurnId;

    public CodexChatProvider(ICodexAppServerClient client, CodexAccountService account)
    {
        _client = client;
        _account = account;
        _model = AppSettings.SelectedModel;
    }

    public LlmProviderKind Kind => LlmProviderKind.Codex;
    public string Name => "OpenAI Codex";
    public LlmProviderCapabilities Capabilities => LlmProviderCapabilities.Streaming | LlmProviderCapabilities.Models | LlmProviderCapabilities.Tools;
    public bool SupportsStreaming => true;
    public bool SupportsTools => true;

    public string Model
    {
        get => _model;
        set => _model = value ?? string.Empty;
    }

    public async Task<LlmResponse> CompleteAsync(IReadOnlyList<LlmMessage> messages, CancellationToken cancellationToken = default)
    {
        var content = new StringBuilder();
        await foreach (string delta in StreamAsync(messages, cancellationToken).ConfigureAwait(false))
            content.Append(delta);
        return new LlmResponse { Provider = Name, Model = Model, Content = content.ToString() };
    }

    public async IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<LlmMessage> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        EnsureChatgptAccount();
        if (string.IsNullOrWhiteSpace(Model))
            throw new LlmException("Select a Codex model before sending.") { Provider = Name };

        LlmMessage? lastUser = messages.LastOrDefault(message =>
            string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase));
        if (lastUser is null || string.IsNullOrWhiteSpace(lastUser.Content))
            throw new LlmException("A user message is required.") { Provider = Name };

        await _turnLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        Channel<string> deltas = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });
        Action<CodexNotification>? handler = null;
        string? turnId = null;
        string threadId = string.Empty;
        try
        {
            await _client.StartAsync(cancellationToken).ConfigureAwait(false);
            threadId = await EnsureThreadAsync(messages, cancellationToken).ConfigureAwait(false);

            var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            handler = notification =>
            {
                try
                {
                    if (notification.Method == "item/agentMessage/delta")
                    {
                        if (!MatchesThread(notification.Params, threadId)) return;
                        string? delta = OptionalString(notification.Params, "delta");
                        if (!string.IsNullOrEmpty(delta)) deltas.Writer.TryWrite(delta);
                        return;
                    }

                    if (notification.Method == "turn/completed")
                    {
                        if (!MatchesThread(notification.Params, threadId)) return;
                        string? status = OptionalString(notification.Params, "status")
                            ?? (notification.Params.TryGetProperty("turn", out JsonElement turn)
                                ? OptionalString(turn, "status")
                                : null);
                        if (status is null or "completed")
                            completion.TrySetResult(null);
                        else if (status is "interrupted")
                            completion.TrySetCanceled();
                        else
                        {
                            string error = OptionalString(notification.Params, "error")
                                ?? OptionalNestedString(notification.Params, "turn", "error")
                                ?? "Codex turn failed.";
                            completion.TrySetException(new LlmException(error) { Provider = Name, MayHaveSubmittedRequest = true });
                        }
                    }
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            };
            _client.Notification += handler;

            object turnParams = BuildTurnStart(threadId, lastUser.Content);
            JsonElement started = await _client.RequestAsync("turn/start", turnParams, cancellationToken).ConfigureAwait(false);
            turnId = OptionalString(started, "turnId")
                ?? (started.TryGetProperty("turn", out JsonElement turnElement) ? OptionalString(turnElement, "id") : null);
            _activeTurnId = turnId;

            using CancellationTokenRegistration registration = cancellationToken.Register(() =>
            {
                _ = InterruptQuietAsync(threadId, turnId);
                completion.TrySetCanceled(cancellationToken);
                deltas.Writer.TryComplete();
            });

            var consume = Task.Run(async () =>
            {
                try
                {
                    string? error = await completion.Task.ConfigureAwait(false);
                    if (error is not null)
                        throw new LlmException(error) { Provider = Name, MayHaveSubmittedRequest = true };
                    deltas.Writer.TryComplete();
                }
                catch (Exception ex)
                {
                    deltas.Writer.TryComplete(ex);
                }
            }, CancellationToken.None);

            await foreach (string delta in deltas.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                yield return delta;

            await consume.ConfigureAwait(false);
        }
        finally
        {
            if (handler is not null) _client.Notification -= handler;
            _activeTurnId = null;
            _turnLock.Release();
        }
    }

    public async Task<IReadOnlyList<LlmModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        if (!_account.Account.IsConnected)
            await _account.InitializeAsync(cancellationToken).ConfigureAwait(false);
        if (!_account.Account.IsConnected)
            throw new LlmException(_account.Account.Message) { Provider = Name };
        return _account.Models.Select(model => new LlmModelInfo { Id = model.Id, Name = model.DisplayName }).ToList();
    }

    public async Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        if (!_account.Account.IsConnected)
            await _account.InitializeAsync(cancellationToken).ConfigureAwait(false);
        return _account.Account.IsConnected && string.Equals(_account.Account.AuthMode, "chatgpt", StringComparison.Ordinal);
    }

    private async Task<string> EnsureThreadAsync(IReadOnlyList<LlmMessage> messages, CancellationToken cancellationToken)
    {
        string existing = AppSettings.CodexChatThreadId;
        if (!string.IsNullOrWhiteSpace(existing))
        {
            try
            {
                JsonElement resumed = await _client.RequestAsync("thread/resume", new { threadId = existing }, cancellationToken).ConfigureAwait(false);
                string? id = OptionalString(resumed, "threadId")
                    ?? (resumed.TryGetProperty("thread", out JsonElement thread) ? OptionalString(thread, "id") : null);
                if (!string.IsNullOrWhiteSpace(id)) return id!;
            }
            catch
            {
                AppSettings.CodexChatThreadId = string.Empty;
            }
        }

        string? developer = BuildDeveloperInstructions(messages);
        object start = CodexProtocolPayloads.ChatThreadStart(Model, AppSettings.CodexReasoningEffort, developer);

        JsonElement created = await _client.RequestAsync("thread/start", start, cancellationToken).ConfigureAwait(false);
        string threadId = OptionalString(created, "threadId")
            ?? (created.TryGetProperty("thread", out JsonElement threadElement) ? OptionalString(threadElement, "id") : null)
            ?? throw new LlmException("Codex did not return a chat thread.") { Provider = Name };
        AppSettings.CodexChatThreadId = threadId;
        return threadId;
    }

    private object BuildTurnStart(string threadId, string text)
        => CodexProtocolPayloads.TurnStart(threadId, text, Model, AppSettings.CodexReasoningEffort);

    private static string? BuildDeveloperInstructions(IReadOnlyList<LlmMessage> messages)
    {
        var parts = new List<string>();
        foreach (LlmMessage message in messages)
        {
            if (string.Equals(message.Role, "system", StringComparison.OrdinalIgnoreCase)
                || string.Equals(message.Role, "developer", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(message.Content))
                    parts.Add(message.Content);
            }
        }

        if (parts.Count == 0) return null;
        string joined = string.Join("\n\n", parts);
        return joined.Length <= 16_384 ? joined : joined[..16_384];
    }

    private void EnsureChatgptAccount()
    {
        if (!_account.Account.IsConnected || !string.Equals(_account.Account.AuthMode, "chatgpt", StringComparison.Ordinal))
            throw new LlmException(_account.Account.IsConnected
                ? "Codex must be signed in with ChatGPT."
                : _account.Account.Message) { Provider = Name };
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
