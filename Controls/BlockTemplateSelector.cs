using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using networker.Models;

namespace networker.Controls
{
    /// <summary>
    /// Picks a data template for an <see cref="ActivityBlock"/> based on its kind —
    /// the Networker analog of the reference part→component mapping.
    /// </summary>
    public sealed class BlockTemplateSelector : DataTemplateSelector
    {
        public DataTemplate? ThinkingTemplate { get; set; }

        public DataTemplate? ToolTemplate { get; set; }

        public DataTemplate? EditTemplate { get; set; }

        public DataTemplate? ActivityTemplate { get; set; }

        public DataTemplate? ErrorTemplate { get; set; }

        protected override DataTemplate SelectTemplateCore(object item) => SelectTemplateCore(item, null);

        protected override DataTemplate SelectTemplateCore(object item, DependencyObject? container)
        {
            if (item is ActivityBlock block)
            {
                return block.Kind switch
                {
                    ActivityBlockKind.Thinking => ThinkingTemplate ?? ToolTemplate!,
                    ActivityBlockKind.Tool => ToolTemplate ?? ErrorTemplate!,
                    ActivityBlockKind.Edit => EditTemplate ?? ToolTemplate!,
                    ActivityBlockKind.Activity => ActivityTemplate ?? ToolTemplate!,
                    ActivityBlockKind.Error => ErrorTemplate ?? ToolTemplate!,
                    _ => ToolTemplate ?? ErrorTemplate!,
                };
            }
            return ToolTemplate ?? ErrorTemplate!;
        }
    }

    /// <summary>
    /// Maps a verdict word (done / stopped / failed / exit N) to the matching
    /// semantic brush so tool rows read as color temperature, not decoration.
    /// </summary>
    public sealed class VerdictBrushConverter : IValueConverter
    {
        public object Convert(object value, System.Type targetType, object parameter, string language)
        {
            string? verdict = value as string;
            var resources = Application.Current.Resources;
            if (string.IsNullOrEmpty(verdict)) return resources["AppTextSecondaryBrush"];
            if (verdict == "done") return resources["AppSuccessBrush"];
            if (verdict == "stopped") return resources["AppWarningBrush"];
            if (verdict == "failed" || verdict.StartsWith("exit ", System.StringComparison.Ordinal)) return resources["AppDangerBrush"];
            return resources["AppTextSecondaryBrush"];
        }

        public object ConvertBack(object value, System.Type targetType, object parameter, string language)
            => throw new System.NotImplementedException();
    }
}
