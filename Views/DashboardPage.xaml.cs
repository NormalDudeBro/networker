using System;
using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using networker.Services;

namespace networker.Views
{
    /// <summary>Dashboard quick-action descriptor.</summary>
    public sealed record QuickAction(string Glyph, string Title, string Subtitle, string Target);

    /// <summary>
    /// Landing page: workspace overview with quick actions, live AI connection
    /// state, keyboard shortcuts, and the shared recent-activity feed.
    /// </summary>
    public sealed partial class DashboardPage : Page
    {
        private static readonly QuickAction[] Actions =
        {
            new("\uE8BD", "New chat", "Start a fresh conversation", "assistant"),
            new("\uE774", "Subnet calculator", "CIDR math, hosts, masks", "tools:IP Calculator"),
            new("\uE943", "Generate config", "Cisco / Juniper device configs", "config:Generate"),
            new("\uE8FD", "Audit config", "Security & best-practice scan", "tools:Config Audit"),
            new("\uE8C8", "Diff configs", "Compare two configurations", "config:Diff"),
            new("\uE721", "Analyze logs", "Find anomalies in device logs", "tools:Log Analyzer"),
            new("\uE703", "Topology", "Build a topology from configs", "tools:Topology"),
            new("\uE717", "Vault", "Encrypted credentials & variables", "config:Vault"),
        };

        public DashboardPage()
        {
            this.InitializeComponent();
            QuickActionsGrid.ItemsSource = Actions;
            ActivityList.ItemsSource = RecentActivity.Items;
            RecentActivity.Items.CollectionChanged += Activity_CollectionChanged;
            LlmSession.Changed += LlmSession_Changed;
            UpdateActivityEmptyState();
            UpdateAiCard();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            RecentActivity.Items.CollectionChanged -= Activity_CollectionChanged;
            LlmSession.Changed -= LlmSession_Changed;
        }

        private void LlmSession_Changed() => DispatcherQueue.TryEnqueue(UpdateAiCard);

        private void Activity_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateActivityEmptyState();

        private void UpdateActivityEmptyState()
            => ActivityEmpty.Visibility = RecentActivity.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        private void UpdateAiCard()
        {
            string dotKey = LlmSession.IsChecking ? "AppTextDisabledBrush"
                : LlmSession.IsConnected ? "AppOnlineBrush"
                : "AppOfflineBrush";
            AiStatusDot.Fill = (SolidColorBrush)Application.Current.Resources[dotKey];
            AiStatusText.Text = LlmSession.StatusMessage;
            AiProviderText.Text = LlmSession.Provider;
            AiModelText.Text = string.IsNullOrWhiteSpace(LlmSession.Model) ? "no model" : LlmSession.Model;
            AiRefreshRing.IsActive = LlmSession.IsChecking;
        }

        private void DashboardLayout_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            bool stackRail = e.NewSize.Width < 840;
            DashboardRightColumn.Width = stackRail ? new GridLength(0) : new GridLength(348);
            Grid.SetColumn(DashboardRightRail, stackRail ? 0 : 1);
            Grid.SetRow(DashboardRightRail, stackRail ? 1 : 0);
        }

        private void QuickActionsGrid_Loaded(object sender, RoutedEventArgs e) => UpdateQuickActionWidth();

        private void QuickActionsGrid_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateQuickActionWidth();

        private void UpdateQuickActionWidth()
        {
            if (QuickActionsGrid.ItemsPanelRoot is not ItemsWrapGrid panel || QuickActionsGrid.ActualWidth <= 0)
            {
                return;
            }

            double width = QuickActionsGrid.ActualWidth;
            int columns = width >= 760 ? 4 : width >= 540 ? 3 : width >= 340 ? 2 : 1;
            panel.ItemWidth = Math.Max(140, Math.Floor((width - columns * 10) / columns));
        }

        private void QuickAction_Click(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is not QuickAction action) return;
            if (action.Target == "assistant")
            {
                MainWindow.Instance?.OpenAssistantNewChat();
                return;
            }

            string[] parts = action.Target.Split(':', 2);
            if (parts.Length == 2)
            {
                MainWindow.Instance?.NavigateToTab(parts[0], parts[1]);
            }
        }

        private void NewChatButton_Click(object sender, RoutedEventArgs e) => MainWindow.Instance?.OpenAssistantNewChat();

        private void AiCheckButton_Click(object sender, RoutedEventArgs e) => _ = LlmSession.RefreshAsync();

        private void AiConfigureButton_Click(object sender, RoutedEventArgs e) => MainWindow.Instance?.NavigateTo("settings");
    }
}
