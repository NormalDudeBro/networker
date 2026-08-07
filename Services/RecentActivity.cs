using System.Collections.ObjectModel;
using networker.Models;

namespace networker.Services
{
    /// <summary>
    /// Shared, bounded recent-activity log surfaced on the dashboard. Any page can
    /// publish an entry; the dashboard binds directly to <see cref="Items"/>.
    /// </summary>
    public static class RecentActivity
    {
        private const int MaxItems = 30;

        public static ObservableCollection<ActivityItem> Items { get; } = new();

        public static void Add(ActivityItem item)
        {
            Items.Insert(0, item);
            while (Items.Count > MaxItems)
            {
                Items.RemoveAt(Items.Count - 1);
            }
        }

        public static void Clear() => Items.Clear();
    }
}
