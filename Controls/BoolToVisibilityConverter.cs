using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace networker.Controls
{
    /// <summary>
    /// Converts a bool to <see cref="Visibility"/> (optionally inverted).
    /// </summary>
    public sealed class BoolToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; }

        public object Convert(object value, System.Type targetType, object parameter, string language)
        {
            bool flag = value is bool b && b;
            if (Invert) flag = !flag;
            return flag ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, System.Type targetType, object parameter, string language)
        {
            return value is Visibility v && v == Visibility.Visible;
        }
    }
}
