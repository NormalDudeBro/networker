using System;
using System.Collections.Generic;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
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
                    Width = 360,
                    Margin = new Thickness(0, 0, 0, 8)
                };

                toast.CloseButtonClick += (s, e) => Remove(toast);
                _host.Children.Add(toast);

                var timer = _dispatcher.CreateTimer();
                timer.Interval = TimeSpan.FromSeconds(6);
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    Remove(toast);
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
    }
}
