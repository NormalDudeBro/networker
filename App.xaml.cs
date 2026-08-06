using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
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
            this.InitializeComponent();
            Services = BuildServiceProvider();
        }

        /// <summary>
        /// Root dependency-injection container. Providers and services are resolved
        /// through <see cref="App.Services"/> from pages and controls.
        /// </summary>
        public IServiceProvider Services { get; }

        /// <summary>
        /// Applies the theme application-wide so that shared brushes and dialogs
        /// follow the requested mode. "System" leaves the OS default in place.
        /// Note: WinUI cannot programmatically revert Application.RequestedTheme
        /// to the system default once set; "System" therefore only applies at
        /// launch or when a fixed theme has not yet been chosen.
        /// </summary>
        public void ApplyTheme()
        {
            switch (AppSettings.ThemeMode)
            {
                case "Light":
                    RequestedTheme = ApplicationTheme.Light;
                    break;
                case "Dark":
                    RequestedTheme = ApplicationTheme.Dark;
                    break;
            }
        }

        private static ServiceProvider BuildServiceProvider()
        {
            var services = new ServiceCollection();
            // LLM providers and the routing service are registered in the Llm module.
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
