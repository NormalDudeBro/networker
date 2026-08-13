namespace Networker.Core.Llm.ChatGpt;

public sealed class ChatGptProvider : ILlmProvider
{
    private readonly IChatGptTransport _transport;
    private LlmProviderCapabilities _capabilities = LlmProviderCapabilities.Streaming | LlmProviderCapabilities.Models;
    private string _model;

    public ChatGptProvider(LlmConfig config, IChatGptTransport transport)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _model = config.ChatGptModel ?? "auto";
    }

    public LlmProviderKind Kind => LlmProviderKind.ChatGpt;
    public string Name => "ChatGPT Plus / Pro";
    public LlmProviderCapabilities Capabilities => _capabilities;
    public bool SupportsStreaming => true;
    public bool SupportsTools => (_capabilities & LlmProviderCapabilities.Tools) != 0;

    public string Model
    {
        get => _model;
        set => _model = string.IsNullOrWhiteSpace(value) ? "auto" : value;
    }

    public Task<LlmResponse> CompleteAsync(IReadOnlyList<LlmMessage> messages, CancellationToken cancellationToken = default)
        => _transport.CompleteAsync(new ChatGptTurnRequest(Model, messages), cancellationToken);

    public IAsyncEnumerable<string> StreamAsync(IReadOnlyList<LlmMessage> messages, CancellationToken cancellationToken = default)
        => _transport.StreamAsync(new ChatGptTurnRequest(Model, messages), cancellationToken);

    public async Task<IReadOnlyList<LlmModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        ChatGptStatus status = await _transport.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        _capabilities = status.Capabilities;
        EnsureReady(status);
        return status.Models;
    }

    public async Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        ChatGptStatus status = await _transport.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        _capabilities = status.Capabilities;
        return status.State == ChatGptSessionState.Ready;
    }

    private void EnsureReady(ChatGptStatus status)
    {
        if (status.State == ChatGptSessionState.Ready) return;
        throw new LlmException(status.Message) { Provider = Name };
    }
}
