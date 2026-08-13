using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Networker.Core.Llm;
using Networker.Core.Llm.ChatGpt;

namespace networker.Services
{
    public sealed class ChatGptWebSession : IChatGptTransport, IDisposable
    {
        private static readonly Uri ChatGptUri = new("https://chatgpt.com/");
        private readonly SemaphoreSlim _turnLock = new(1, 1);
        private WebView2? _webView;
        private DispatcherQueue? _dispatcher;
        private Action? _showBrowser;
        private Action? _hideBrowser;
        private Task? _initialization;
        private CancellationTokenSource? _activeTurn;
        private bool _disposed;
        private bool _loginVisible;

        public event Action? StatusChanged;

        public void Attach(WebView2 webView, Action showBrowser, Action hideBrowser)
        {
            _webView = webView ?? throw new ArgumentNullException(nameof(webView));
            _dispatcher = webView.DispatcherQueue;
            _showBrowser = showBrowser;
            _hideBrowser = hideBrowser;
            _initialization = InitializeAsync();
        }

        public async Task ShowLoginAsync()
        {
            await EnsureInitializedAsync().ConfigureAwait(false);
            await OnUiAsync(() =>
            {
                _loginVisible = true;
                _showBrowser?.Invoke();
                _webView!.CoreWebView2.Navigate(ChatGptUri.AbsoluteUri);
            }).ConfigureAwait(false);
        }

        public Task HideLoginAsync() => OnUiAsync(() => { _loginVisible = false; _hideBrowser?.Invoke(); });

        public async Task SignOutAndDeleteProfileAsync()
        {
            await EnsureInitializedAsync().ConfigureAwait(false);
            await CancelAsync().ConfigureAwait(false);
            await OnUiAsync(async () =>
            {
                await _webView!.CoreWebView2.Profile.ClearBrowsingDataAsync();
                _webView.CoreWebView2.Navigate(ChatGptUri.AbsoluteUri);
                StatusChanged?.Invoke();
            }).ConfigureAwait(false);
        }

        public async Task<ChatGptStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await EnsureInitializedAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
                string json = await ExecuteJsonAsync(StatusScript, cancellationToken).ConfigureAwait(false);
                BrowserStatus? browser = JsonSerializer.Deserialize<BrowserStatus>(json, JsonOptions);
                if (browser is null)
                {
                    return Compatibility("ChatGPT page structure could not be recognized.");
                }

                if (browser.RateLimited)
                {
                    return new ChatGptStatus(ChatGptSessionState.RateLimited, "ChatGPT is rate limited. Try again later.", Models(browser), Capabilities(browser));
                }

                if (!browser.SignedIn)
                {
                    return new ChatGptStatus(ChatGptSessionState.SignedOut, "Sign in to ChatGPT to use this provider.", Array.Empty<LlmModelInfo>(), LlmProviderCapabilities.None);
                }

