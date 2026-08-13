using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Dispatching;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
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
        private readonly Dictionary<HighlightType, SolidColorBrush> _brushes = new();
        private readonly DispatcherQueueTimer _renderTimer;
        private readonly DispatcherQueueTimer _copyTimer;

        public CodeBlockView()
        {
            this.InitializeComponent();
            _renderTimer = DispatcherQueue.CreateTimer();
            _renderTimer.Interval = TimeSpan.FromMilliseconds(50);
            _renderTimer.IsRepeating = false;
            _renderTimer.Tick += (_, _) =>
            {
                if (_source is not null) Render(_source);
            };

            _copyTimer = DispatcherQueue.CreateTimer();
            _copyTimer.Interval = TimeSpan.FromMilliseconds(1500);
            _copyTimer.IsRepeating = false;
            _copyTimer.Tick += (_, _) => ResetCopyFeedback();

            DataContextChanged += OnDataContextChanged;
            ActualThemeChanged += (_, _) =>
            {
                _brushes.Clear();
                if (_source is not null) Render(_source);
            };
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
            else
            {
                TitleText.Text = "Configuration";
                BadgeHost.Visibility = Visibility.Collapsed;
                BodyText.Blocks.Clear();
            }
        }

        private void OnSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not ChatMessage message)
            {
                return;
            }

            if (e.PropertyName == nameof(ChatMessage.Text))
            {
                if (message.IsStreaming)
                {
                    if (!_renderTimer.IsRunning) _renderTimer.Start();
                }
                else
                {
                    Render(message);
                }
            }
            else if (e.PropertyName == nameof(ChatMessage.IsStreaming) && !message.IsStreaming)
            {
                _renderTimer.Stop();
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
                        Foreground = BrushFor(token.Type)
                    });
                }

                BodyText.Blocks.Add(paragraph);
            }
        }

        private SolidColorBrush BrushFor(HighlightType type)
        {
            if (_brushes.TryGetValue(type, out var brush))
            {
                return brush;
            }

            var color = type switch
            {
                HighlightType.Comment => ColorFromResource("AppCommentColor"),
                HighlightType.Keyword => ColorFromResource("AppKeywordColor"),
                HighlightType.Ip => ColorFromResource("AppIpColor"),
                HighlightType.Number => ColorFromResource("AppNumberColor"),
                _ => ColorFromResource("AppTextPrimaryColor")
            };

            brush = new SolidColorBrush(color);
            _brushes[type] = brush;
            return brush;
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
            CopyGlyph.Glyph = "\uE73E";
            CopyStatus.Visibility = Visibility.Visible;
            AutomationProperties.SetName(CopyButton, "Copied");
            ToolTipService.SetToolTip(CopyButton, "Copied");
            _copyTimer.Stop();
            _copyTimer.Start();
        }

        private void ResetCopyFeedback()
        {
            CopyGlyph.Glyph = "\uE8C8";
            CopyStatus.Visibility = Visibility.Collapsed;
            AutomationProperties.SetName(CopyButton, "Copy to clipboard");
            ToolTipService.SetToolTip(CopyButton, "Copy to clipboard");
        }
    }
}
