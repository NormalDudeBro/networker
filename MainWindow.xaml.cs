using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI.ApplicationSettings;

namespace networker
{
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            this.InitializeComponent();
            ContentFrame.Navigate(typeof(MainPage));
        }

        private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            if (args.InvokedItemContainer is NavigationViewItem item)
            {
                switch (item.Tag?.ToString())
                {
                    case "home": ContentFrame.Navigate(typeof(MainPage)); break;
                    case "settings": ContentFrame.Navigate(typeof(SettingsPg)); break;
                }
            }
        }
    }
}