                return new ChatGptStatus(
                    ChatGptSessionState.Ready,
                    browser.TemporaryChat ? "Ready (Temporary Chat)" : "Ready; ChatGPT account history may be used.",
                    Models(browser),
                    Capabilities(browser),
                    UsesAccountHistory: !browser.TemporaryChat);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return new ChatGptStatus(ChatGptSessionState.Offline, ex.Message, Array.Empty<LlmModelInfo>(), LlmProviderCapabilities.None);
            }
        }

        public async Task<LlmResponse> CompleteAsync(ChatGptTurnRequest request, CancellationToken cancellationToken = default)
        {
            var builder = new StringBuilder();
            await foreach (string delta in StreamAsync(request, cancellationToken).ConfigureAwait(false)) builder.Append(delta);
            return new LlmResponse { Content = builder.ToString(), Provider = "ChatGPT Plus / Pro", Model = request.Model };
        }

        public async IAsyncEnumerable<string> StreamAsync(
            ChatGptTurnRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await _turnLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _activeTurn = linked;
            try
            {
                ChatGptStatus status = await GetStatusAsync(linked.Token).ConfigureAwait(false);
                if (status.State != ChatGptSessionState.Ready)
                {
                    throw new LlmException(status.Message) { Provider = "ChatGPT Plus / Pro" };
                }

                string prompt = FormatPrompt(request.Messages);
                if (string.IsNullOrWhiteSpace(prompt)) throw new LlmException("The ChatGPT request is empty.");

                await using IAsyncEnumerator<string> stream = StreamSubmittedTurnAsync(prompt, request.PreferTemporaryChat, linked.Token)
                    .GetAsyncEnumerator(linked.Token);
                while (true)
                {
                    bool moved;
                    try
                    {
                        moved = await stream.MoveNextAsync().ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        await StopGenerationAsync().ConfigureAwait(false);
                        throw;
                    }
                    catch (LlmException ex) when (!ex.MayHaveSubmittedRequest)
                    {
                        throw new LlmException(ex.Message, ex) { Provider = "ChatGPT Plus / Pro", MayHaveSubmittedRequest = true };
                    }
                    catch (Exception ex)
                    {
                        throw new LlmException(ex.Message, ex) { Provider = "ChatGPT Plus / Pro", MayHaveSubmittedRequest = true };
                    }

                    if (!moved) break;
                    yield return stream.Current;
                }
            }
            finally
            {
                _activeTurn = null;
                _turnLock.Release();
            }
        }

        private async IAsyncEnumerable<string> StreamSubmittedTurnAsync(
            string prompt,
            bool preferTemporaryChat,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (preferTemporaryChat)
            {
                await PrepareFreshConversationAsync(cancellationToken).ConfigureAwait(false);
                _ = await ExecuteJsonAsync(EnableTemporaryChatScript, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await PrepareFreshConversationAsync(cancellationToken).ConfigureAwait(false);
            }

            string submitScript = SubmitScript.Replace("__PROMPT__", JsonSerializer.Serialize(prompt), StringComparison.Ordinal);
            bool accepted = JsonSerializer.Deserialize<bool>(await ExecuteJsonAsync(submitScript, cancellationToken).ConfigureAwait(false));
            if (!accepted) throw new LlmException("ChatGPT's message composer was not available. Sign in again or refresh the provider.");

            string previous = string.Empty;
            int stableReads = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string result = await ExecuteJsonAsync(ResponseScript, cancellationToken).ConfigureAwait(false);
                BrowserResponse? response = JsonSerializer.Deserialize<BrowserResponse>(result, JsonOptions);
                if (response is null) throw new InvalidOperationException("ChatGPT response could not be observed.");
                if (response.RateLimited) throw new LlmException("ChatGPT is rate limited. Try again later.") { MayHaveSubmittedRequest = true };

                string delta = string.Empty;
                if (response.Text.Length > previous.Length && response.Text.StartsWith(previous, StringComparison.Ordinal))
                {
                    delta = response.Text[previous.Length..];
                    previous = response.Text;
                }
                else if (response.Text.Length > 0 && response.Text != previous)
                {
                    delta = response.Text;
                    previous = response.Text;
                }

                stableReads = response.Done && delta.Length == 0 ? stableReads + 1 : 0;
                if (delta.Length > 0) yield return delta;
                if (response.Done && stableReads >= 2 && previous.Length > 0) break;
                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task CancelAsync(CancellationToken cancellationToken = default)
        {
            _activeTurn?.Cancel();
            await StopGenerationAsync(cancellationToken).ConfigureAwait(false);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _activeTurn?.Cancel();
            if (_webView is not null)
            {
                try { _webView.Close(); } catch { }
            }
            _turnLock.Dispose();
        }

        private async Task InitializeAsync()
        {
            if (_webView is null) throw new InvalidOperationException("The ChatGPT WebView has not been attached.");
            string profileRoot = Path.Combine(AppSettings.GetLocalDataDirectory(), "ChatGptWebView");
            Directory.CreateDirectory(profileRoot);
            var options = new CoreWebView2EnvironmentOptions { ExclusiveUserDataFolderAccess = true };
            CoreWebView2Environment environment = await CoreWebView2Environment.CreateWithOptionsAsync(string.Empty, profileRoot, options);
            CoreWebView2ControllerOptions controllerOptions = environment.CreateCoreWebView2ControllerOptions();
            controllerOptions.ProfileName = "NetworkerChatGpt";
            controllerOptions.IsInPrivateModeEnabled = false;
            await _webView.EnsureCoreWebView2Async(environment, controllerOptions);
            CoreWebView2 core = _webView.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.AreDefaultScriptDialogsEnabled = false;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Profile.IsGeneralAutofillEnabled = false;
            core.Profile.IsPasswordAutosaveEnabled = false;
            core.NewWindowRequested += (_, args) =>
            {
                args.Handled = true;
                if (args.IsUserInitiated && IsAllowedUri(args.Uri, allowIdentityProviders: true)) core.Navigate(args.Uri);
            };
            core.DownloadStarting += (_, args) => { args.Cancel = true; args.Handled = true; };
            core.PermissionRequested += (_, args) => { args.State = CoreWebView2PermissionState.Deny; args.Handled = true; };
            core.NavigationStarting += (_, args) =>
            {
                if (!IsAllowedUri(args.Uri, _loginVisible)) args.Cancel = true;
            };
            core.FrameNavigationStarting += (_, args) =>
            {
                if (!IsAllowedUri(args.Uri, _loginVisible)) args.Cancel = true;
            };
            core.LaunchingExternalUriScheme += (_, args) => args.Cancel = true;
            core.ScriptDialogOpening += (_, _) => { };
            core.NavigationCompleted += (_, _) => StatusChanged?.Invoke();
            core.ProcessFailed += (_, _) => StatusChanged?.Invoke();
            core.Navigate(ChatGptUri.AbsoluteUri);
        }

        private async Task EnsureInitializedAsync()
        {
            if (_initialization is null) throw new InvalidOperationException("The ChatGPT browser is not attached to the application shell.");
            await _initialization.ConfigureAwait(false);
        }

        private async Task<string> ExecuteJsonAsync(string script, CancellationToken cancellationToken)
        {
            await EnsureInitializedAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            string raw = await OnUiAsync(async () => await _webView!.ExecuteScriptAsync(script)).WaitAsync(cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<string>(raw) ?? raw;
        }

        private Task StopGenerationAsync(CancellationToken cancellationToken = default)
            => ExecuteJsonAsync(StopScript, cancellationToken);

        private async Task PrepareFreshConversationAsync(CancellationToken cancellationToken)
        {
            await OnUiAsync(() => _webView!.CoreWebView2.Navigate(ChatGptUri.AbsoluteUri)).ConfigureAwait(false);
            for (int attempt = 0; attempt < 40; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string json = await ExecuteJsonAsync(StatusScript, cancellationToken).ConfigureAwait(false);
                BrowserStatus? status = JsonSerializer.Deserialize<BrowserStatus>(json, JsonOptions);
                if (status?.SignedIn == true) return;
                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            }
            throw new InvalidOperationException("ChatGPT did not make a fresh message composer available.");
        }

        private Task OnUiAsync(Action action)
        {
            if (_dispatcher?.HasThreadAccess == true) { action(); return Task.CompletedTask; }
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (_dispatcher is null || !_dispatcher.TryEnqueue(() =>
            {
                try { action(); completion.SetResult(); }
                catch (Exception ex) { completion.SetException(ex); }
            })) completion.SetException(new InvalidOperationException("The UI dispatcher is unavailable."));
            return completion.Task;
        }

        private Task<T> OnUiAsync<T>(Func<Task<T>> action)
        {
            if (_dispatcher?.HasThreadAccess == true) return action();
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (_dispatcher is null || !_dispatcher.TryEnqueue(async () =>
            {
                try { completion.SetResult(await action()); }
                catch (Exception ex) { completion.SetException(ex); }
            })) completion.SetException(new InvalidOperationException("The UI dispatcher is unavailable."));
            return completion.Task;
        }

        private Task OnUiAsync(Func<Task> action)
        {
            if (_dispatcher?.HasThreadAccess == true) return action();
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (_dispatcher is null || !_dispatcher.TryEnqueue(async () =>
            {
                try { await action(); completion.SetResult(); }
                catch (Exception ex) { completion.SetException(ex); }
            })) completion.SetException(new InvalidOperationException("The UI dispatcher is unavailable."));
            return completion.Task;
        }

        private static bool IsAllowedUri(string value, bool allowIdentityProviders = false)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)) return false;
            return uri.Scheme == Uri.UriSchemeHttps &&
                (uri.Host.Equals("chatgpt.com", StringComparison.OrdinalIgnoreCase) ||
                 uri.Host.EndsWith(".chatgpt.com", StringComparison.OrdinalIgnoreCase) ||
                 uri.Host.Equals("auth.openai.com", StringComparison.OrdinalIgnoreCase) ||
                 uri.Host.EndsWith(".openai.com", StringComparison.OrdinalIgnoreCase) ||
                 (allowIdentityProviders &&
                  (uri.Host.Equals("accounts.google.com", StringComparison.OrdinalIgnoreCase) ||
                   uri.Host.Equals("login.microsoftonline.com", StringComparison.OrdinalIgnoreCase) ||
                   uri.Host.Equals("appleid.apple.com", StringComparison.OrdinalIgnoreCase))));
        }

        private static string FormatPrompt(IReadOnlyList<LlmMessage> messages)
        {
            var builder = new StringBuilder();
            foreach (LlmMessage message in messages)
            {
                if (string.IsNullOrWhiteSpace(message.Content)) continue;
                string label = message.Role switch { "system" => "Instructions", "assistant" => "Assistant", _ => "User" };
                builder.Append(label).Append(":\n").Append(message.Content.Trim()).Append("\n\n");
            }
            return builder.ToString().Trim();
        }

        private static IReadOnlyList<LlmModelInfo> Models(BrowserStatus status)
        {
            string[] ids = status.Models is { Length: > 0 } ? status.Models : new[] { "auto" };
            return ids.Distinct(StringComparer.OrdinalIgnoreCase).Select(id => new LlmModelInfo { Id = id, Name = id }).ToList();
        }

        private static LlmProviderCapabilities Capabilities(BrowserStatus status)
        {
            var value = LlmProviderCapabilities.Streaming | LlmProviderCapabilities.Models;
            if (status.Search) value |= LlmProviderCapabilities.WebSearch;
            if (status.Upload) value |= LlmProviderCapabilities.FileUpload;
            if (status.Image) value |= LlmProviderCapabilities.ImageInput;
            return value;
        }

        private static ChatGptStatus Compatibility(string message) => new(
            ChatGptSessionState.CompatibilityError, message, Array.Empty<LlmModelInfo>(), LlmProviderCapabilities.None);

        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
        private sealed record BrowserStatus(bool SignedIn, bool TemporaryChat, bool RateLimited, bool Search, bool Upload, bool Image, string[] Models);
        private sealed record BrowserResponse(string Text, bool Done, bool RateLimited);

        private const string StatusScript = """
            (() => {
              const text = document.body?.innerText || '';
              const composer = document.querySelector('#prompt-textarea, textarea[data-id="root"], [contenteditable="true"][data-virtualkeyboard]');
              const login = [...document.querySelectorAll('button,a')].some(e => /log in|sign up/i.test(e.innerText || ''));
              const models = [...document.querySelectorAll('[data-testid*="model"], button[aria-haspopup="menu"]')]
                .map(e => (e.innerText || '').trim()).filter(x => x && x.length < 80);
              const temporary = /temporary chat/i.test(text) && [...document.querySelectorAll('button,[role="switch"]')]
                .some(e => /temporary chat/i.test((e.innerText || '') + ' ' + (e.getAttribute('aria-label') || '')) && e.getAttribute('aria-checked') !== 'false');
              return JSON.stringify({ signedIn: !!composer && !login, temporaryChat: temporary,
                rateLimited: /rate limit|too many requests|try again later/i.test(text),
                search: [...document.querySelectorAll('button')].some(e => /search/i.test((e.innerText || '') + (e.getAttribute('aria-label') || ''))),
                upload: !!document.querySelector('input[type="file"]'), image: !!document.querySelector('input[type="file"][accept*="image"]'), models });
            })()
            """;

        private const string EnableTemporaryChatScript = """
            (() => { const b = [...document.querySelectorAll('button,[role="switch"]')].find(e => /temporary chat/i.test((e.innerText || '') + ' ' + (e.getAttribute('aria-label') || ''))); if (!b) return JSON.stringify(false); if (b.getAttribute('aria-checked') === 'false') b.click(); return JSON.stringify(true); })()
            """;

        private const string SubmitScript = """
            (() => { const e = document.querySelector('#prompt-textarea, textarea[data-id="root"], [contenteditable="true"][data-virtualkeyboard]'); if (!e) return JSON.stringify(false); e.focus(); const value = __PROMPT__; if ('value' in e) { const s = Object.getOwnPropertyDescriptor(Object.getPrototypeOf(e), 'value')?.set; s ? s.call(e, value) : e.value = value; } else { e.textContent = value; } e.dispatchEvent(new InputEvent('input', { bubbles:true, inputType:'insertText', data:value })); e.dispatchEvent(new Event('change', { bubbles:true })); const send = document.querySelector('button[data-testid="send-button"], button[aria-label*="Send"]'); if (send && !send.disabled) { send.click(); return JSON.stringify(true); } e.dispatchEvent(new KeyboardEvent('keydown', { key:'Enter', code:'Enter', bubbles:true })); return JSON.stringify(true); })()
            """;

        private const string ResponseScript = """
            (() => { const nodes = [...document.querySelectorAll('[data-message-author-role="assistant"]')]; const last = nodes[nodes.length - 1]; const text = last?.innerText || ''; const stop = document.querySelector('button[data-testid="stop-button"], button[aria-label*="Stop"]'); const page = document.body?.innerText || ''; return JSON.stringify({ text, done: !stop, rateLimited: /rate limit|too many requests|try again later/i.test(page) }); })()
            """;

        private const string StopScript = """
            (() => { const b = document.querySelector('button[data-testid="stop-button"], button[aria-label*="Stop"]'); if (b) b.click(); return JSON.stringify(!!b); })()
            """;
    }
}
