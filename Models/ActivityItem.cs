using System;

namespace networker.Models
{
    /// <summary>
    /// A single entry in the workspace's recent-activity feed (dashboard).
    /// </summary>
    public sealed class ActivityItem
    {
        public string Title { get; set; } = "Tool result";

        public string? Detail { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.Now;

        /// <summary>Segoe MDL2 glyph shown in the feed row.</summary>
        public string Glyph { get; set; } = "\uE774";

        public string RelativeTime => FormatRelative(Timestamp);

        private static string FormatRelative(DateTime timestamp)
        {
            var delta = DateTime.Now - timestamp;
            if (delta.TotalMinutes < 1) return "now";
            if (delta.TotalMinutes < 60) return $"{(int)delta.TotalMinutes}m ago";
            if (delta.TotalHours < 24) return $"{(int)delta.TotalHours}h ago";
            if (delta.TotalDays < 7) return $"{(int)delta.TotalDays}d ago";
            return timestamp.ToString("MMM d");
        }
    }
}
