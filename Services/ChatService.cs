using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Networker.Core.Llm;
using Networker.Core.Prompting;

namespace networker.Services
{
    /// <summary>
    /// Single seam between the chat UI and the model backend. Composes the
    /// global prompt via <see cref="PromptBuilder"/>, scrubs any configured
    /// credentials from outgoing content, and routes through the
    /// <see cref="LlmRouter"/> with retry and provider fallback.
    /// </summary>
    public static class ChatService
    {
        public static async Task<string> CompleteAsync(string message, CancellationToken cancellationToken = default)
        {
            var messages = BuildMessages(message);
            var response = await LlmRuntime.Router.CompleteAsync(messages, cancellationToken);
            return response.Content;
        }

        public static IAsyncEnumerable<string> StreamAsync(string message, CancellationToken cancellationToken = default)
        {
            var messages = BuildMessages(message);
            return LlmRuntime.Router.StreamAsync(messages, cancellationToken);
        }

        private static List<LlmMessage> BuildMessages(string message)
        {
            var messages = new List<LlmMessage>(2);

            string systemPrompt = PromptBuilder.BuildSystemPrompt(
                AppSettings.GlobalSystemPrompt,
                AppSettings.GlobalCustomInstructions);
            if (systemPrompt.Length > 0)
            {
                messages.Add(LlmMessage.System(systemPrompt));
            }

            string scrubbed = CredentialScrubber.Scrub(message, Secrets());
            messages.Add(LlmMessage.User(scrubbed));
            return messages;
        }

        private static IEnumerable<string?> Secrets()
        {
            var config = LlmRuntime.Config;
            return new[]
            {
                config.OllamaApiKey,
                config.XaiApiKey,
                config.GeminiApiKey,
            };
        }
    }
}

