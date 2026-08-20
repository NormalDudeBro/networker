using System;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using networker.Models;
using Networker.Core.Text;
using Windows.ApplicationModel.DataTransfer;

namespace networker.Controls
{
    /// <summary>
    /// The "$ cmd" terminal block: command line in a prompt header, live output
    /// below on a dark terminal surface. Output is ANSI-stripped for display and
    /// re-rendered incrementally while the command streams. The rendered window
    /// is capped to the last <see cref="DisplayedMaxLines"/> lines so huge dumps
    /// stay cheap to lay out; the full text stays on the block and is available
    /// through the copy button.
    /// </summary>
    public sealed partial class TerminalOutputView : UserControl
    {
        private const int DisplayedMaxLines = 500;

        private ToolBlock? _source;

        public TerminalOutputView()
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

            _source = args.NewValue as ToolBlock;

            if (_source != null)
            {
                _source.PropertyChanged += OnSourcePropertyChanged;
                Render(_source);
            }
            else
            {
                OutputText.Text = string.Empty;
                DisplayCapCaption.Visibility = Visibility.Collapsed;
            }
        }

        private void OnSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not ToolBlock block)
            {
                return;
            }

            switch (e.PropertyName)
            {
                case nameof(ToolBlock.Output):
                    Render(block);
                    break;
                case nameof(ToolBlock.State):
                    // Force refresh of verdict/duration while also re-rendering output.
                    Render(block);
                    break;
            }
        }

        private void Render(ToolBlock block)
        {
            string stripped = AnsiStripper.Strip(block.Output);
            if (LineCount(stripped) > DisplayedMaxLines)
            {
                int total = LineCount(stripped);
                OutputText.Text = CapForDisplay(stripped);
                DisplayCapCaption.Text = $"… showing last {DisplayedMaxLines} of {total} lines — copy for the full output";
                DisplayCapCaption.Visibility = Visibility.Visible;
            }
            else
            {
                OutputText.Text = stripped;
                DisplayCapCaption.Visibility = Visibility.Collapsed;
            }
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            if (_source is null || string.IsNullOrEmpty(_source.Output)) return;
            var data = new DataPackage();
            data.SetText(AnsiStripper.Strip(_source.Output));
            Clipboard.SetContent(data);
            Clipboard.Flush();
        }

        private static int LineCount(string text)
        {
            if (text.Length == 0) return 0;
            int count = 1;
            foreach (char c in text)
            {
                if (c == '\n') count++;
            }
            return count;
        }

        private static string CapForDisplay(string text)
        {
            // Walk the last DisplayedMaxLines newlines from the end and cut below
            // the 500th; returns the original when the text is within the cap.
            int newlines = 0;
            int index = text.Length;
            while (newlines < DisplayedMaxLines)
            {
                int found = text.LastIndexOf('\n', index - 1);
                if (found < 0) return text;
                newlines++;
                index = found;
            }
            return text.Substring(index + 1);
        }
    }
}
