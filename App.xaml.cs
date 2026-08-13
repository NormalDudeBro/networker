using System;
using System.IO;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using networker.Services;
using networker.Services.Updates;
using Networker.Core.Services.NetworkConfig;
using Networker.Core.Services.NetworkConfig.Parsers;
using Networker.Core.Updates;
using Networker.Core.Workflow;
using Windows.ApplicationModel.Activation;

namespace networker
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        /// <summary>
        /// Initializes the singleton application object. This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            UnhandledException += (_, args) => LogStartupException(args.Exception);

            // WinUI requires the theme before InitializeComponent.
            switch (AppSettings.ThemeMode)
            {
                case "Light":
                    RequestedTheme = ApplicationTheme.Light;
                    break;
                case "Dark":
                    RequestedTheme = ApplicationTheme.Dark;
                    break;
            }

            this.InitializeComponent();
            Services = BuildServiceProvider();
        }

        private static void LogStartupException(Exception exception)
        {
            try
            {
                string directory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Networker");
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "startup-error.log"), exception.ToString());
            }
            catch
            {
            }
        }

        /// <summary>
        /// Root dependency-injection container. Providers and services are resolved
        /// through <see cref="App.Services"/> from pages and controls.
        /// </summary>
        public IServiceProvider Services { get; }

        private static ServiceProvider BuildServiceProvider()
        {
            var services = new ServiceCollection();
            services.AddSingleton(LlmRuntime.Router);
            services.AddSingleton<IConfigGenerator, NetworkConfigGenerator>();
            services.AddSingleton<IConfigValidator, ConfigValidator>();
            services.AddSingleton<IConfigParserFactory, ConfigParserFactory>();
            services.AddSingleton<IVaultService>(_ => new VaultService(
                Path.Combine(AppSettings.NetworkConfigDirectory, "vault.dat")));
            services.AddSingleton<ITemplateLibrary>(_ => new TemplateLibrary(
                Path.Combine(AppSettings.NetworkConfigDirectory, "custom_templates.json")));
            services.AddSingleton(_ => new TroubleshootingWorkspaceStore(
                Path.Combine(AppSettings.GetLocalDataDirectory(), "troubleshooting-workspace.json")));
            services.AddSingleton<TroubleshootingSession>();

            // Update services: a 15-second metadata client for release checks and a
            // streaming download client with no total-body timeout. Both are
            // DI-managed so sockets are reused and DNS/lifetime are handled.
            services.AddHttpClient("UpdateMetadata", client =>
            {
                client.BaseAddress = new Uri(NetworkerVersionPolicy.ApiBase);
                client.Timeout = TimeSpan.FromSeconds(15);
            });
            services.AddHttpClient("UpdateDownload", client =>
            {
                client.BaseAddress = new Uri(NetworkerVersionPolicy.ApiBase);
                client.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
            });

            services.AddSingleton<IUpdateClock, SystemUpdateClock>();
            services.AddSingleton<IUpdateLog, UpdateFileLogger>();
            services.AddSingleton<IInstalledVersionProvider, InstalledVersionProvider>();
            services.AddSingleton<IGitHubReleaseClient>(sp => new GitHubReleaseClient(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("UpdateMetadata"),
                sp.GetRequiredService<IInstalledVersionProvider>(),
                sp.GetRequiredService<IUpdateLog>()));
            services.AddSingleton<IUpdatePackageDownloader>(sp => new UpdatePackageDownloader(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("UpdateDownload"),
                sp.GetRequiredService<IUpdateLog>()));
            services.AddSingleton<IUpdatePackageVerifier, UpdatePackageVerifier>();
            services.AddSingleton<IUpdateInstaller, MsixUpdateInstaller>();
            services.AddSingleton<IUpdateCacheStore, UpdateCacheStore>();
            services.AddSingleton<IUpdatePackageStorage, UpdatePackageStorage>();
            services.AddSingleton<UpdateCoordinator>();
            services.AddSingleton<UpdateScheduler>();
            services.AddSingleton<AppRestartService>();
            return services.BuildServiceProvider();
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            m_window = new MainWindow();
            m_window.Activate();

            // Update plumbing must never delay or fail startup. Clean confirmed
            // staged packages, then start the scheduler without awaiting.
            try
            {
                UpdateCoordinator coordinator = Services.GetRequiredService<UpdateCoordinator>();
                coordinator.CleanupConfirmedStaged();
                Services.GetRequiredService<UpdateScheduler>().Start();
            }
            catch (Exception ex)
            {
                Services.GetRequiredService<IUpdateLog>().Error("Update startup failed.", ex);
            }
        }

        private Window? m_window;
    }
}
