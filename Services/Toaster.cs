using System;
using System.Collections.Generic;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace networker.Services
{
    /// <summary>
    /// Lightweight transient toast host. InfoBars are stacked in the top-right
    /// corner of the window and auto-dismiss after a few seconds.
    /// </summary>
    public static class Toaster
    {
        private static StackPanel? _host;
        private static DispatcherQueue? _dispatcher;
        private static readonly Dictionary<InfoBar, DispatcherQueueTimer> _timers = new();
        private static readonly Dictionary<string, InfoBar> _updateToasts = new();

        public static void Initialize(StackPanel host, DispatcherQueue dispatcher)
        {
            _host = host;
            _dispatcher = dispatcher;
        }

        public static void Show(string message, InfoBarSeverity severity = InfoBarSeverity.Informational, string? title = null)
        {
            if (_host is null || _dispatcher is null) return;

            _dispatcher.TryEnqueue(() =>
            {
                var toast = new InfoBar
                {
                    Title = title,
                    Message = message,
                    Severity = severity,
                    IsClosable = true,
                    IsOpen = true,
                    MinWidth = 280,
                    MaxWidth = Application.Current.Resources["ToastMaxWidth"] is double maxWidth ? maxWidth : 400,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Margin = new Thickness(0, 0, 0, 8),
                };
                AutomationProperties.SetName(toast, string.IsNullOrWhiteSpace(title) ? message : $"{title}. {message}");
                AutomationProperties.SetLiveSetting(toast, AutomationLiveSetting.Polite);

                toast.Closed += (s, e) => Remove(toast);
                _host.Children.Add(toast);

                var timer = _dispatcher.CreateTimer();
                timer.Interval = TimeSpan.FromSeconds(severity is InfoBarSeverity.Warning or InfoBarSeverity.Error ? 10 : 5);
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    toast.IsOpen = false;
                };
                timer.Start();
                _timers[toast] = timer;
            });
        }

        private static void Remove(InfoBar toast)
        {
            if (_timers.TryGetValue(toast, out var timer))
            {
                timer.Stop();
                _timers.Remove(toast);
            }
            if (_host != null && _host.Children.Contains(toast))
            {
                _host.Children.Remove(toast);
            }
        }

        /// <summary>
        /// Shows one non-expiring, closable update notification per release tag.
        /// The action button is the primary call to action; closing the toast is
        /// the non-intrusive Later action.
        /// </summary>
        public static void ShowUpdate(string tag, string message, string actionLabel, Action action)
        {
            if (_host is null || _dispatcher is null) return;

            _dispatcher.TryEnqueue(() =>
            {
                if (_updateToasts.ContainsKey(tag)) return;

                var toast = new InfoBar
                {
                    Title = "Update available",
                    Message = message,
                    Severity = InfoBarSeverity.Informational,
                    IsClosable = true,
                    IsOpen = true,
                    MinWidth = 280,
                    MaxWidth = Application.Current.Resources["ToastMaxWidth"] is double maxWidth ? maxWidth : 400,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Margin = new Thickness(0, 0, 0, 8),
                    ActionButton = new Button { Content = actionLabel },
                };
                AutomationProperties.SetName(toast, $"Update available. {message}");
                AutomationProperties.SetLiveSetting(toast, AutomationLiveSetting.Polite);

                if (toast.ActionButton is Button actionButton)
                {
                    actionButton.Click += (s, e) =>
                    {
                        try
                        {
                            action();
                        }
                        finally
                        {
                            toast.IsOpen = false;
                        }
                    };
                }

                toast.Closed += (s, e) => RemoveUpdateToast(toast);
                _updateToasts[tag] = toast;
                _host.Children.Add(toast);
            });
        }

        private static void RemoveUpdateToast(InfoBar toast)
        {
            string? tag = null;
            foreach (var pair in _updateToasts)
            {
                if (pair.Value == toast)
                {
                    tag = pair.Key;
                    break;
                }
            }

            if (tag is not null)
            {
                _updateToasts.Remove(tag);
            }

            if (_host != null && _host.Children.Contains(toast))
            {
                _host.Children.Remove(toast);
            }
        }
    }
}
