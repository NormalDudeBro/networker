using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using networker.Models;
using networker.Services;

namespace networker
{
    public sealed partial class MainPage : Page
    {
        public static MainPage? Current { get; private set; }

        private readonly ObservableCollection<ChatMessage> _messages = new();

        public MainPage()
        {
            this.InitializeComponent();
            MessagesList.ItemsSource = _messages;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            Current = this;

            if (ProviderComboBox.Items.Count == 0)
            {
                ProviderComboBox.Items.Add("ollama");
                ProviderComboBox.SelectedIndex = 0;
            }

            UpdateProviderLabel();
            _ = RefreshConnectionAsync();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            if (Current == this) Current = null;
        }

        // ============================ Sending ============================

        private async void SendButton_Click(object sender, RoutedEventArgs e) => await SendAsync();

        private void InputBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter &&
                (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
                 .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down)))
            {
                e.Handled = true;
                _ = SendAsync();
            }
        }

        private async Task SendAsync()
        {
            string text = InputBox.Text;
            if (string.IsNullOrWhiteSpace(text)) return;

            if (string.IsNullOrWhiteSpace(AppSettings.SelectedModel))
            {
                Toaster.Show("No model selected. Refresh models in the Assistant panel or Settings.", InfoBarSeverity.Warning, "Model required");
                return;
            }

            var userMessage = new ChatMessage { Role = ChatRole.User, Text = text };
            _messages.Add(userMessage);
            InputBox.Text = "";
            ShowChat();

            var assistant = new ChatMessage
            {
                Role = ChatRole.Assistant,
                Text = "Thinking…",
                IsStreaming = true,
                Provider = AppSettings.SelectedProvider,
                Model = AppSettings.SelectedModel
            };
            _messages.Add(assistant);
            SetBusy(true);

            try
            {
                string response = await ChatService.CompleteAsync(text);
                assistant.Text = response;
            }
            catch (Exception ex)
            {
                _messages.Add(new ChatMessage { Role = ChatRole.Error, Text = ex.Message });
                Toaster.Show(ex.Message, InfoBarSeverity.Error, "Request failed");
            }
            finally
            {
                assistant.IsStreaming = false;
                SetBusy(false);
                ScrollToBottom();
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            // Real cancellation lands with the streaming provider layer.
        }

        private void SetBusy(bool busy)
        {
            SendButton.IsEnabled = !busy;
            CancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ShowChat()
        {
            EmptyState.Visibility = Visibility.Collapsed;
            MessagesList.Visibility = Visibility.Visible;
            ScrollToBottom();
        }

        private void ScrollToBottom()
        {
            if (_messages.Count > 0)
            {
                MessagesList.ScrollIntoView(_messages[^1]);
            }
        }

        // ============================ Quick actions ============================

        private readonly Dictionary<string, string> _quickTemplates = new()
        {
            ["ip"] = "Calculate the subnet details for 192.168.10.0/24, including usable host range and wildcard mask.",
            ["ospf"] = "Generate an OSPF configuration for a Cisco IOS router in area 0 on networks 10.0.0.0/24 and 10.0.1.0/24.",
            ["bgp"] = "Generate a BGP configuration for a Cisco IOS router announcing 203.0.113.0/24 to neighbor 192.0.2.1.",
            ["audit"] = "Audit the following network device configuration and report security and best-practice issues:\n\n",
            ["bgp-trouble"] = "Troubleshoot a BGP peer that keeps flapping between the two routers in my network.",
            ["explain"] = "Explain why BGP sessions flap and what checks to run first.",
            ["logs"] = "Analyze the following device logs and tell me what needs attention:\n\n"
        };

        private void QuickStart_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string tag }) return;
            if (_quickTemplates.TryGetValue(tag, out string? template) && template is not null)
            {
                InputBox.Text = template;
                InputBox.Focus(FocusState.Programmatic);
            }
        }

        // ============================ Header / panel ============================

        private void PaletteButton_Click(object sender, RoutedEventArgs e) => MainWindow.Instance?.OpenPalette();

        private void ThemeButton_Click(object sender, RoutedEventArgs e) => MainWindow.Instance?.ToggleTheme();

        private void PanelToggleButton_Click(object sender, RoutedEventArgs e)
        {
            bool isVisible = AssistantPanel.Visibility == Visibility.Visible;
            AssistantPanel.Visibility = isVisible ? Visibility.Collapsed : Visibility.Visible;
        }

        private void NewChatButton_Click(object sender, RoutedEventArgs e) => NewChat();

        public void NewChat()
        {
            _messages.Clear();
            MessagesList.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Visible;
            InputBox.Text = "";
            RefreshHistory();
        }

        public void ClearHistory()
        {
            if (_messages.Count == 0)
            {
                Toaster.Show("No messages to clear.", InfoBarSeverity.Informational);
                return;
            }

            var dialog = new ContentDialog
            {
                Title = "Clear history?",
                Content = "This removes all conversation messages from the current workspace. This cannot be undone.",
                PrimaryButtonText = "Clear",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };
            dialog.PrimaryButtonClick += (s, e) =>
            {
                _messages.Clear();
                RefreshHistory();
                NewChat();
                Toaster.Show("History cleared.", InfoBarSeverity.Success);
            };
            _ = dialog.ShowAsync();
        }

        private void ClearHistoryButton_Click(object sender, RoutedEventArgs e) => ClearHistory();

        // ============================ Provider / models / health ============================

        private void UpdateProviderLabel()
        {
            ProviderText.Text = AppSettings.SelectedProvider;
            ModelText.Text = string.IsNullOrWhiteSpace(AppSettings.SelectedModel) ? "no model" : AppSettings.SelectedModel;
        }

        private async void HealthCheckButton_Click(object sender, RoutedEventArgs e) => await RefreshConnectionAsync();

        public async Task RefreshConnectionAsync()
        {
            PanelHealthText.Text = "Checking…";
            PanelHealthDot.Fill = new SolidColorBrush(Colors.Gray);
            HealthText.Text = "Checking";
            HealthDot.Fill = new SolidColorBrush(Colors.Gray);

            try
            {
                var models = await OllamaService.GetModelsAsync(AppSettings.OllamaEndpoint, AppSettings.OllamaApiKey);
                SetHealthy(models);
            }
            catch (Exception ex)
            {
                SetUnhealthy(ex.Message);
            }

            await LoadModelsAsync();
            UpdateProviderLabel();
        }

        public void RefreshConnection()
        {
            _ = RefreshConnectionAsync();
        }

        private void SetHealthy(IReadOnlyList<string> models)
        {
            var green = new SolidColorBrush(Colors.LightGreen);
            PanelHealthDot.Fill = green;
            HealthDot.Fill = green;
            PanelHealthText.Text = "Connected";
            HealthText.Text = "Connected";

            if (models.Count == 0)
            {
                PanelHealthText.Text = "Connected — no models";
            }
        }

        private void SetUnhealthy(string message)
        {
            var red = new SolidColorBrush(Colors.OrangeRed);
            PanelHealthDot.Fill = red;
            HealthDot.Fill = red;
            string shortMessage = message.Length > 60 ? message[..60] : message;
            PanelHealthText.Text = $"Offline: {shortMessage}";
            HealthText.Text = "Offline";
        }

        private async Task LoadModelsAsync()
        {
            ModelLoadingRing.IsActive = true;
            try
            {
                var models = await OllamaService.GetModelsAsync(AppSettings.OllamaEndpoint, AppSettings.OllamaApiKey);
                if (models == null || models.Count == 0)
                {
                    ModelComboBox.ItemsSource = null;
                    ModelComboBox.IsEnabled = false;
                    AppSettings.SelectedModel = "";
                }
                else
                {
                    ModelComboBox.IsEnabled = true;
                    ModelComboBox.ItemsSource = models;

                    string previous = AppSettings.SelectedModel;
                    ModelComboBox.SelectedItem = !string.IsNullOrEmpty(previous) && models.Contains(previous)
                        ? previous
                        : models[0];
                    if (ModelComboBox.SelectedItem is string selected)
                    {
                        AppSettings.SelectedModel = selected;
                    }
                }
            }
            catch
            {
                ModelComboBox.ItemsSource = null;
                ModelComboBox.IsEnabled = false;
            }
            finally
            {
                ModelLoadingRing.IsActive = false;
                UpdateProviderLabel();
            }
        }

        private void ProviderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ProviderComboBox.SelectedItem is string provider)
            {
                AppSettings.SelectedProvider = provider;
                UpdateProviderLabel();
            }
        }

        private void ModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ModelComboBox.SelectedItem is string model)
            {
                AppSettings.SelectedModel = model;
                UpdateProviderLabel();
            }
        }

        // ============================ History ============================

        private void RefreshHistory()
        {
            HistoryList.ItemsSource = null;
            HistoryList.ItemsSource = FilterHistory();
        }

        private IReadOnlyList<ChatMessage> FilterHistory()
        {
            string query = (HistorySearchBox.Text ?? "").Trim();
            var all = _messages.Reverse().ToList();
            if (string.IsNullOrEmpty(query))
            {
                return all.Take(100).ToList();
            }
            return all.Where(m => m.Text.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(100).ToList();
        }

        private void HistorySearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshHistory();

        private void HistoryList_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is ChatMessage message)
            {
                MessagesList.ScrollIntoView(message);
            }
        }

        // ============================ Input growth ============================

        private void InputBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            double lines = InputBox.Text.Split('\n').Length;
            double height = Math.Clamp(24 + (lines * 20), 36, 160);
            InputBox.Height = height;
        }
    }
}
