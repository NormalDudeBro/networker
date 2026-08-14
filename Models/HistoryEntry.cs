using System;

namespace networker.Models
{
    /// <summary>
    /// Lightweight search/display item for the side history panel. Both plain
    /// <see cref="ChatMessage"/>s and structured <see cref="AssistantTurn"/>s are
    /// flattened into this so the history list template can stay simple.
    /// </summary>
    public sealed class HistoryEntry
    {
        public string Text { get; init; } = string.Empty;

        public DateTime Timestamp { get; init; }

        /// <summary>The underlying chat item to scroll to when clicked.</summary>
        public object? Target { get; init; }
    }
}
