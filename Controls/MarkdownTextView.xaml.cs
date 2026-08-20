using System;
using System.Collections.Generic;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace networker.Controls
{
    /// <summary>
    /// Bounded markdown renderer for assistant turn text: headings, bold/italic,
    /// inline code, fenced code blocks, bullet/numbered lists, blockquotes, and
    /// links (accent-colored text — navigation is left to the user). Renders into
    /// a block stack with a 50 ms debounce so streaming text repaints smoothly.
    /// </summary>
    public sealed partial class MarkdownTextView : UserControl
    {
        private readonly DispatcherQueueTimer _renderTimer;
        private string _pending = string.Empty;

        public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
            nameof(Text),
            typeof(string),
            typeof(MarkdownTextView),
            new PropertyMetadata(string.Empty, (dependencyObject, args) =>
            {
                if (dependencyObject is MarkdownTextView view)
                {
                    view.QueueRender(args.NewValue as string ?? string.Empty);
                }
            }));

        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        public MarkdownTextView()
        {
            this.InitializeComponent();
            _renderTimer = DispatcherQueue.CreateTimer();
            _renderTimer.Interval = TimeSpan.FromMilliseconds(50);
            _renderTimer.IsRepeating = false;
            _renderTimer.Tick += (_, _) =>
            {
                if (_pending.Length > 0)
                {
                    Render(_pending);
                }
            };
        }

        private void QueueRender(string text)
        {
            _pending = text;
            _renderTimer.Stop();
            _renderTimer.Start();
        }

        private void Render(string markdown)
        {
            Body.Children.Clear();
            if (string.IsNullOrWhiteSpace(markdown))
            {
                return;
            }

            var foreground = (Brush)Application.Current.Resources["AppTextPrimaryBrush"];
            var secondary = (Brush)Application.Current.Resources["AppTextSecondaryBrush"];
            var accent = (Brush)Application.Current.Resources["AppAccentBrush"];
            var codeFont = (FontFamily)Application.Current.Resources["CodeFontFamily"];
            var inset = (Brush)Application.Current.Resources["AppInsetBrush"];

            string[] lines = markdown.Replace("\r\n", "\n").Split('\n');
            bool inCode = false;
            var codeLines = new List<string>();

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];

                if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
                {
                    if (!inCode)
                    {
                        inCode = true;
                        codeLines.Clear();
                    }
                    else
                    {
                        inCode = false;
                        AppendCodeBlock(codeLines, foreground, codeFont, inset);
                    }
                    continue;
                }

                if (inCode)
                {
                    codeLines.Add(line);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                string trimmed = line.Trim();
                if (TryAppendHeading(trimmed, accent, foreground, codeFont))
                {
                    continue;
                }
                if (TryAppendList(trimmed, accent, foreground, codeFont))
                {
                    continue;
                }
                if (TryAppendBlockquote(trimmed, secondary, foreground, codeFont))
                {
                    continue;
                }
                if (IsHorizontalRule(trimmed))
                {
                    Body.Children.Add(new Border
                    {
                        Height = 1,
                        Margin = new Thickness(0, 4, 0, 4),
                        Background = (Brush)Application.Current.Resources["AppBorderBrush"],
                    });
                    continue;
                }

                Body.Children.Add(BuildParagraph(line, accent, secondary, foreground, codeFont));
            }

            if (inCode)
            {
                AppendCodeBlock(codeLines, foreground, codeFont, inset);
            }
        }

        private void AppendCodeBlock(List<string> lines, Brush foreground, FontFamily codeFont, Brush inset)
        {
            Body.Children.Add(new Border
            {
                Background = inset,
                BorderBrush = (Brush)Application.Current.Resources["AppBorderBrush"],
                BorderThickness = new Thickness(1, 1, 1, 1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 6, 10, 6),
                Child = new TextBlock
                {
                    Text = string.Join("\n", lines),
                    FontFamily = codeFont,
                    FontSize = 12.5,
                    Foreground = foreground,
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true,
                },
            });
        }

        private bool TryAppendHeading(string trimmed, Brush accent, Brush foreground, FontFamily codeFont)
        {
            int level = 0;
            while (level < trimmed.Length && level < 4 && trimmed[level] == '#')
            {
                level++;
            }
            if (level == 0 || level > trimmed.Length || (level < trimmed.Length && trimmed[level] != ' '))
            {
                return false;
            }

            string text = trimmed.Substring(level).TrimStart();
            FrameworkElement block = BuildParagraph(text, accent, (Brush)Application.Current.Resources["AppTextSecondaryBrush"], foreground, codeFont);
            if (block is RichTextBlock rich && rich.Blocks[0] is Paragraph paragraph)
            {
                foreach (Inline inline in paragraph.Inlines)
                {
                    inline.FontSize = HeadingSize(level);
                    inline.FontWeight = FontWeights.SemiBold;
                }
                if (paragraph.Inlines.Count > 0 && paragraph.Inlines[0] is Run firstRun)
                {
                    firstRun.Foreground = level == 1 ? accent : foreground;
                }
            }
            else if (block is TextBlock textBlock)
            {
                textBlock.FontSize = HeadingSize(level);
                textBlock.FontWeight = FontWeights.SemiBold;
                textBlock.Foreground = level == 1 ? accent : foreground;
            }
            Body.Children.Add(block);
            return true;
        }

        private static double HeadingSize(int level) => level switch { 1 => 20, 2 => 17, 3 => 15, _ => 13.5 };

        private bool TryAppendList(string trimmed, Brush accent, Brush foreground, FontFamily codeFont)
        {
            bool ordered = trimmed.Length > 1 && char.IsDigit(trimmed[0]) && trimmed[1] == '.';
            if (!ordered && trimmed[0] is not ('-' or '*' or '+'))
            {
                return false;
            }

            string marker;
            string content;
            if (ordered)
            {
                int end = trimmed.IndexOf(". ", StringComparison.Ordinal);
                if (end < 0)
                {
                    return false;
                }
                marker = trimmed.Substring(0, end + 1);
                content = trimmed.Substring(end + 1).TrimStart();
            }
            else
            {
                marker = trimmed[0].ToString();
                content = trimmed.Length > 1 ? trimmed.Substring(1).TrimStart() : string.Empty;
            }

            var paragraph = new Paragraph();
            paragraph.Inlines.Add(new Run { Text = ordered ? marker + " " : marker + "  ", Foreground = accent, FontWeight = FontWeights.SemiBold });
            ParseInlines(content, paragraph, accent, (Brush)Application.Current.Resources["AppTextSecondaryBrush"], foreground, codeFont);
            Body.Children.Add(WrapRich(paragraph, foreground));
            return true;
        }

        private bool TryAppendBlockquote(string trimmed, Brush secondary, Brush foreground, FontFamily codeFont)
        {
            if (trimmed[0] != '>')
            {
                return false;
            }
            string content = trimmed.Length > 1 ? trimmed.Substring(1).TrimStart() : string.Empty;
            var paragraph = new Paragraph();
            paragraph.Inlines.Add(new Run { Text = content, Foreground = secondary, FontStyle = Windows.UI.Text.FontStyle.Italic });
            Body.Children.Add(WrapRich(paragraph, foreground));
            return true;
        }

        private static bool IsHorizontalRule(string trimmed)
        {
            if (trimmed.Length < 3)
            {
                return false;
            }
            char first = trimmed[0];
            if (first is not ('-' or '*' or '_'))
            {
                return false;
            }
            foreach (char character in trimmed)
            {
                if (character != first)
                {
                    return false;
                }
            }
            return true;
        }

        private FrameworkElement BuildParagraph(string text, Brush accent, Brush secondary, Brush foreground, FontFamily codeFont)
        {
            if (text.IndexOfAny(new[] { '*', '`', '[' }) < 0)
            {
                return new TextBlock
                {
                    Text = text,
                    Foreground = foreground,
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true,
                    FontSize = 14,
                };
            }
            var paragraph = new Paragraph();
            ParseInlines(text, paragraph, accent, secondary, foreground, codeFont);
            return WrapRich(paragraph, foreground);
        }

        private static RichTextBlock WrapRich(Paragraph paragraph, Brush foreground)
        {
            var rich = new RichTextBlock
            {
                IsTextSelectionEnabled = true,
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Foreground = foreground,
            };
            rich.Blocks.Add(paragraph);
            return rich;
        }

        /// <summary>
        /// Splits <paramref name="text"/> on **bold**, *italic*, `inline code`,
        /// and [label](url) links, appending styled runs into <paramref name="paragraph"/>.
        /// </summary>
        private static void ParseInlines(string text, Paragraph paragraph, Brush accent, Brush secondary, Brush foreground, FontFamily codeFont)
        {
            int index = 0;
            while (index < text.Length)
            {
                int bold = text.IndexOf("**", index, StringComparison.Ordinal);
                int italic = text.IndexOf('*', index);
                int code = text.IndexOf('`', index);
                int link = text.IndexOf('[', index);

                int next = MinNonNegative(bold, italic, code, link);
                if (next < 0)
                {
                    AddPlain(text.Substring(index), paragraph, foreground);
                    return;
                }

                if (next > index)
                {
                    AddPlain(text.Substring(index, next - index), paragraph, foreground);
                }

                if (next == link)
                {
                    int close = text.IndexOf("](", next + 1, StringComparison.Ordinal);
                    int end = close >= 0 ? text.IndexOf(')', close + 2) : -1;
                    if (close < 0 || end < 0)
                    {
                        AddPlain(text.Substring(next), paragraph, foreground);
                        return;
                    }
                    string label = text.Substring(next + 1, close - next - 1);
                    paragraph.Inlines.Add(new Run { Text = label, Foreground = accent, FontWeight = FontWeights.SemiBold });
                    paragraph.Inlines.Add(new Run { Text = " ↗", Foreground = secondary, FontSize = 10 });
                    index = end + 1;
                    continue;
                }

                if (next == bold)
                {
                    int end = text.IndexOf("**", next + 2, StringComparison.Ordinal);
                    if (end < 0)
                    {
                        AddPlain(text.Substring(next), paragraph, foreground);
                        return;
                    }
                    paragraph.Inlines.Add(new Run { Text = text.Substring(next + 2, end - next - 2), Foreground = foreground, FontWeight = FontWeights.SemiBold });
                    index = end + 2;
                    continue;
                }

                if (next == code)
                {
                    int end = text.IndexOf('`', next + 1);
                    if (end < 0)
                    {
                        AddPlain(text.Substring(next), paragraph, foreground);
                        return;
                    }
                    paragraph.Inlines.Add(new Run
                    {
                        Text = text.Substring(next + 1, end - next - 1),
                        Foreground = accent,
                        FontFamily = codeFont,
                        FontSize = 12,
                    });
                    index = end + 1;
                    continue;
                }

                // Italic
                int endItalic = text.IndexOf('*', next + 1);
                if (endItalic < 0)
                {
                    AddPlain(text.Substring(next), paragraph, foreground);
                    return;
                }
                paragraph.Inlines.Add(new Run { Text = text.Substring(next + 1, endItalic - next - 1), Foreground = foreground, FontStyle = Windows.UI.Text.FontStyle.Italic });
                index = endItalic + 1;
            }
        }

        private static void AddPlain(string text, Paragraph paragraph, Brush foreground)
        {
            if (text.Length > 0)
            {
                paragraph.Inlines.Add(new Run { Text = text, Foreground = foreground });
            }
        }

        private static int MinNonNegative(params int[] values)
        {
            int best = -1;
            foreach (int value in values)
            {
                if (value >= 0 && (best < 0 || value < best))
                {
                    best = value;
                }
            }
            return best;
        }
    }
}
