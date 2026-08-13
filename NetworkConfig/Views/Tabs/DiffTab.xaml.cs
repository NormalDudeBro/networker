using System;
using System.Linq;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using networker.Models;
using networker.Services;
using Networker.Core.NetTools.Config;

namespace networker.NetworkConfig.Views.Tabs
{
    /// <summary>
    /// Diff tab — compare two configurations with <see cref="TextDiff"/>.
    /// Ported from NetworkConfigPro's Diff tab (<c>_compare_configs</c>); the
    /// Python UI rendered <c>difflib.unified_diff</c> output, which the C#
    /// TextDiff engine reproduces as a marker-prefixed unified-style diff.
    /// </summary>
    public sealed partial class DiffTab : UserControl
    {
        public event Action? WorkspaceChanged;
        public event Action<string>? ActionCompleted;
        public event Action<string>? ActionFailed;
        public DiffTab()
        {
            this.InitializeComponent();
            DiffLeftText.TextChanged += (_, _) => WorkspaceChanged?.Invoke();
            DiffRightText.TextChanged += (_, _) => WorkspaceChanged?.Invoke();
        }

        private void Compare_Click(object sender, RoutedEventArgs e)
        {
            var left = DiffLeftText.Text;
            var right = DiffRightText.Text;
            if (left.Length == 0 || right.Length == 0)
            {
                SetStatus("Please paste configurations in both panels", error: true);
                ActionFailed?.Invoke("Both baseline and revised configurations are required.");
                return;
            }

            var diff = TextDiff.DiffLines(left, right);
            var additions = diff.Count(line => line.Kind == DiffLineKind.Added);
            var deletions = diff.Count(line => line.Kind == DiffLineKind.Removed);

            if (additions == 0 && deletions == 0)
            {
                ShowResults("Configurations are identical - no differences found.");
                DiffStatsText.Text = "0 additions, 0 deletions";
                SetStatus("Configurations are identical");
                LogActivity("Config Diff", "Configurations are identical", "\uE8C8");
                ActionCompleted?.Invoke("Configurations are identical.");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("--- Configuration A");
            sb.AppendLine("+++ Configuration B");
            sb.Append(TextDiff.ToUnified(diff));

            ShowResults(sb.ToString());
            DiffStatsText.Text = $"{additions} additions, {deletions} deletions";
            SetStatus($"Diff complete: {additions} additions, {deletions} deletions");
            LogActivity("Config Diff", $"{additions} additions, {deletions} deletions", "\uE8C8");
            ActionCompleted?.Invoke($"{additions} additions and {deletions} deletions found.");
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            DiffLeftText.Text = string.Empty;
            DiffRightText.Text = string.Empty;
            DiffResultsText.Text = string.Empty;
            DiffStatsText.Text = string.Empty;
            SetStatus("Diff cleared");
        }

        private void SetStatus(string message, bool error = false)
        {
            StatusText.Text = message;
            StatusText.Style = (Style)Application.Current.Resources[
                error ? "InlineErrorTextStyle" : "InlineStatusTextStyle"];
        }

        private void ShowResults(string text)
        {
            DiffResultsText.Text = text;
            DiffResultsText.Focus(FocusState.Programmatic);
        }

        private static void LogActivity(string title, string detail, string glyph = "\uE774")
        {
            string text = (detail ?? "").Trim();
            RecentActivity.Add(new ActivityItem
            {
                Title = title,
                Detail = text.Length <= 200 ? text : text[..200] + "…",
                Timestamp = DateTime.Now,
                Glyph = glyph,
            });
        }

        public (string Baseline, string Candidate, string Results, string Stats) CaptureState()
            => (DiffLeftText.Text, DiffRightText.Text, DiffResultsText.Text, DiffStatsText.Text);

        public void RestoreState(string? baseline, string? candidate, string? results, string? stats)
        {
            DiffLeftText.Text = baseline ?? string.Empty;
            DiffRightText.Text = candidate ?? string.Empty;
            DiffResultsText.Text = results ?? string.Empty;
            DiffStatsText.Text = stats ?? string.Empty;
        }
    }
}
