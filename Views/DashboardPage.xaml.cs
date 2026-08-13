using System;
using System.Collections.Specialized;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using networker.Services;
using Networker.Core.Workflow;

namespace networker.Views
{
    public sealed partial class DashboardPage : Page
    {
        private readonly TroubleshootingSession _session;
        private bool _restoring;
        private bool _subscribed;

        public DashboardPage()
        {
            InitializeComponent();
            _session = ((App)Application.Current).Services.GetRequiredService<TroubleshootingSession>();
            ActivityList.ItemsSource = RecentActivity.Items;
            RestoreWorkspace();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (!_subscribed)
            {
                RecentActivity.Items.CollectionChanged += ActivityChanged;
                LlmSession.Changed += LlmChanged;
                _session.Changed += SessionChanged;
                _subscribed = true;
            }
            UpdateSummary();
            if (!string.IsNullOrWhiteSpace(_session.RestoreWarning)) WorkspaceStatus.Text = _session.RestoreWarning;
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            if (!_subscribed) return;
            RecentActivity.Items.CollectionChanged -= ActivityChanged;
            LlmSession.Changed -= LlmChanged;
            _session.Changed -= SessionChanged;
            _subscribed = false;
        }

        private void RestoreWorkspace()
        {
            _restoring = true;
            IncidentTitleInput.Text = _session.Current.Incident.Title;
            SymptomsInput.Text = _session.Current.Incident.Symptoms;
            ContextInput.Text = _session.Current.Incident.Context;
            _restoring = false;
            UpdateSummary();
        }

        private void IncidentInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_restoring) return;
            _session.Current.Incident.Title = IncidentTitleInput.Text;
            _session.Current.Incident.Symptoms = SymptomsInput.Text;
            _session.Current.Incident.Context = ContextInput.Text;
            IncidentTitleError.Visibility = Visibility.Collapsed;
            SymptomsError.Visibility = Visibility.Collapsed;
            _session.NotifyChanged();
        }

        private void StartInvestigation_Click(object sender, RoutedEventArgs e)
        {
            bool valid = true;
            if (string.IsNullOrWhiteSpace(IncidentTitleInput.Text))
            {
                IncidentTitleError.Text = "Enter an incident title.";
                IncidentTitleError.Visibility = Visibility.Visible;
                IncidentTitleInput.Focus(FocusState.Programmatic);
                valid = false;
            }
            if (string.IsNullOrWhiteSpace(SymptomsInput.Text))
            {
                SymptomsError.Text = "Describe the symptoms or known evidence.";
                SymptomsError.Visibility = Visibility.Visible;
                if (valid) SymptomsInput.Focus(FocusState.Programmatic);
                valid = false;
            }
            if (!valid)
            {
                _session.SetError(WorkflowStage.Start, "Incident title and symptoms are required.");
                WorkspaceStatus.Text = "Complete the required incident fields.";
                return;
            }

            _session.SetCompleted(WorkflowStage.Start, "Incident intake complete.");
            RecentActivity.Add(new Models.ActivityItem { Title = "Investigation started", Detail = IncidentTitleInput.Text, Timestamp = DateTime.Now, Glyph = "\uE8A5" });
            MainWindow.Instance?.NavigateToStage(WorkflowStage.Inspect);
        }

        private async void ClearWorkspace_Click(object sender, RoutedEventArgs e)
        {
            if (_session.Current.IsEmpty) return;
            var dialog = new ContentDialog
            {
                Title = "Clear troubleshooting workspace?",
                Content = "This removes saved evidence, results, progress, activity, and chat. Settings, templates, and the encrypted vault are retained.",
                PrimaryButtonText = "Clear workspace",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            _session.Clear();
            RecentActivity.Clear();
            RestoreWorkspace();
            WorkspaceStatus.Text = "Workspace cleared.";
        }

        private void UpdateSummary()
        {
            int completed = Enum.GetValues<WorkflowStage>().Count(stage => stage != WorkflowStage.Settings && _session.Current.GetProgress(stage).State == WorkflowProgressState.Completed);
            ProgressText.Text = $"{completed} of 8 action stages completed";
            WorkflowProgress.Value = completed;
            UpdatedText.Text = _session.Current.UpdatedAt == default ? "Not saved yet" : $"Updated {_session.Current.UpdatedAt.ToLocalTime():g}";
            ActivityEmpty.Visibility = RecentActivity.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            StartButton.Content = _session.Current.GetProgress(WorkflowStage.Start).State == WorkflowProgressState.Completed ? "Continue to Inspect" : "Start investigation";
            UpdateAi();
        }

        private void UpdateAi()
        {
            AiStatusText.Text = LlmSession.StatusMessage;
            AiModelText.Text = string.IsNullOrWhiteSpace(LlmSession.Model) ? "No model selected" : $"{LlmSession.Provider} / {LlmSession.Model}";
            AiStatusDot.Fill = (SolidColorBrush)Application.Current.Resources[LlmSession.IsConnected ? "AppOnlineBrush" : "AppOfflineBrush"];
        }

        private void ActivityChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateSummary();
        private void LlmChanged() => DispatcherQueue.TryEnqueue(UpdateAi);
        private void SessionChanged() => DispatcherQueue.TryEnqueue(UpdateSummary);
        private void AiCheckButton_Click(object sender, RoutedEventArgs e) => _ = LlmSession.RefreshAsync();
        private void AiConfigureButton_Click(object sender, RoutedEventArgs e) => MainWindow.Instance?.NavigateToStage(WorkflowStage.Settings, "ai");
    }
}
