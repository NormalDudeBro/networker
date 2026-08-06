using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using networker.Controls;
using networker.Services;

namespace networker
{
    public sealed partial class MainWindow : Window
    {
        public static MainWindow? Instance { get; private set; }

        public MainWindow()
        {
            this.InitializeComponent();
            Instance = this;

            // Initialize theme from settings using Root Grid's RequestedTheme (propagates to all children)
            ApplyThemeToRoot();

            Toaster.Initialize(ToastHost, DispatcherQueue);
            BuildPaletteCommands();

            ContentFrame.Navigate(typeof(MainPage));
        }

        public void ToggleTheme()
        {
            // Simple Light ↔ Dark cycle (System is handled at startup only)
            AppSettings.ThemeMode = AppSettings.ThemeMode switch
            {
                "Light" => "Dark",
                _ => "Light"
            };
            ApplyThemeToRoot();
        }

        private void ApplyThemeToRoot()
        {
            // Root Grid's RequestedTheme propagates to all children (NavView + ContentFrame)
            Root.RequestedTheme = AppSettings.ThemeMode switch
            {
                "Light" => ElementTheme.Light,
                "Dark" => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };
        }

        public void ApplyThemeToFramePublic() => ApplyThemeToRoot();

        public void OpenPalette() => Palette.Open();

        private void BuildPaletteCommands()
        {
            Palette.SetCommands(new[]
            {
                new PaletteCommand("Go to Home", "Open the chat workspace", "\uE80F", () => NavigateTo("home"), "chat", "home"),
                new PaletteCommand("Go to Tools", "Open the network toolkit", "\uE774", () => NavigateTo("tools"), "ip", "config", "tools", "subnet", "audit"),
                new PaletteCommand("Go to Network Config", "Generate, import, and validate device configs", "\uE943", () => NavigateTo("networkconfig"), "config", "generate", "vault", "network"),
                new PaletteCommand("Go to Settings", "Provider and application settings", "\uE713", () => NavigateTo("settings"), "settings", "provider", "theme"),
                new PaletteCommand("Toggle theme", "Switch light / dark / system", "\uE790", ToggleTheme, "theme", "dark", "light"),
                new PaletteCommand("New chat", "Start a fresh conversation", "\uE8BD", () => MainPage.Current?.NewChat(), "new", "chat", "clear"),
                new PaletteCommand("Clear history", "Remove all conversation messages", "\uE74D", () => MainPage.Current?.ClearHistory(), "history", "clear", "delete"),
            });
        }

        private void NavigateTo(string tag)
        {
            switch (tag)
            {
                case "home": ContentFrame.Navigate(typeof(MainPage)); break;
                case "tools": ContentFrame.Navigate(typeof(ToolsPage)); break;
                case "networkconfig": ContentFrame.Navigate(typeof(NetworkConfig.Views.NetworkConfigPage)); break;
                case "settings": ContentFrame.Navigate(typeof(SettingsPg)); break;
            }
        }

        private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            if (args.InvokedItemContainer is NavigationViewItem item && item.Tag is string tag)
            {
                NavigateTo(tag);
            }
        }

        private void PaletteAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            args.Handled = true;
            Palette.Open();
        }

        private void RefreshHealthAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            args.Handled = true;
            MainPage.Current?.RefreshConnection();
        }
    }
}