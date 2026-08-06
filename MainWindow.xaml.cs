using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace networker
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        // Use a static HttpClient to avoid socket exhaustion
        private static readonly HttpClient _httpClient = new HttpClient();

        // Optional: Provide your API key here if your local Ollama is hosted behind a reverse proxy that requires authentication
        private const string OLLAMA_API_KEY = "";

        public MainWindow()
        {
            this.InitializeComponent();
        }

        private bool _isDialogOpen = false; // State guard for dialogs

        private async void RunButton_Click(object sender, RoutedEventArgs e)
        {
            // 1. Safely cast sender to Button once. If it's not a button, exit early.
            if (sender is not Button clickedButton) return;

            // Disable the button to prevent the user from clicking it multiple times
            clickedButton.IsEnabled = false;

            try
            {
                string command = myTextBox.Text;

                if (string.IsNullOrWhiteSpace(command))
                {
                    await ShowDialogAsync("Input Required", "Please enter a prompt/command in the text box.");
                    return;
                }

                var request = new OllamaRequest
                {
                    Model = "llama3",
                    Messages = new List<OllamaMessage>
            {
                new OllamaMessage { Role = "user", Content = command }
            },
                    Stream = false
                };

                if (!string.IsNullOrWhiteSpace(OLLAMA_API_KEY))
                {
                    _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", OLLAMA_API_KEY);
                }

                var response = await _httpClient.PostAsJsonAsync("http://localhost:11434/api/chat", request);
                response.EnsureSuccessStatusCode();

                var result = await response.Content.ReadFromJsonAsync<OllamaResponse>();
                string aiResponse = result?.Message?.Content ?? "No response received from AI.";

                // If you added an output TextBlock to your XAML, uncomment the line below:
                // outputTextBlock.Text = aiResponse;

                await ShowDialogAsync("AI Response", aiResponse);
            }
            catch (HttpRequestException httpEx)
            {
                await ShowDialogAsync("Connection Error", $"Could not reach the Ollama API. Ensure it is running on http://localhost:11434.\n\nDetails: {httpEx.Message}");
            }
            catch (Exception ex)
            {
                await ShowDialogAsync("Unexpected Error", $"An error occurred: {ex.Message}");
            }
            finally
            {
                // 2. Re-enable the button when the operation (and any resulting dialogs) is completely finished
                clickedButton.IsEnabled = true;
            }
        }

        private async Task ShowDialogAsync(string title, string content)
        {
            // 3. Prevent opening a second dialog if one is somehow still lingering
            if (_isDialogOpen) return;

            _isDialogOpen = true;
            try
            {
                var dialog = new ContentDialog
                {
                    Title = title,
                    Content = content,
                    CloseButtonText = "Ok",
                    XamlRoot = this.Content.XamlRoot
                };

                // Add a tiny delay to allow the UI thread to fully close any previously dismissed dialogs
                await Task.Delay(50);

                await dialog.ShowAsync();
            }
            finally
            {
                _isDialogOpen = false;
            }
        }

        // --- Ollama API Data Models ---

        public class OllamaRequest
        {
            [JsonPropertyName("model")]
            public string Model { get; set; } = "llama3";

            [JsonPropertyName("messages")]
            public List<OllamaMessage> Messages { get; set; } = new List<OllamaMessage>();

            [JsonPropertyName("stream")]
            public bool Stream { get; set; } = false;
        }

        public class OllamaMessage
        {
            [JsonPropertyName("role")]
            public string Role { get; set; } = "user";

            [JsonPropertyName("content")]
            public string Content { get; set; } = "";
        }

        public class OllamaResponse
        {
            [JsonPropertyName("message")]
            public OllamaMessage Message { get; set; }
        }
    }
}