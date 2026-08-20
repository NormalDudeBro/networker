using System;
using System.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using networker.Models;
using Windows.UI;

namespace networker.Controls
{
    /// <summary>
    /// Renders a unified diff with color temperature: added lines on translucent
    /// green, removed lines on translucent red, file/hunk headers on translucent
    /// blue, context plain. Rows are TextBlocks so the tints layer over either
    /// theme the page runs in.
    /// </summary>
    public sealed partial class DiffBlockView : UserControl
    {
        private EditBlock? _source;

        public DiffBlockView()
        {
            this.InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
        {
            if (_source != null)
            {
                _source.PropertyChanged -= OnSourcePropertyChanged;
            }

            _source = args.NewValue as EditBlock;

            if (_source != null)
            {
                _source.PropertyChanged += OnSourcePropertyChanged;
                Render(_source.Diff);
            }
            else
            {
                DiffBody.Children.Clear();
            }
        }

        private void OnSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is EditBlock block && e.PropertyName == nameof(EditBlock.Diff))
            {
                Render(block.Diff);
            }
        }

        private void Render(string diff)
        {
            DiffBody.Children.Clear();
            if (string.IsNullOrEmpty(diff))
            {
                return;
            }

            var addedBrush = (Brush)Application.Current.Resources["DiffAddedBackgroundBrush"];
            var removedBrush = (Brush)Application.Current.Resources["DiffRemovedBackgroundBrush"];
            var headerBrush = (Brush)Application.Current.Resources["DiffHeaderBackgroundBrush"];
            var addedText = (Brush)Application.Current.Resources["AppSuccessBrush"];
            var removedText = (Brush)Application.Current.Resources["AppDangerBrush"];
            var caption = (Brush)Application.Current.Resources["AppTextSecondaryBrush"];
            var plain = (Brush)Application.Current.Resources["AppTextPrimaryBrush"];

            foreach (string rawLine in diff.Split('\n'))
            {
                string line = rawLine.TrimEnd('\r');
                var textBlock = new TextBlock
                {
                    FontFamily = (FontFamily)Application.Current.Resources["CodeFontFamily"],
                    FontSize = 12.5,
                    TextWrapping = TextWrapping.NoWrap,
                    IsTextSelectionEnabled = true,
                    Padding = new Thickness(0, 1, 12, 1),
                };

                if (line.Length == 0)
                {
                    DiffBody.Children.Add(textBlock);
                    continue;
                }

                if (IsHunkOrFileHeader(line))
                {
                    textBlock.Foreground = caption;
                    textBlock.Text = line;
                    DiffBody.Children.Add(new Border { Background = headerBrush, Child = textBlock });
                }
                else if (line[0] == '+')
                {
                    textBlock.Foreground = addedText;
                    textBlock.Text = line.Substring(1);
                    DiffBody.Children.Add(new Border { Background = addedBrush, Child = textBlock });
                }
                else if (line[0] == '-')
                {
                    textBlock.Foreground = removedText;
                    textBlock.Text = line.Substring(1);
                    DiffBody.Children.Add(new Border { Background = removedBrush, Child = textBlock });
                }
                else
                {
                    textBlock.Text = line;
                    textBlock.Foreground = plain;
                    DiffBody.Children.Add(textBlock);
                }
            }
        }

        private static bool IsHunkOrFileHeader(string line)
            => line.StartsWith("@@", StringComparison.Ordinal)
            || line.StartsWith("+++ ", StringComparison.Ordinal)
            || line.StartsWith("--- ", StringComparison.Ordinal)
            || line.StartsWith("diff --git ", StringComparison.Ordinal)
            || line.StartsWith("index ", StringComparison.Ordinal)
            || line.StartsWith("new file ", StringComparison.Ordinal)
            || line.StartsWith("deleted file ", StringComparison.Ordinal);
    }
}
