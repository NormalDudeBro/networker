using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Networker.Core.Llm;

namespace networker.Services
{
    /// <summary>
    /// Single source of truth for the AI session: the selected provider/model
    /// and the live connection state. The status bar, dashboard, and assistant
    /// page all observe this one instance instead of each owning its own health
    /// check logic. Raise <see cref="Changed"/> on the UI thread; consumers that
    /// render UI should marshal with their DispatcherQueue.
    /// </summary>
    public static class LlmSession
    {
        private static readonly IReadOnlyList<string> EmptyModels = Array.Empty<string>();
        private static LlmRouter? _observedRouter;

        public static string Provider { get; private set; } = AppSettings.SelectedProvider;
        public static string Model { get; private set; } = AppSettings.SelectedModel;
        public static string StatusMessage { get; private set; } = "Not checked";
        public static bool IsConnected { get; private set; }
        public static bool IsChecking { get; private set; }
        public static bool HasModels { get; private set; }
        public static IReadOnlyList<string> Models { get; private set; } = EmptyModels;

        /// <summary>Raised whenever any session state changes.</summary>
        public static event Action? Changed;

        /// <summary>
        /// Wires live router events (request failures, fallbacks) into the
        /// status line so the UI reflects what is actually happening without
        /// forcing an extra health round-trip.
        /// </summary>
        public static void Initialize()
        {
            Observe(LlmRuntime.Router);
            LlmRuntime.RouterChanged -= Observe;
            LlmRuntime.RouterChanged += Observe;
        }

        private static void Observe(LlmRouter router)
        {
            if (ReferenceEquals(_observedRouter, router)) return;
            if (_observedRouter is not null) _observedRouter.StatusChanged -= Router_StatusChanged;
            _observedRouter = router;
            router.StatusChanged += Router_StatusChanged;
        }

        private static void Router_StatusChanged(object? sender, LlmRouterStatusChangedEventArgs e)
        {
            if (e.IsError) StatusMessage = $"Error: {e.Message}";
            Changed?.Invoke();
        }

        public static void SetProvider(string provider)
        {
            if (string.IsNullOrWhiteSpace(provider)) return;
            Provider = provider;
            AppSettings.SelectedProvider = provider;
            Changed?.Invoke();
        }

        public static void SetModel(string model)
        {
            Model = model;
            AppSettings.SelectedModel = model;
            Changed?.Invoke();
        }

        public static async Task RefreshAsync()
        {
            if (IsChecking) return;

            IsChecking = true;
            StatusMessage = "Checking…";
            Changed?.Invoke();

            try
            {
                bool connected = false;
                try
                {
                    var status = await LlmRuntime.GetSelectedProviderHealthAsync(Provider).ConfigureAwait(true);
                    connected = status.IsAvailable;
                    if (!connected)
                    {
                        IsConnected = false;
                        StatusMessage = status.Message ?? "Provider unavailable";
                    }
                }
                catch (Exception ex)
                {
                    IsConnected = false;
                    StatusMessage = ex.Message;
                }

                bool hasModels = await LoadModelsAsync().ConfigureAwait(true);
                HasModels = hasModels;

                if (connected)
                {
                    IsConnected = true;
                    StatusMessage = hasModels ? "Connected" : "Connected — no models";
                }
            }
            finally
            {
                IsChecking = false;
                Changed?.Invoke();
            }
        }

        private static async Task<bool> LoadModelsAsync()
        {
            try
            {
                var models = await LlmRuntime.GetModelsAsync().ConfigureAwait(true);
                if (models.Count == 0)
                {
                    Models = EmptyModels;
                    HasModels = false;
                    AppSettings.SelectedModel = "";
                    Model = "";
                    return false;
                }

                var ids = models.Select(m => m.Id).ToList();
                Models = ids;
                HasModels = true;

                string previous = AppSettings.SelectedModel;
                string selected = !string.IsNullOrEmpty(previous) && ids.Contains(previous)
                    ? previous
                    : ids[0];
                AppSettings.SelectedModel = selected;
                Model = selected;
                return true;
            }
            catch
            {
                Models = EmptyModels;
                HasModels = false;
                AppSettings.SelectedModel = "";
                Model = "";
                return false;
            }
        }
    }
}
