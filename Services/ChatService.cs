using System.Threading.Tasks;

namespace networker.Services
{
    /// <summary>
    /// Single seam between the chat UI and the model backend.
    /// Phase 1 delegates to the legacy Ollama client; the Llm router replaces
    /// the implementation once the provider layer lands.
    /// </summary>
    public static class ChatService
    {
        public static async Task<string> CompleteAsync(string message)
        {
            return await OllamaService.ChatAsync(
                AppSettings.OllamaEndpoint,
                AppSettings.OllamaApiKey,
                AppSettings.SelectedModel,
                message);
        }
    }
}
