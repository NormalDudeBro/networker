using System;
using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

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
            EndpointTextBox.Text = AppSettings.OllamaEndpoint;
            ApiKeyPasswordBox.Password = AppSettings.OllamaApiKey;
            SystemPromptTextBox.Text = AppSettings.GlobalSystemPrompt;
            CustomInstructionsTextBox.Text = AppSettings.GlobalCustomInstructions;
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
                AppSettings.OllamaEndpoint = (EndpointTextBox.Text ?? "").Trim();
                AppSettings.OllamaApiKey = ApiKeyPasswordBox.Password ?? "";

                var models = await OllamaService.GetModelsAsync(AppSettings.OllamaEndpoint, AppSettings.OllamaApiKey);

                if (models == null || models.Count == 0)
                {
                    ConnectionStatusText.Text = "⚠ Connected, but no models found.";
                    ConnectionStatusText.Foreground = new SolidColorBrush(Colors.OrangeRed);
                    ModelComboBox.ItemsSource = null;
                    ModelComboBox.IsEnabled = false;
                    AppSettings.SelectedModel = "";
                }
                else
                {
                    ConnectionStatusText.Text = "✅ Connected";
                    ConnectionStatusText.Foreground = new SolidColorBrush(Colors.Green);
                    ModelComboBox.IsEnabled = true;

                    _isInitializing = true;
                    ModelComboBox.ItemsSource = models;

                    string previousSelection = AppSettings.SelectedModel;
                    string? modelToSelect = null;

                    if (!string.IsNullOrEmpty(previousSelection) && models.Contains(previousSelection))
                        modelToSelect = previousSelection;
                    else if (models.Contains("qwen2.5-coder:7b"))
                        modelToSelect = "qwen2.5-coder:7b";
                    else
                        modelToSelect = models.First();

                    ModelComboBox.SelectedItem = modelToSelect;
                    AppSettings.SelectedModel = modelToSelect ?? "";
                    _isInitializing = false;
                }
            }
            catch (Exception ex)
            {
                ConnectionStatusText.Text = $"❌ Unable to connect: {ex.Message}";
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
