using System.Collections.Generic;
using System.Linq;

namespace Networker.Core.Prompting
{
    /// <summary>
    /// Centralized builder for model prompts. This is the single source of truth
    /// for how a user message is composed before it is sent to the model.
    /// </summary>
    public static class PromptBuilder
    {
        /// <summary>
        /// Composes a system-role prompt from the configured global prompt parts
        /// (system prompt, then custom instructions). Empty parts are omitted.
        /// Used to attach the global prompt once as a system message when
        /// multi-turn conversation history is sent to the model.
        /// </summary>
        public static string BuildSystemPrompt(string? systemPrompt, string? customInstructions)
            => JoinNonEmpty(systemPrompt, customInstructions);

        /// <summary>
        /// Joins any number of optional prompt sections with blank-line separators,
        /// trimming each and omitting empty/whitespace-only sections. Used to merge
        /// a tool-specific system prompt with the global prompt and custom
        /// instructions into a single system message.
        /// </summary>
        public static string JoinNonEmpty(params string?[] sections)
            => string.Join(
                "\n\n",
                sections.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()));

        /// <summary>
        /// Composes a user message from the configured global prompt parts.
        /// Parts are always prepended in the order: system prompt, custom
        /// instructions, then the user's message. Empty parts are omitted so that
        /// with no global prompt configured the user message is passed through
        /// unchanged.
        /// </summary>
        public static string BuildUserMessage(string? systemPrompt, string? customInstructions, string userMessage)
        {
            var sections = new List<string>(3);

            string system = BuildSystemPrompt(systemPrompt, customInstructions);
            if (system.Length > 0)
            {
                sections.Add(system);
            }

            if (!string.IsNullOrEmpty(userMessage))
            {
                sections.Add(userMessage);
            }

            return string.Join("\n\n", sections);
        }

        private static void AddIfPresent(List<string> sections, string? value)
        {
            string trimmed = value?.Trim() ?? "";
            if (!string.IsNullOrEmpty(trimmed))
            {
                sections.Add(trimmed);
            }
        }
    }
}

