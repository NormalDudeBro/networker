using System;
using System.Linq;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
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
        public DiffTab()
        {
            this.InitializeComponent();
        }

        private void Compare_Click(object sender, RoutedEventArgs e)
        {
            var left = DiffLeftText.Text;
            var right = DiffRightText.Text;
            if (left.Length == 0 || right.Length == 0)
            {
                SetStatus("Please paste configurations in both panels", error: true);
                return;
            }

            var diff = TextDiff.DiffLines(left, right);
            var additions = diff.Count(line => line.Kind == DiffLineKind.Added);
            var deletions = diff.Count(line => line.Kind == DiffLineKind.Removed);

            if (additions == 0 && deletions == 0)
            {
                DiffResultsText.Text = "Configurations are identical - no differences found.";
                DiffStatsText.Text = "0 additions, 0 deletions";
                SetStatus("Configurations are identical");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("--- Configuration A");
            sb.AppendLine("+++ Configuration B");
            sb.Append(TextDiff.ToUnified(diff));

            DiffResultsText.Text = sb.ToString();
            DiffStatsText.Text = $"{additions} additions, {deletions} deletions";
            SetStatus($"Diff complete: {additions} additions, {deletions} deletions");
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
            StatusText.Foreground = error
                ? (Brush)Application.Current.Resources["AppDangerBrush"]
                : (Brush)Application.Current.Resources["AppTextSecondaryBrush"];
        }
    }
}
