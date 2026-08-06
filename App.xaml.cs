using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using networker.Services;
using Networker.Core.Services.NetworkConfig;
using Windows.ApplicationModel.Activation;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

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
        // Apply theme BEFORE InitializeComponent() - WinUI requires this
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
        }

        private Window? m_window;
    }
}
