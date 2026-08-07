using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace networker.NetworkConfig.Views
{
    /// <summary>
    /// Network Config feature page — hosts the Generate, Import/Analyze, Diff,
    /// Vault, and Templates tabs ported from NetworkConfigPro.
    /// </summary>
    public sealed partial class NetworkConfigPage : Page
    {
        public NetworkConfigPage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is string header && !string.IsNullOrWhiteSpace(header))
            {
                SelectTab(header);
            }
        }

        private void SelectTab(string header)
        {
            for (int i = 0; i < ConfigTabs.TabItems.Count; i++)
            {
                if (ConfigTabs.TabItems[i] is TabViewItem { Header: string h } && h == header)
                {
                    ConfigTabs.SelectedIndex = i;
                    return;
                }
            }
        }
    }
}
