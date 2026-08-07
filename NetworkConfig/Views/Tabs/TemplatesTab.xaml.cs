using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using networker.Models;
using Networker.Core.Models.NetworkConfig;
using Networker.Core.Services.NetworkConfig;

namespace networker.NetworkConfig.Views.Tabs
{
    /// <summary>
    /// Templates tab — browse the <see cref="ITemplateLibrary"/> (built-in +
    /// custom templates), preview the generated configuration for a selection,
    /// and delete custom templates. The Python GUI had no templates tab (templates
    /// lived in a Generate-tab combo); this tab surfaces the library directly.
    /// </summary>
    public sealed partial class TemplatesTab : UserControl
    {
        private readonly ITemplateLibrary _templates;
        private readonly IConfigGenerator _generator;

        public TemplatesTab()
        {
            this.InitializeComponent();

            var services = ((App)Application.Current).Services;
            _templates = services.GetService<ITemplateLibrary>()
                ?? throw new InvalidOperationException("ITemplateLibrary is not registered in the DI container.");
            _generator = services.GetService<IConfigGenerator>()
                ?? throw new InvalidOperationException("IConfigGenerator is not registered in the DI container.");

            LoadTemplates();
        }

        private void LoadTemplates()
        {
            var items = _templates.GetTemplates()
                .OrderBy(t => t.IsBuiltIn ? 0 : 1)
                .ThenBy(t => t.Name)
                .Select(t => new TemplateListItem(
                    t.Name,
                    t.Description,
                    VendorLabel(t.Vendor),
                    t.IsBuiltIn ? "Built-in" : "Custom",
                    t))
                .ToList();

            TemplateList.ItemsSource = items;
        }

        private void TemplateList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TemplateList.SelectedItem is not TemplateListItem item)
            {
                DetailTitle.Text = "No template selected";
                DetailMeta.Text = string.Empty;
                DetailPreview.Visibility = Visibility.Collapsed;
                DeleteButton.Visibility = Visibility.Collapsed;
                return;
            }

            DetailTitle.Text = item.Name;
            DetailMeta.Text = $"{item.VendorLabel}  •  {item.BadgeText}  •  {item.Description}";

            var detail = _templates.GetTemplate(item.Info.Name);
            if (detail is null)
            {
                DetailPreview.Visibility = Visibility.Collapsed;
            }
            else
            {
                var output = _generator.Generate(detail.Config);
                DetailPreview.DataContext = new ChatMessage
                {
                    IsCode = true,
                    CodeTitle = $"{detail.Name} — {item.VendorLabel}",
                    Text = output,
                };
                DetailPreview.Visibility = Visibility.Visible;
            }

            DeleteButton.Visibility = item.Info.IsBuiltIn ? Visibility.Collapsed : Visibility.Visible;
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (TemplateList.SelectedItem is not TemplateListItem item || item.Info.IsBuiltIn)
            {
                return;
            }

            if (_templates.DeleteCustomTemplate(item.Info.Name))
            {
                SetStatus($"Template '{item.Info.Name}' deleted");
                LoadTemplates();
                TemplateList.SelectedItem = null;
            }
            else
            {
                SetStatus($"Could not delete template '{item.Info.Name}'", error: true);
            }
        }

        private static string VendorLabel(Vendor vendor) => vendor switch
        {
            Vendor.CiscoIos => "Cisco IOS/IOS-XE",
            Vendor.CiscoNxos => "Cisco NX-OS",
            Vendor.AristaEos => "Arista EOS",
            Vendor.JuniperJunos => "Juniper Junos",
            Vendor.Sonic => "SONiC",
            Vendor.FortinetFortigate => "Fortinet FortiGate",
            _ => vendor.ToString(),
        };

        private void SetStatus(string message, bool error = false)
        {
            StatusText.Text = message;
            StatusText.Foreground = error
                ? (Brush)Application.Current.Resources["AppDangerBrush"]
                : (Brush)Application.Current.Resources["AppTextSecondaryBrush"];
        }
    }

    /// <summary>
    /// Display row for the template list — pre-computes the vendor label and
    /// badge text so the XAML template needs no converters.
    /// </summary>
    public sealed record TemplateListItem(
        string Name,
        string Description,
        string VendorLabel,
        string BadgeText,
        TemplateInfo Info);
}
