using System;
using System.Linq;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using networker.NetworkConfig.Views.Tabs;
using networker.Services;
using networker.Services.Updates;
using Networker.Core.Updates;
using Networker.Core.Workflow;

namespace networker.Views
{
    public sealed partial class SettingsPage : Page
    {
        private bool _isInitializing = false;
        private UpdateCoordinator? _updateCoordinator;
        private System.Threading.CancellationTokenSource? _installCts;
        private TroubleshootingSession? _troubleshootingSession;

        private static IServiceProvider Services => ((App)Application.Current).Services;

        public SettingsPage()
        {
            this.InitializeComponent();
            this.Loaded += SettingsPage_Loaded;
            this.Unloaded += SettingsPage_Unloaded;
            _troubleshootingSession = Services.GetService<TroubleshootingSession>();
        }

        private async void SettingsPage_Loaded(object sender, RoutedEventArgs e)
        {
            _isInitializing = true;

            ProviderComboBox.Items.Clear();
            ProviderComboBox.Items.Add("ollama");
            ProviderComboBox.Items.Add("grok");
            ProviderComboBox.Items.Add("gemini");
            ProviderComboBox.SelectedItem = AppSettings.SelectedProvider;

            ThemeComboBox.Items.Clear();
            ThemeComboBox.Items.Add("System");
            ThemeComboBox.Items.Add("Light");
            ThemeComboBox.Items.Add("Dark");
            ThemeComboBox.SelectedItem = AppSettings.ThemeMode;

            EndpointTextBox.Text = AppSettings.OllamaEndpoint;
            ApiKeyPasswordBox.Password = AppSettings.OllamaApiKey;
            SystemPromptTextBox.Text = AppSettings.GlobalSystemPrompt;
            CustomInstructionsTextBox.Text = AppSettings.GlobalCustomInstructions;

            VendorComboBox.Items.Clear();
            foreach (var displayName in GenerateTab.VendorDisplayNames)
            {
                VendorComboBox.Items.Add(displayName);
            }
            VendorComboBox.SelectedItem = AppSettings.DefaultVendor;
            NetworkConfigDirTextBox.Text = AppSettings.NetworkConfigDirectory;

            AutomaticChecksToggle.IsOn = AppSettings.AutomaticUpdateChecksEnabled;
            PreviewToggle.IsOn = AppSettings.IncludePrereleaseUpdates;

            try
            {
                UpdateCoordinator coordinator = Services.GetRequiredService<UpdateCoordinator>();
                _updateCoordinator = coordinator;
                coordinator.StateChanged += UpdateCoordinator_StateChanged;
                coordinator.ProgressChanged += UpdateCoordinator_ProgressChanged;
            }
            catch (Exception)
            {
                // The update stack is unavailable; the page renders its disabled state.
            }

            _isInitializing = false;

            if (_updateCoordinator is not null)
            {
                ApplySnapshot(_updateCoordinator.Snapshot);
            }
            else
            {
                AutomaticChecksToggle.IsEnabled = false;
                PreviewToggle.IsEnabled = false;
                CheckUpdatesButton.IsEnabled = false;
                SetStatus(UpdateStatusText, "Update services aren't available right now.", "InlineErrorTextStyle");
            }

            await FetchModelsAsync(applyConnection: false);
        }

        private void SettingsPage_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_updateCoordinator is not null)
            {
                _updateCoordinator.StateChanged -= UpdateCoordinator_StateChanged;
                _updateCoordinator.ProgressChanged -= UpdateCoordinator_ProgressChanged;
                _updateCoordinator = null;
            }
            _installCts?.Dispose();
            _installCts = null;
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await FetchModelsAsync(applyConnection: true);

