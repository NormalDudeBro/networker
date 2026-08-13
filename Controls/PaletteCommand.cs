using System;

namespace networker.Controls
{
    /// <summary>
    /// A single entry in the global command palette.
    /// </summary>
    public sealed class PaletteCommand
    {
        public PaletteCommand(string title, string subtitle, string glyph, Action action, params string[] keywords)
            : this(title, subtitle, glyph, string.Empty, string.Empty, action, keywords)
        {
        }

        public PaletteCommand(
            string title,
            string subtitle,
            string glyph,
            string category,
            string shortcut,
            Action action,
            params string[] keywords)
        {
            Title = title;
            Subtitle = subtitle;
            Glyph = glyph;
            Category = category;
            Shortcut = shortcut;
            Action = action;
            Keywords = keywords;
        }

        public string Title { get; }

        public string Subtitle { get; }

        public string Glyph { get; }

        public string Category { get; }

        public string Shortcut { get; }

        public Action Action { get; }

        public string[] Keywords { get; }

        /// <summary>
        /// Case-insensitive match against the title, subtitle and keywords.
        /// </summary>
        public bool IsMatch(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return true;
            if (Title.Contains(query, StringComparison.OrdinalIgnoreCase)) return true;
            if (Subtitle.Contains(query, StringComparison.OrdinalIgnoreCase)) return true;
            foreach (var keyword in Keywords)
            {
                if (keyword.Contains(query, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }
}
