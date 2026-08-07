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
            var messages = BuildMessages(message, systemPrompt: null);
            var response = await LlmRuntime.Router.CompleteAsync(messages, cancellationToken);
            return response.Content;
        }

        public static IAsyncEnumerable<string> StreamAsync(string message, CancellationToken cancellationToken = default)
        {
            var messages = BuildMessages(message, systemPrompt: null);
            return LlmRuntime.Router.StreamAsync(messages, cancellationToken);
        }

        /// <summary>
        /// One-shot completion with a task-specific system prompt (used by the
        /// AI-assisted tools). The global system prompt and custom instructions
        /// are appended after it.
        /// </summary>
        public static async Task<string> CompleteAsync(string message, string? systemPrompt, CancellationToken cancellationToken = default)
        {
            var messages = BuildMessages(message, systemPrompt);
            var response = await LlmRuntime.Router.CompleteAsync(messages, cancellationToken);
            return response.Content;
        }

        /// <summary>
        /// Streaming completion with a task-specific system prompt (used by the
        /// AI-assisted tools). The global system prompt and custom instructions
        /// are appended after it.
        /// </summary>
        public static IAsyncEnumerable<string> StreamAsync(string message, string? systemPrompt, CancellationToken cancellationToken = default)
        {
            var messages = BuildMessages(message, systemPrompt);
            return LlmRuntime.Router.StreamAsync(messages, cancellationToken);
        }

        /// <summary>
        /// True when a model is selected and the router has something to talk to.
        /// The AI tool buttons surface a warning (rather than a silent failure)
        /// when this is false.
        /// </summary>
        public static bool IsModelSelected
            => !string.IsNullOrWhiteSpace(AppSettings.SelectedModel);

        private static List<LlmMessage> BuildMessages(string message, string? systemPrompt)
        {
            var messages = new List<LlmMessage>(2);

            string global = PromptBuilder.BuildSystemPrompt(
                AppSettings.GlobalSystemPrompt,
                AppSettings.GlobalCustomInstructions);
            string system = PromptBuilder.JoinNonEmpty(systemPrompt, global);
            if (system.Length > 0)
            {
                messages.Add(LlmMessage.System(system));
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