        private void ModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing || ModelComboBox.SelectedItem == null) return;
            string model = ModelComboBox.SelectedItem.ToString() ?? "";
            LlmSession.SetModel(model);
            LlmRuntime.ApplyProviderSelection(LlmSession.Provider, model);
        }

        private async void ProviderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing || ProviderComboBox.SelectedItem is not string provider) return;
            LlmSession.SetProvider(provider);
            LlmRuntime.ApplyProviderSelection(provider, AppSettings.SelectedModel);

            if (provider != "ollama")
            {
                Toaster.Show(
                    $"Provider '{provider}' requires environment configuration (XAI_API_KEY / GEMINI_API_KEY). See .env.example.",
                    InfoBarSeverity.Warning,
                    "Cloud provider");
            }

            await FetchModelsAsync(applyConnection: false);
            UpdateWorkspaceData();
        }

        private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing || ThemeComboBox.SelectedItem is not string theme) return;
            AppSettings.ThemeMode = theme;

            MainWindow.Instance?.ApplyThemeToFramePublic();
        }

        private void VendorComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing || VendorComboBox.SelectedItem is not string vendor) return;
            AppSettings.DefaultVendor = vendor;
        }

        private void ApplyDataFolderButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            string directory = (NetworkConfigDirTextBox.Text ?? "").Trim();
            if (directory.Length == 0)
            {
                SetStatus(DataFolderStatusText, "Enter a data-folder path before applying.", "InlineErrorTextStyle");
                return;
            }

            if (directory == AppSettings.NetworkConfigDirectory)
            {
                SetStatus(DataFolderStatusText, "No pending data-folder changes.", "InlineStatusTextStyle");
                return;
            }

            AppSettings.NetworkConfigDirectory = directory;
            SetStatus(DataFolderStatusText, "Saved. Restart Networker to use this folder.", "InlineWarningTextStyle");
            Toaster.Show(
                "Configuration workspace data folder updated. Restart the app for it to take effect.",
                InfoBarSeverity.Informational,
                "Configuration workspace");
        }

        private void SavePromptButton_Click(object sender, RoutedEventArgs e) => SaveGlobalPrompt();

        private void ResetPromptButton_Click(object sender, RoutedEventArgs e)
        {
            SystemPromptTextBox.Text = "";
            CustomInstructionsTextBox.Text = "";
            SaveGlobalPrompt();
        }

        private void SaveGlobalPrompt()
        {
            if (AppSettings.TrySaveGlobalPrompts(
                SystemPromptTextBox.Text ?? "",
                CustomInstructionsTextBox.Text ?? "",
                out string error))
            {
                SetStatus(PromptStatusText, "Prompt defaults saved.", "InlineSuccessTextStyle");
                return;
            }

            SetStatus(PromptStatusText, $"Unable to save prompts: {error}", "InlineErrorTextStyle");
            Toaster.Show(error, InfoBarSeverity.Error, "Prompt save failed");
        }

        private async System.Threading.Tasks.Task FetchModelsAsync(bool applyConnection)
        {
            string endpoint = (EndpointTextBox.Text ?? "").Trim();
            string apiKey = ApiKeyPasswordBox.Password ?? "";
            if (applyConnection && string.IsNullOrWhiteSpace(endpoint))
            {
                SetConnectionStatus("Enter an API endpoint before refreshing.", "AppDangerBrush");
                EndpointTextBox.Focus(FocusState.Programmatic);
                return;
            }

            SetLoadingState(true);
            SetConnectionStatus("Checking provider and models...", "AppTextDisabledBrush");

            try
            {
                if (applyConnection)
                {
                    bool connectionChanged = endpoint != AppSettings.OllamaEndpoint || apiKey != AppSettings.OllamaApiKey;
                    AppSettings.OllamaEndpoint = endpoint;
                    AppSettings.OllamaApiKey = apiKey;

                    if (connectionChanged)
                    {
                        LlmRuntime.Reset();
                    }
                }

                LlmRuntime.ApplyProviderSelection(LlmSession.Provider, LlmSession.Model);
                await LlmSession.RefreshAsync();
                var modelIds = LlmSession.Models.ToList();

                if (modelIds.Count == 0)
                {
                    string status = LlmSession.IsConnected ? "Connected, but no models were found." : LlmSession.StatusMessage;
                    SetConnectionStatus(status, LlmSession.IsConnected ? "AppWarningBrush" : "AppDangerBrush");
                    ModelComboBox.ItemsSource = null;
                    ModelComboBox.IsEnabled = false;
                }
                else
                {
                    SetConnectionStatus($"Connected to {LlmSession.Provider}. {modelIds.Count} model{(modelIds.Count == 1 ? "" : "s")} available.", "AppSuccessBrush");
                    ModelComboBox.IsEnabled = true;

                    _isInitializing = true;
                    ModelComboBox.ItemsSource = modelIds;
                    ModelComboBox.SelectedItem = LlmSession.Model;
                    _isInitializing = false;
                }
            }
            catch (Exception ex)
            {
                SetConnectionStatus($"Unable to connect: {ex.Message}", "AppDangerBrush");
                ModelComboBox.ItemsSource = null;
                ModelComboBox.IsEnabled = false;
            }
            finally { SetLoadingState(false); }
        }

        private void SetConnectionStatus(string message, string brushKey)
        {
            ConnectionStatusText.Text = message;
            ConnectionStatusText.Foreground = Brush(brushKey);
            ConnectionStatusDot.Fill = Brush(brushKey);
        }

        private void AutomaticChecksToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            AppSettings.AutomaticUpdateChecksEnabled = AutomaticChecksToggle.IsOn;
        }

        private async void PreviewToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            AppSettings.IncludePrereleaseUpdates = PreviewToggle.IsOn;

            // A channel change is immediately due on its own cadence.
            AppSettings.LastCheckedUpdateChannel = (PreviewToggle.IsOn ? UpdateChannel.Preview : UpdateChannel.Stable).ToString();
            AppSettings.NextAutomaticUpdateCheckUtc = null;
            await RunManualCheckAsync();
        }

        private async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e) => await RunManualCheckAsync();

        private async System.Threading.Tasks.Task RunManualCheckAsync()
        {
            if (_updateCoordinator is null) return;
            SetStatus(UpdateStatusText, "Checking for updates...", "InlineStatusTextStyle");
            try
            {
                UpdateChannel channel = AppSettings.IncludePrereleaseUpdates ? UpdateChannel.Preview : UpdateChannel.Stable;
                AppSettings.LastCheckedUpdateChannel = channel.ToString();
                UpdateCheckOutcome outcome = await _updateCoordinator.CheckAsync(channel, manual: true, System.Threading.CancellationToken.None);
                if (outcome.Cancelled) return;

                // Manual checks bypass time/backoff but persist like any successful check.
                DateTimeOffset now = DateTimeOffset.UtcNow;
                if (outcome.Succeeded)
                {
                    AppSettings.UpdateCheckFailureCount = 0;
                    AppSettings.LastSuccessfulUpdateCheckUtc = now;
                    AppSettings.NextAutomaticUpdateCheckUtc = UpdateSchedulerPolicy.ComputeNextCheck(now, succeeded: true, 0, null);
                }
                else
                {
                    int failures = AppSettings.UpdateCheckFailureCount + 1;
                    AppSettings.UpdateCheckFailureCount = failures;
                    AppSettings.NextAutomaticUpdateCheckUtc = UpdateSchedulerPolicy.ComputeNextCheck(now, succeeded: false, failures, outcome.RetryAfterUtc);
                }

                if (_updateCoordinator is not null)
                {
                    ApplySnapshot(_updateCoordinator.Snapshot);
                }
            }
            catch (Exception)
            {
                SetStatus(UpdateStatusText, "Couldn't check for updates right now.", "InlineErrorTextStyle");
            }
        }

        private async void InstallUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            if (_updateCoordinator is null) return;
            _installCts?.Dispose();
            _installCts = new System.Threading.CancellationTokenSource();
            try
            {
                await _updateCoordinator.InstallUpdateAsync(_installCts.Token);
            }
            catch (OperationCanceledException)
            {
                // The coordinator publishes the cancelled state.
            }
        }

        private void CancelInstallButton_Click(object sender, RoutedEventArgs e)
        {
            _installCts?.Cancel();
        }

        private async void RestartNowButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "Restart Networker?",
                Content = "The update is ready. Restart now to finish installing it?",
                PrimaryButtonText = "Restart now",
                CloseButtonText = "Later",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot,
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

            AppRestartService restart = Services.GetRequiredService<AppRestartService>();
            if (restart.TryRestart(out string? error)) return;
            SetStatus(UpdateStatusText, error ?? "Couldn't restart Networker.", "InlineErrorTextStyle");
        }

        private void LaterButton_Click(object sender, RoutedEventArgs e)
        {
            _updateCoordinator?.DismissUpdate();
        }

        private void UpdateCoordinator_StateChanged(UpdateSnapshot snapshot)
        {
            if (_updateCoordinator is null) return;
            if (DispatcherQueue.HasThreadAccess)
            {
                ApplySnapshot(snapshot);
            }
            else
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (_updateCoordinator is not null) ApplySnapshot(snapshot);
                });
            }
        }

        private void UpdateCoordinator_ProgressChanged(double value)
        {
            if (_updateCoordinator is null) return;
            if (DispatcherQueue.HasThreadAccess)
            {
                ApplyProgress(value);
            }
            else
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (_updateCoordinator is not null) ApplyProgress(value);
                });
            }
        }

        private void ApplyProgress(double value)
        {
            if (value < 0)
            {
                UpdateProgressBar.IsIndeterminate = true;
            }
            else
            {
                UpdateProgressBar.IsIndeterminate = false;
                UpdateProgressBar.Value = value;
            }
        }

        private void ApplySnapshot(UpdateSnapshot snapshot)
        {
            if (UpdateForm is null || UpdateStatusText is null) return;

            InstalledVersionText.Text = snapshot.Installed.DisplayVersion;

            bool canInstall = snapshot.Installed.CanInstallUpdates;
            AutomaticChecksToggle.IsEnabled = canInstall;
            PreviewToggle.IsEnabled = canInstall;

            DateTimeOffset? last = AppSettings.LastSuccessfulUpdateCheckUtc;
            LastCheckText.Text = last is null
                ? "Never"
                : last.Value.ToLocalTime().ToString("g");

            bool busy = snapshot.Status is UpdateStatus.Checking
                or UpdateStatus.Downloading
                or UpdateStatus.Verifying
                or UpdateStatus.Installing;

            UpdateBusyRing.IsActive = busy;
            UpdateBusyRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            CheckUpdatesButton.IsEnabled = !busy && canInstall;

            bool showProgress = snapshot.Status is UpdateStatus.Downloading
                or UpdateStatus.Verifying
                or UpdateStatus.Installing;
            UpdateProgressBar.Visibility = showProgress ? Visibility.Visible : Visibility.Collapsed;
            if (showProgress)
            {
                ApplyProgress(snapshot.Progress);
            }

            UpdateRelease? release = snapshot.AvailableRelease;
            UpdateDetailPanel.Visibility = release is null ? Visibility.Collapsed : Visibility.Visible;
            if (release is not null)
            {
                UpdateVersionText.Text = $"Version {release.Version.ToNormalizedString()} ({(release.IsPrerelease ? "preview" : "stable")})";
                UpdatePublishedText.Text = release.PublishedAt.ToLocalTime().ToString("g");
                UpdateNotesText.Text = TruncateReleaseNotes(release.Body);
                ReleaseLinkButton.NavigateUri = new Uri(release.HtmlUrl);
            }

            InstallUpdateButton.Visibility = snapshot.Status == UpdateStatus.Available ? Visibility.Visible : Visibility.Collapsed;
            LaterButton.Visibility = snapshot.Status == UpdateStatus.Available ? Visibility.Visible : Visibility.Collapsed;
            CancelInstallButton.Visibility = snapshot.Status is UpdateStatus.Downloading or UpdateStatus.Verifying ? Visibility.Visible : Visibility.Collapsed;
            RestartNowButton.Visibility = snapshot.Status == UpdateStatus.RestartRequired ? Visibility.Visible : Visibility.Collapsed;

            string statusText = snapshot.Status switch
            {
                UpdateStatus.Disabled => "Automatic updates aren't available for this build.",
                UpdateStatus.Checking => "Checking for updates...",
                UpdateStatus.UpToDate => "You're up to date.",
                UpdateStatus.Available => release is null
                    ? "An update is available."
                    : $"Version {release.Version.ToNormalizedString()} is available.",
                UpdateStatus.Downloading => "Downloading update...",
                UpdateStatus.Verifying => "Verifying update...",
                UpdateStatus.Installing => "Installing update...",
                UpdateStatus.RestartRequired => "The update is ready. Restart Networker to finish installing it.",
                UpdateStatus.Cancelled => "Update cancelled.",
                UpdateStatus.Failed => snapshot.Error?.Message ?? "The update failed.",
                _ => string.Empty,
            };

            if (snapshot.Status == UpdateStatus.Failed)
            {
                SetStatus(UpdateStatusText, statusText, "InlineErrorTextStyle");
            }
            else if (snapshot.Status == UpdateStatus.RestartRequired)
            {
                SetStatus(UpdateStatusText, statusText, "InlineSuccessTextStyle");
            }
            else
            {
                SetStatus(UpdateStatusText, statusText, "InlineStatusTextStyle");
            }
        }

        private static string TruncateReleaseNotes(string? body)
        {
            if (string.IsNullOrWhiteSpace(body)) return "No release notes were provided.";

            string plain = body.Replace("\r\n", " ").Replace("\r", " ").Replace("\n", " ");
            var builder = new StringBuilder(plain.Length);
            bool lastSpace = false;
            foreach (char c in plain)
            {
                bool isSpace = c == ' ' || c == '\t';
                if (!isSpace || !lastSpace)
                {
                    builder.Append(c);
                }
                lastSpace = isSpace;
            }

            const int maxLength = 600;
            string text = builder.ToString().Trim();
            return text.Length <= maxLength ? text : text.Substring(0, maxLength - 3).TrimEnd() + "...";
        }

        private static void SetStatus(TextBlock target, string message, string styleKey)
        {
            target.Text = message;
            if (Application.Current.Resources.TryGetValue(styleKey, out object value) && value is Style style)
            {
                target.Style = style;
            }
        }

        private static SolidColorBrush Brush(string key)
        {
            if (Application.Current.Resources.TryGetValue(key, out object value) && value is SolidColorBrush brush)
            {
                return brush;
            }

            return new SolidColorBrush(Colors.Gray);
        }

        private void SetLoadingState(bool isLoading)
        {
            LoadingRing.IsActive = isLoading;
            LoadingRing.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
            RefreshButton.IsEnabled = !isLoading;
        }

        public void SelectSection(string section)
        {
            string key = section.Contains(':') ? section.Split(':', 2)[1] : section;
            GeneralSettingsPanel.Visibility = key is "templates" or "vault" ? Visibility.Collapsed : Visibility.Visible;
            TemplatesSettingsPanel.Visibility = key == "templates" ? Visibility.Visible : Visibility.Collapsed;
            VaultSettingsPanel.Visibility = key == "vault" ? Visibility.Visible : Visibility.Collapsed;
            if (key == "updates") UpdateForm.StartBringIntoView();
            if (key == "ai") AiRuntimeForm.StartBringIntoView();
        }

        private void SettingsSection_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: string tag }) SelectSection(tag);
        }

        private void UpdateWorkspaceData()
        {
            if (_troubleshootingSession is null || WorkspaceDataText is null) return;
            WorkspaceDataText.Text = _troubleshootingSession.Current.IsEmpty
                ? "No saved troubleshooting evidence."
                : $"Last updated {_troubleshootingSession.Current.UpdatedAt.ToLocalTime():g}.";
        }

        private async void ClearWorkspace_Click(object sender, RoutedEventArgs e)
        {
            if (_troubleshootingSession is null || _troubleshootingSession.Current.IsEmpty) return;
            var dialog = new ContentDialog
            {
                Title = "Clear troubleshooting workspace?",
                Content = "This removes saved evidence, results, progress, activity, and chat. Settings, templates, and the encrypted vault are retained.",
                PrimaryButtonText = "Clear workspace",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            _troubleshootingSession.Clear();
            RecentActivity.Clear();
            UpdateWorkspaceData();
        }
    }
}
