using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace networker
{
    public sealed partial class MainPage : Page
    {
        public MainPage()
        {
            this.InitializeComponent();
            this.Loaded += (s, e) => UpdateRunButtonState();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            UpdateRunButtonState();
        }

        private void UpdateRunButtonState()
        {
            if (string.IsNullOrWhiteSpace(AppSettings.SelectedModel))
            {
                WarningText.Text = "⚠ No model selected. Please configure Ollama in Settings.";
                WarningText.Visibility = Visibility.Visible;
                RunButton.IsEnabled = false;
            }
            else
            {
                WarningText.Visibility = Visibility.Collapsed;
                RunButton.IsEnabled = true;
            }
        }

        private async void RunButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button clickedButton) return;
            string command = myTextBox.Text;
            if (string.IsNullOrWhiteSpace(command)) return;

            clickedButton.IsEnabled = false;
            LoadingRing.IsActive = true; LoadingRing.Visibility = Visibility.Visible;
            outputTextBlock.Text = "Thinking...";

            try
            {
                string response = await OllamaService.ChatAsync(AppSettings.OllamaEndpoint, AppSettings.OllamaApiKey, AppSettings.SelectedModel, command);
                outputTextBlock.Text = response;
            }
            catch (Exception ex) { outputTextBlock.Text = $"Error: {ex.Message}"; }
            finally
            {
                clickedButton.IsEnabled = !string.IsNullOrWhiteSpace(AppSettings.SelectedModel);
                LoadingRing.IsActive = false; LoadingRing.Visibility = Visibility.Collapsed;
            }
        }
    }
}