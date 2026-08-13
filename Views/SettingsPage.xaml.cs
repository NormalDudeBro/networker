using System;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using networker.NetworkConfig.Views.Tabs;
using networker.Services;
using Networker.Update.Contracts.State;
using Networker.Update.Contracts.Versioning;
using Networker.Core.Workflow;

namespace networker.Views
{
    public sealed partial class SettingsPage : Page
    {
        private bool _isInitializing = false;
        private TroubleshootingSession? _troubleshootingSession;
        private readonly LauncherStateStore _launcherState = new();
        private readonly ChatGptWebSession? _chatGptSession;

        private static IServiceProvider Services => ((App)Application.Current).Services;

        public SettingsPage()
        {
            this.InitializeComponent();
            this.Loaded += SettingsPage_Loaded;
            this.Unloaded += SettingsPage_Unloaded;
            _troubleshootingSession = Services.GetService<TroubleshootingSession>();
            _chatGptSession = Services.GetService<ChatGptWebSession>();
        }

        private async void SettingsPage_Loaded(object sender, RoutedEventArgs e)
        {
            _isInitializing = true;

            ProviderComboBox.Items.Clear();
            ProviderComboBox.Items.Add("ollama");
            ProviderComboBox.Items.Add("grok");
            ProviderComboBox.Items.Add("gemini");
            ProviderComboBox.Items.Add("chatgpt");
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

            LauncherState launcherState = _launcherState.Read();
            AutomaticChecksToggle.IsOn = launcherState.AutomaticChecksEnabled;
            PreviewToggle.IsOn = launcherState.Channel == NetworkerVersionPolicy.PreviewChannel;
            InstalledVersionText.Text = GetInstalledVersion();
            LastCheckText.Text = launcherState.LastSuccessfulCheckUtc is { } checkedAt
                ? checkedAt.ToLocalTime().ToString("g") : "Never";
            SetStatus(UpdateStatusText, "Updates are applied by the independent launcher before Networker starts.", "InlineStatusTextStyle");

            _isInitializing = false;
            UpdateProviderPanels();

            await FetchModelsAsync(applyConnection: false);
        }

        private void SettingsPage_Unloaded(object sender, RoutedEventArgs e)
        {
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
            UpdateProviderPanels();

            if (provider is "grok" or "gemini")
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

        private void UpdateProviderPanels()
        {
            bool chatGpt = string.Equals(LlmSession.Provider, "chatgpt", StringComparison.OrdinalIgnoreCase);
            ChatGptConnectionPanel.Visibility = chatGpt ? Visibility.Visible : Visibility.Collapsed;
            EndpointTextBox.IsEnabled = !chatGpt;
            ApiKeyPasswordBox.IsEnabled = !chatGpt;
            RefreshButton.Content = chatGpt ? "Refresh status" : "Apply & refresh";
        }

        private async void ChatGptSignInButton_Click(object sender, RoutedEventArgs e)
        {
            if (_chatGptSession is null) return;
            if (!AppSettings.ChatGptDisclosureAccepted)
            {
                var dialog = new ContentDialog
                {
                    Title = "Use experimental ChatGPT integration?",
                    Content = "Networker sends your prompts and selected conversation context to ChatGPT through a dedicated browser profile. Sign-in credentials remain browser-owned. ChatGPT may change this web interface at any time.",
                    PrimaryButtonText = "Continue",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = XamlRoot,
                };
                if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
                AppSettings.ChatGptDisclosureAccepted = true;
            }

            try
            {
                await _chatGptSession.ShowLoginAsync();
                ChatGptStatusText.Text = "Complete sign-in in the browser, then close it and refresh status.";
            }
            catch (Exception ex) { ChatGptStatusText.Text = ex.Message; }
        }

        private async void ChatGptCloseButton_Click(object sender, RoutedEventArgs e)
        {
            if (_chatGptSession is null) return;
            await _chatGptSession.HideLoginAsync();
            await FetchModelsAsync(applyConnection: false);
        }

        private async void ChatGptDeleteProfileButton_Click(object sender, RoutedEventArgs e)
        {
            if (_chatGptSession is null) return;
            var dialog = new ContentDialog
            {
                Title = "Delete ChatGPT browser profile?",
                Content = "This removes cookies and local browser data from Networker's dedicated ChatGPT profile. Your normal browsers are unaffected.",
                PrimaryButtonText = "Delete profile",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            await _chatGptSession.SignOutAndDeleteProfileAsync();
            ChatGptStatusText.Text = "Dedicated ChatGPT browser profile cleared.";
            await FetchModelsAsync(applyConnection: false);
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
            bool chatGpt = string.Equals(LlmSession.Provider, "chatgpt", StringComparison.OrdinalIgnoreCase);
            if (applyConnection && !chatGpt && string.IsNullOrWhiteSpace(endpoint))
            {
                SetConnectionStatus("Enter an API endpoint before refreshing.", "AppDangerBrush");
                EndpointTextBox.Focus(FocusState.Programmatic);
                return;
            }

            SetLoadingState(true);
            SetConnectionStatus("Checking provider and models...", "AppTextDisabledBrush");

            try
            {
                if (applyConnection && !chatGpt)
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

                if (chatGpt && _chatGptSession is not null)
                {
                    var browserStatus = await _chatGptSession.GetStatusAsync();
                    string capabilities = browserStatus.Capabilities == Networker.Core.Llm.LlmProviderCapabilities.None
                        ? "No optional capabilities detected."
                        : $"Detected: {browserStatus.Capabilities}.";
                    ChatGptStatusText.Text = $"{browserStatus.Message} {capabilities}";
                }

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
            _launcherState.Update(state => state with { AutomaticChecksEnabled = AutomaticChecksToggle.IsOn });
        }

        private void PreviewToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            string channel = PreviewToggle.IsOn ? NetworkerVersionPolicy.PreviewChannel : NetworkerVersionPolicy.StableChannel;
            _launcherState.Update(state => state with { Channel = channel, NextCheckUtc = null, ManualCheckRequested = true });
            SetStatus(UpdateStatusText, "The launcher will use this channel on the next launch.", "InlineStatusTextStyle");
        }

        private void CheckUpdatesButton_Click(object sender, RoutedEventArgs e)
        {
            _launcherState.Update(state => state with { ManualCheckRequested = true, NextCheckUtc = null });
            SetStatus(UpdateStatusText, "Update check scheduled for the next launch.", "InlineSuccessTextStyle");
        }

        private static string GetInstalledVersion() => Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "Development build";

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
