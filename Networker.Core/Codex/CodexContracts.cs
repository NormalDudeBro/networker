using System.Text.Json;

namespace Networker.Core.Codex;

public sealed record CodexNotification(string Method, JsonElement Params);

public interface ICodexAppServerClient : IAsyncDisposable
{
    event Action<CodexNotification>? Notification;

    bool IsRunning { get; }
    string ComponentVersion { get; }

    Task StartAsync(CancellationToken cancellationToken = default);
    Task<JsonElement> RequestAsync(string method, object? parameters = null, CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}

public sealed record CodexAccount(
    bool IsConnected,
    string? Email,
    string? PlanType,
    string? AuthMode,
    string Message)
{
    public static CodexAccount Disconnected(string message = "Not connected") => new(false, null, null, null, message);
}

public sealed record CodexReasoningOption(string Id, string Description);

public sealed record CodexModelDescriptor(
    string Id,
    string DisplayName,
    string Description,
    bool IsDefault,
    string DefaultReasoningEffort,
    IReadOnlyList<CodexReasoningOption> SupportedReasoningEfforts,
    IReadOnlyList<string> InputModalities);

public sealed record CodexRateLimitWindow(double UsedPercent, int? WindowDurationMinutes, long? ResetsAtUnixSeconds);

public sealed record CodexUsage(CodexRateLimitWindow? Primary, CodexRateLimitWindow? Secondary, bool? SpendControlReached)
{
    public static CodexUsage Empty { get; } = new(null, null, null);
}

public sealed class CodexProtocolException : Exception
{
    public CodexProtocolException(string message, int? code = null) : base(message) => Code = code;
    public int? Code { get; }
}
