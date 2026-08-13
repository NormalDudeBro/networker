namespace Networker.Core.Llm;

public interface ILlmProvider
{
    LlmProviderKind Kind { get; }
    string Name { get; }
    string Model { get; set; }
    LlmProviderCapabilities Capabilities { get; }
    bool SupportsStreaming { get; }
    bool SupportsTools { get; }

    Task<LlmResponse> CompleteAsync(IReadOnlyList<LlmMessage> messages, CancellationToken cancellationToken = default);

    IAsyncEnumerable<string> StreamAsync(IReadOnlyList<LlmMessage> messages, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LlmModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default);

    Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default);
}

