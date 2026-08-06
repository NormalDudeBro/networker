using System;
using System.ComponentModel;
using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using networker.Models;
using networker.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI;

namespace networker.Controls
{
    public sealed partial class CodeBlockView : UserControl
    {
        private ChatMessage? _source;

        public CodeBlockView()
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

            _source = args.NewValue as ChatMessage;

            if (_source != null)
            {
                _source.PropertyChanged += OnSourcePropertyChanged;
                Render(_source);
            }
        }

        private void OnSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ChatMessage.Text) && sender is ChatMessage message)
            {
                Render(message);
            }
        }

        private void Render(ChatMessage message)
        {
            TitleText.Text = message.CodeTitle ?? "Configuration";

            if (!string.IsNullOrEmpty(message.ValidationBadge))
            {
                BadgeText.Text = message.ValidationBadge;
                BadgeHost.Style = ResolveBadgeStyle(message.ValidationSeverity);
                BadgeHost.Visibility = Visibility.Visible;
            }
            else
            {
                BadgeHost.Visibility = Visibility.Collapsed;
            }

            BodyText.Blocks.Clear();
            string text = message.Text ?? "";
            var lines = text.Split('\n');

            foreach (string line in lines)
            {
                var paragraph = new Microsoft.UI.Xaml.Documents.Paragraph
                {
                    Margin = new Thickness(0)
                };

                foreach (HighlightToken token in ConfigSyntaxHighlighter.Tokenize(line))
                {
                    paragraph.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
                    {
                        Text = token.Text,
                        Foreground = new SolidColorBrush(BrushFor(token.Type))
                    });
                }

                BodyText.Blocks.Add(paragraph);
            }
        }

        private static Color BrushFor(HighlightType type)
        {
            return type switch
            {
                HighlightType.Comment => ColorFromResource("AppCommentColor"),
                HighlightType.Keyword => ColorFromResource("AppKeywordColor"),
                HighlightType.Ip => ColorFromResource("AppIpColor"),
                HighlightType.Number => ColorFromResource("AppNumberColor"),
                _ => ColorFromResource("AppTextPrimaryColor")
            };
        }

        private static Color ColorFromResource(string key)
        {
            return Application.Current.Resources.TryGetValue(key, out object value) && value is Color color
                ? color
                : Colors.Silver;
        }

        private static Style ResolveBadgeStyle(string severity)
        {
            string key = severity switch
            {
                "success" => "BadgeSuccessStyle",
                "warning" => "BadgeWarningStyle",
                "danger" => "BadgeDangerStyle",
                _ => "BadgeInfoStyle"
            };
            return (Style)Application.Current.Resources[key];
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            if (_source is null) return;

            var package = new DataPackage();
            package.SetText(_source.Text ?? "");
            Clipboard.SetContent(package);
        }
    }
}
