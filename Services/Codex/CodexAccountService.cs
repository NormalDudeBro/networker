using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Networker.Core.Codex;

namespace networker.Services.Codex;

public sealed class CodexAccountService
{
    private readonly ICodexAppServerClient _client;
    private readonly SemaphoreSlim _refresh = new(1, 1);
    private string? _loginId;

    public CodexAccountService(ICodexAppServerClient client)
    {
        _client = client;
        _client.Notification += Client_Notification;
    }

    public event Action? Changed;

    public CodexAccount Account { get; private set; } = CodexAccount.Disconnected();
    public CodexUsage Usage { get; private set; } = CodexUsage.Empty;
    public IReadOnlyList<CodexModelDescriptor> Models { get; private set; } = Array.Empty<CodexModelDescriptor>();
    public bool IsLoginPending => _loginId is not null;
    public string ComponentVersion => _client.ComponentVersion;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.StartAsync(cancellationToken).ConfigureAwait(false);
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Account = CodexAccount.Disconnected(SafeMessage(ex));
            Changed?.Invoke();
        }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _refresh.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            JsonElement result = await _client.RequestAsync("account/read", new { refreshToken = false }, cancellationToken).ConfigureAwait(false);
            if (!result.TryGetProperty("account", out JsonElement account) || account.ValueKind == JsonValueKind.Null)
            {
                Account = CodexAccount.Disconnected();
                Models = Array.Empty<CodexModelDescriptor>();
                Usage = CodexUsage.Empty;
                return;
            }

            string type = account.TryGetProperty("type", out JsonElement typeValue) ? typeValue.GetString() ?? string.Empty : string.Empty;
            if (!type.Equals("chatgpt", StringComparison.Ordinal))
            {
                Account = CodexAccount.Disconnected("Codex is not signed in with ChatGPT. Sign out of the current Codex profile and reconnect.");
                Models = Array.Empty<CodexModelDescriptor>();
                return;
            }

            string? email = account.TryGetProperty("email", out JsonElement emailValue) && emailValue.ValueKind == JsonValueKind.String ? emailValue.GetString() : null;
            string? plan = account.TryGetProperty("planType", out JsonElement planValue) && planValue.ValueKind == JsonValueKind.String ? planValue.GetString() : null;
            Account = new CodexAccount(true, email, plan, "chatgpt", "Connected to ChatGPT");
            EnsureNoPlaintextCredentials();
            Models = await LoadModelsAsync(cancellationToken).ConfigureAwait(false);
            Usage = await LoadUsageAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _refresh.Release();
            Changed?.Invoke();
        }
    }

    public async Task<Uri> SignInAsync(CancellationToken cancellationToken = default)
    {
        if (_loginId is not null) throw new InvalidOperationException("A ChatGPT sign-in is already in progress.");
        await _client.StartAsync(cancellationToken).ConfigureAwait(false);
        JsonElement result = await _client.RequestAsync("account/login/start", new
        {
            type = "chatgpt",
            useHostedLoginSuccessPage = true,
            appBrand = "codex",
        }, cancellationToken).ConfigureAwait(false);
        string loginId = RequiredString(result, "loginId");
        Uri uri = new(RequiredString(result, "authUrl"), UriKind.Absolute);
        if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) || !IsAllowedLoginHost(uri.Host))
            throw new CodexProtocolException("Codex returned an unexpected sign-in address.");
        _loginId = loginId;
        Changed?.Invoke();

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch
        {
            // Keep the login pending so Settings can expose the validated link and Cancel action.
        }
        return uri;
    }

    public async Task CancelSignInAsync(CancellationToken cancellationToken = default)
    {
        string? loginId = _loginId;
        if (loginId is null) return;
        await _client.RequestAsync("account/login/cancel", new { loginId }, cancellationToken).ConfigureAwait(false);
        _loginId = null;
        Changed?.Invoke();
    }

    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        if (_loginId is not null) await CancelSignInAsync(cancellationToken).ConfigureAwait(false);
        await _client.RequestAsync("account/logout", null, cancellationToken).ConfigureAwait(false);
        Account = CodexAccount.Disconnected();
        Models = Array.Empty<CodexModelDescriptor>();
        Usage = CodexUsage.Empty;
        AppSettings.CodexChatThreadId = string.Empty;
        AppSettings.CodexAssistThreadId = string.Empty;
        AppSettings.CodexAssistModel = string.Empty;
        Changed?.Invoke();
    }

    private async Task<IReadOnlyList<CodexModelDescriptor>> LoadModelsAsync(CancellationToken cancellationToken)
    {
        var models = new List<CodexModelDescriptor>();
        string? cursor = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        do
        {
            JsonElement result = await _client.RequestAsync("model/list", new { cursor, limit = 100, includeHidden = false }, cancellationToken).ConfigureAwait(false);
            if (result.TryGetProperty("data", out JsonElement data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in data.EnumerateArray())
                {
                    if (item.TryGetProperty("hidden", out JsonElement hidden) && hidden.ValueKind == JsonValueKind.True) continue;
                    string id = RequiredString(item, "model");
                    var efforts = new List<CodexReasoningOption>();
                    if (item.TryGetProperty("supportedReasoningEfforts", out JsonElement effortArray) && effortArray.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement effort in effortArray.EnumerateArray())
                        {
                            efforts.Add(new CodexReasoningOption(
                                RequiredString(effort, "reasoningEffort"),
                                OptionalString(effort, "description") ?? string.Empty));
                        }
                    }
                    var modalities = item.TryGetProperty("inputModalities", out JsonElement modalitiesArray) && modalitiesArray.ValueKind == JsonValueKind.Array
                        ? modalitiesArray.EnumerateArray().Select(value => value.GetString()).Where(value => value is not null).Cast<string>().ToList()
                        : new List<string> { "text" };
                    models.Add(new CodexModelDescriptor(
                        id,
                        OptionalString(item, "displayName") ?? id,
                        OptionalString(item, "description") ?? string.Empty,
                        item.TryGetProperty("isDefault", out JsonElement isDefault) && isDefault.ValueKind == JsonValueKind.True,
                        OptionalString(item, "defaultReasoningEffort") ?? efforts.FirstOrDefault()?.Id ?? string.Empty,
                        efforts,
                        modalities));
                }
            }
            cursor = OptionalString(result, "nextCursor");
            if (cursor is not null && !seen.Add(cursor)) throw new CodexProtocolException("Codex returned an invalid model cursor.");
        } while (cursor is not null);
        return models;
    }

    private async Task<CodexUsage> LoadUsageAsync(CancellationToken cancellationToken)
    {
        try
        {
            JsonElement result = await _client.RequestAsync("account/rateLimits/read", null, cancellationToken).ConfigureAwait(false);
            if (!result.TryGetProperty("rateLimits", out JsonElement limits)) return CodexUsage.Empty;
            return new CodexUsage(ParseWindow(limits, "primary"), ParseWindow(limits, "secondary"),
                limits.TryGetProperty("spendControlReached", out JsonElement spend) && spend.ValueKind is JsonValueKind.True or JsonValueKind.False ? spend.GetBoolean() : null);
        }
        catch { return CodexUsage.Empty; }
    }

    private void Client_Notification(CodexNotification notification)
    {
        if (notification.Method == "account/login/completed")
        {
            string? loginId = OptionalString(notification.Params, "loginId");
            if (loginId is null || loginId == _loginId) _loginId = null;
            bool success = notification.Params.TryGetProperty("success", out JsonElement successValue) && successValue.ValueKind == JsonValueKind.True;
            if (!success)
            {
                Account = CodexAccount.Disconnected(OptionalString(notification.Params, "error") ?? "ChatGPT sign-in was not completed.");
                Changed?.Invoke();
            }
            else _ = SafeRefreshAsync();
        }
        else if (notification.Method == "account/updated") _ = SafeRefreshAsync();
        else if (notification.Method == "account/rateLimits/updated") _ = SafeRefreshAsync();
    }

    private async Task SafeRefreshAsync()
    {
        try { await RefreshAsync().ConfigureAwait(false); }
        catch (Exception ex)
        {
            Account = CodexAccount.Disconnected(SafeMessage(ex));
            Models = Array.Empty<CodexModelDescriptor>();
            Usage = CodexUsage.Empty;
            Changed?.Invoke();
        }
    }

    private static CodexRateLimitWindow? ParseWindow(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out JsonElement window) || window.ValueKind != JsonValueKind.Object) return null;
        double used = window.TryGetProperty("usedPercent", out JsonElement usedValue) && usedValue.TryGetDouble(out double parsed) ? parsed : 0;
        int? duration = window.TryGetProperty("windowDurationMins", out JsonElement durationValue) && durationValue.TryGetInt32(out int parsedDuration) ? parsedDuration : null;
        long? resets = window.TryGetProperty("resetsAt", out JsonElement resetsValue) && resetsValue.TryGetInt64(out long parsedResets) ? parsedResets : null;
        return new CodexRateLimitWindow(used, duration, resets);
    }

    private static bool IsAllowedLoginHost(string host) => host.Equals("auth.openai.com", StringComparison.OrdinalIgnoreCase)
        || host.Equals("chatgpt.com", StringComparison.OrdinalIgnoreCase);

    private static string RequiredString(JsonElement element, string name) => OptionalString(element, name)
        ?? throw new CodexProtocolException($"Codex response is missing '{name}'.");

    private static string? OptionalString(JsonElement element, string name) => element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
        ? value.GetString() : null;

    private static string SafeMessage(Exception exception) => exception switch
    {
        FileNotFoundException => exception.Message,
        InvalidDataException => exception.Message,
        CodexProtocolException => exception.Message,
        _ => "OpenAI Codex is unavailable. Try again or repair Networker.",
    };

    private static void EnsureNoPlaintextCredentials()
    {
        string path = Path.Combine(AppSettings.GetLocalDataDirectory(), "Codex", "auth.json");
        if (File.Exists(path)) throw new InvalidDataException("Codex attempted to use plaintext credential storage. Sign-in was blocked.");
    }
}
