namespace Networker.Core.Llm.ChatGpt;

public enum ChatGptSessionState
{
    Uninitialized,
    SignedOut,
    Ready,
    RateLimited,
    CompatibilityError,
    Offline,
}

public sealed record ChatGptStatus(
    ChatGptSessionState State,
    string Message,
    IReadOnlyList<LlmModelInfo> Models,
    LlmProviderCapabilities Capabilities,
    bool UsesAccountHistory = false);

public sealed record ChatGptTurnRequest(
    string Model,
    IReadOnlyList<LlmMessage> Messages,
    bool PreferTemporaryChat = true);

public interface IChatGptTransport
{
    Task<ChatGptStatus> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<LlmResponse> CompleteAsync(ChatGptTurnRequest request, CancellationToken cancellationToken = default);
    IAsyncEnumerable<string> StreamAsync(ChatGptTurnRequest request, CancellationToken cancellationToken = default);
    Task CancelAsync(CancellationToken cancellationToken = default);
}
