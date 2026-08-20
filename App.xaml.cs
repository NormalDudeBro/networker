using System;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using networker.Services;
using networker.Services.Codex;
using Networker.Core.Codex;
using Networker.Core.Services.NetworkConfig;
using Networker.Core.Services.NetworkConfig.Parsers;
using Networker.Core.Workflow;

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
            services.AddSingleton<ICodexAppServerClient, CodexAppServerClient>();
            services.AddSingleton<CodexAccountService>();
            services.AddSingleton<CodexChatProvider>();
            services.AddSingleton<CodexAgentService>();
            services.AddSingleton<AgentService>(sp => new AgentService(sp.GetRequiredService<CodexAgentService>()));
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
            services.AddSingleton<Networker.Core.Terminal.TerminalSession>();

            services.AddSingleton<LaunchHealthService>();
            return services.BuildServiceProvider();
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            _ = Services.GetRequiredService<LaunchHealthService>();
            m_window = new MainWindow();
            m_window.Activate();
        }

        private Window? m_window;
    }
}
