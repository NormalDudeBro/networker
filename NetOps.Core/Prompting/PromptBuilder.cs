using System.Collections.Generic;

namespace NetOps.Core.Prompting
{
    /// <summary>
    /// Centralized builder for model prompts. This is the single source of truth
    /// for how a user message is composed before it is sent to the model.
    /// </summary>
    public static class PromptBuilder
    {
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

            AddIfPresent(sections, systemPrompt);
            AddIfPresent(sections, customInstructions);

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
