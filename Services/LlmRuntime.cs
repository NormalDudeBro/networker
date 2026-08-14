using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Networker.Core.Llm;
using networker.Services.Codex;
using Windows.Storage;

namespace networker.Services
{
    /// <summary>
    /// App-side seam over the Llm module. Builds the router once from the
    /// environment (.env / env vars) plus UI overrides, and exposes the
    /// operations the chat and settings surfaces need.
    /// </summary>
    public static class LlmRuntime
    {
        private static LlmRouter? _router;
        private static CodexChatProvider? _codexProvider;

        public static event Action<LlmRouter>? RouterChanged;

        public static LlmRouter Router => _router ??= CreateRouter();

        public static LlmConfig Config => Router.Config;

        public static void ConfigureCodex(CodexChatProvider provider)
        {
            _codexProvider = provider ?? throw new ArgumentNullException(nameof(provider));
            Reset();
        }

        /// <summary>
        /// Rebuilds the router from current environment + app settings. Call when
        /// connection settings (endpoint, API key) change at runtime.
        /// </summary>
        public static void Reset()
        {
            _router = null;
            LlmRouter router = Router;
            ApplyProviderSelection(AppSettings.SelectedProvider, AppSettings.SelectedModel);
            RouterChanged?.Invoke(router);
        }

        public static void ApplyProviderSelection(string providerName, string? model)
        {
            var kind = LlmConfig.ParseProvider(providerName);
            Router.SetPrimary(kind);

            var provider = Router.Providers.FirstOrDefault(p => p.Kind == kind);
            if (provider is not null && !string.IsNullOrWhiteSpace(model))
            {
                provider.Model = model;
            }
        }

        public static async Task<IReadOnlyList<LlmModelInfo>> GetModelsAsync()
        {
            LlmProviderKind selected = LlmConfig.ParseProvider(AppSettings.SelectedProvider);
            ILlmProvider? provider = Router.Providers.FirstOrDefault(candidate => candidate.Kind == selected);
            return provider is null
                ? Array.Empty<LlmModelInfo>()
                : await provider.ListModelsAsync();
        }

        public static async Task<LlmProviderStatus> GetSelectedProviderHealthAsync(string providerName)
        {
            var kind = LlmConfig.ParseProvider(providerName);
            var statuses = await Router.HealthCheckAllAsync();
            return statuses.FirstOrDefault(s => s.Kind == kind)
                ?? new LlmProviderStatus
                {
                    Kind = kind,
                    Provider = providerName,
                    IsAvailable = false,
                    Message = "Provider not configured in the router chain.",
                };
        }

        private static LlmRouter CreateRouter()
        {
            var overrides = new LlmEnvOverrides
            {
                OllamaHost = AppSettings.OllamaEndpoint,
                OllamaModel = AppSettings.SelectedModel,
                OllamaApiKey = AppSettings.OllamaApiKey,
            };

            var config = LlmConfigLoader.Load(overrides, LocalDataDirectory());
            config.CodexModel = AppSettings.SelectedModel;
            var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(180) };
            var providers = new List<ILlmProvider>
            {
                new OllamaProvider(config, http),
                new GrokProvider(config, http),
                new GeminiProvider(config, http),
            };
            if (_codexProvider is not null)
            {
                providers.Add(_codexProvider);
            }

            var router = new LlmRouter(config, providers);
            router.SetPrimary(LlmConfig.ParseProvider(AppSettings.SelectedProvider));
            return router;
        }

        private static string LocalDataDirectory()
        {
            try
            {
                return ApplicationData.Current.LocalFolder.Path;
            }
            catch
            {
                return AppSettings.GetLocalDataDirectory();
            }
        }
    }
}
