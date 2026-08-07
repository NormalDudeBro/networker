using System;
using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using networker.NetworkConfig.Views.Tabs;
using networker.Services;

namespace networker
{
    public sealed partial class SettingsPg : Page
    {
        private bool _isInitializing = false;

        public SettingsPg()
        {
            this.InitializeComponent();
            this.Loaded += SettingsPg_Loaded;
        }

        private async void SettingsPg_Loaded(object sender, RoutedEventArgs e)
        {
            _isInitializing = true;

            ProviderComboBox.Items.Clear();
            ProviderComboBox.Items.Add("ollama");
            ProviderComboBox.Items.Add("grok");
            ProviderComboBox.Items.Add("gemini");
            ProviderComboBox.SelectedItem = AppSettings.SelectedProvider;

            ThemeComboBox.Items.Clear();
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

            _isInitializing = false;

            await FetchModelsAsync();
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await FetchModelsAsync();

        private async void EndpointTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            string newEndpoint = (EndpointTextBox.Text ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(newEndpoint) && newEndpoint != AppSettings.OllamaEndpoint)
            {
                AppSettings.OllamaEndpoint = newEndpoint;
                await FetchModelsAsync();
            }
        }

        private void ApiKeyPasswordBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            AppSettings.OllamaApiKey = ApiKeyPasswordBox.Password ?? "";
        }

        private void ModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing || ModelComboBox.SelectedItem == null) return;
            AppSettings.SelectedModel = ModelComboBox.SelectedItem.ToString() ?? "";
        }

        private void ProviderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing || ProviderComboBox.SelectedItem is not string provider) return;
            AppSettings.SelectedProvider = provider;
            LlmRuntime.ApplyProviderSelection(provider, AppSettings.SelectedModel);

            if (provider != "ollama")
            {
                Toaster.Show(
                    $"Provider '{provider}' requires environment configuration (XAI_API_KEY / GEMINI_API_KEY). See .env.example.",
                    InfoBarSeverity.Warning,
                    "Cloud provider");
            }
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

        private void NetworkConfigDirTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            string directory = (NetworkConfigDirTextBox.Text ?? "").Trim();
            if (directory.Length == 0 || directory == AppSettings.NetworkConfigDirectory) return;

            AppSettings.NetworkConfigDirectory = directory;
            Toaster.Show(
                "Network Config data folder updated. Restart the app for it to take effect.",
                InfoBarSeverity.Informational,
                "Network Config");
        }

        private void SystemPromptTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            SaveGlobalPrompt();
        }

        private void CustomInstructionsTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            SaveGlobalPrompt();
        }

        private void SavePromptButton_Click(object sender, RoutedEventArgs e) => SaveGlobalPrompt();

        private void ResetPromptButton_Click(object sender, RoutedEventArgs e)
        {
            SystemPromptTextBox.Text = "";
            CustomInstructionsTextBox.Text = "";
            SaveGlobalPrompt();
            PromptStatusText.Text = "Cleared";
        }

        private void SaveGlobalPrompt()
        {
            AppSettings.GlobalSystemPrompt = SystemPromptTextBox.Text ?? "";
            AppSettings.GlobalCustomInstructions = CustomInstructionsTextBox.Text ?? "";
            PromptStatusText.Text = "Saved";
            PromptStatusText.Foreground = new SolidColorBrush(Colors.Green);
        }

        private async System.Threading.Tasks.Task FetchModelsAsync()
        {
            SetLoadingState(true);
            ConnectionStatusText.Text = "Connecting...";
            ConnectionStatusText.Foreground = new SolidColorBrush(Colors.Gray);

            try
            {
                string endpoint = (EndpointTextBox.Text ?? "").Trim();
                string apiKey = ApiKeyPasswordBox.Password ?? "";
                bool connectionChanged = endpoint != AppSettings.OllamaEndpoint || apiKey != AppSettings.OllamaApiKey;

                AppSettings.OllamaEndpoint = endpoint;
                AppSettings.OllamaApiKey = apiKey;

                if (connectionChanged)
                {
                    LlmRuntime.Reset();
                }
                else
                {
                    LlmRuntime.ApplyProviderSelection(AppSettings.SelectedProvider, AppSettings.SelectedModel);
                }

                var models = await LlmRuntime.GetModelsAsync();
                var modelIds = models.Select(m => m.Id).ToList();

                if (modelIds.Count == 0)
                {
                    ConnectionStatusText.Text = "Connected, but no models found.";
                    ConnectionStatusText.Foreground = new SolidColorBrush(Colors.OrangeRed);
                    ModelComboBox.ItemsSource = null;
                    ModelComboBox.IsEnabled = false;
                    AppSettings.SelectedModel = "";
                }
                else
                {
                    ConnectionStatusText.Text = $"Connected ({AppSettings.SelectedProvider})";
                    ConnectionStatusText.Foreground = new SolidColorBrush(Colors.Green);
                    ModelComboBox.IsEnabled = true;

                    _isInitializing = true;
                    ModelComboBox.ItemsSource = modelIds;

                    string previousSelection = AppSettings.SelectedModel;
                    string? modelToSelect = null;

                    if (!string.IsNullOrEmpty(previousSelection) && modelIds.Contains(previousSelection))
                        modelToSelect = previousSelection;
                    else if (modelIds.Contains("qwen2.5-coder:7b"))
                        modelToSelect = "qwen2.5-coder:7b";
                    else
                        modelToSelect = modelIds.First();

                    ModelComboBox.SelectedItem = modelToSelect;
                    AppSettings.SelectedModel = modelToSelect ?? "";
                    _isInitializing = false;
                }
            }
            catch (Exception ex)
            {
                ConnectionStatusText.Text = $"Unable to connect: {ex.Message}";
                ConnectionStatusText.Foreground = new SolidColorBrush(Colors.Red);
                ModelComboBox.ItemsSource = null;
                ModelComboBox.IsEnabled = false;
                AppSettings.SelectedModel = "";
            }
            finally { SetLoadingState(false); }
        }

        private void SetLoadingState(bool isLoading)
        {
            LoadingRing.IsActive = isLoading;
            LoadingRing.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
            RefreshButton.IsEnabled = !isLoading;
        }
    }
}
