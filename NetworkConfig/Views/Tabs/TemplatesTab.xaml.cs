using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using networker.Models;
using networker.Services;
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
                ShowEmptyDetail();
                return;
            }

            DetailTitle.Text = item.Name;
            DetailMeta.Text = $"{item.VendorLabel}  •  {item.BadgeText}  •  {item.Description}";

            var detail = _templates.GetTemplate(item.Info.Name);
            if (detail is null)
            {
                ShowEmptyDetail();
                SetStatus($"Template '{item.Name}' is unavailable.", error: true);
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
                EmptyDetailPanel.Visibility = Visibility.Collapsed;
                TemplateDetailPanel.Visibility = Visibility.Visible;
                DeleteButton.Visibility = item.Info.IsBuiltIn ? Visibility.Collapsed : Visibility.Visible;
                SetStatus(string.Empty);
            }
        }

        private async void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (TemplateList.SelectedItem is not TemplateListItem item || item.Info.IsBuiltIn)
            {
                return;
            }

            int deletedIndex = TemplateList.SelectedIndex;
            var dialog = new ContentDialog
            {
                Title = "Delete custom template?",
                Content = $"Delete '{item.Name}'? This cannot be undone.",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

            try
            {
                if (_templates.DeleteCustomTemplate(item.Info.Name))
                {
                    SetStatus($"Template '{item.Info.Name}' deleted");
                    LogActivity("Template Deleted", $"'{item.Info.Name}' removed from custom templates", "\uE8A5");
                    LoadTemplates();
                    FocusNearestTemplate(deletedIndex);
                }
                else
                {
                    SetStatus($"Could not delete template '{item.Info.Name}'", error: true);
                    DeleteButton.Focus(FocusState.Programmatic);
                }
            }
            catch (Exception ex)
            {
                SetStatus($"Could not delete template '{item.Info.Name}': {ex.Message}", error: true);
                DeleteButton.Focus(FocusState.Programmatic);
            }
        }

        private void ShowEmptyDetail()
        {
            EmptyDetailPanel.Visibility = Visibility.Visible;
            TemplateDetailPanel.Visibility = Visibility.Collapsed;
            DetailTitle.Text = string.Empty;
            DetailMeta.Text = string.Empty;
            DetailPreview.DataContext = null;
            DeleteButton.Visibility = Visibility.Collapsed;
        }

        private void FocusNearestTemplate(int previousIndex)
        {
            if (TemplateList.Items.Count == 0)
            {
                ShowEmptyDetail();
                TemplateList.Focus(FocusState.Programmatic);
                return;
            }

            int nextIndex = Math.Min(previousIndex, TemplateList.Items.Count - 1);
            TemplateList.SelectedIndex = nextIndex;
            TemplateList.ScrollIntoView(TemplateList.SelectedItem);
            DispatcherQueue.TryEnqueue(() =>
            {
                if (TemplateList.ContainerFromIndex(nextIndex) is Control container)
                {
                    container.Focus(FocusState.Programmatic);
                }
                else
                {
                    TemplateList.Focus(FocusState.Programmatic);
                }
            });
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
            StatusText.Style = (Style)Application.Current.Resources[
                error ? "InlineErrorTextStyle" : "InlineStatusTextStyle"];
        }

        private static void LogActivity(string title, string detail, string glyph = "\uE774")
        {
            string text = (detail ?? "").Trim();
            RecentActivity.Add(new ActivityItem
            {
                Title = title,
                Detail = text.Length <= 200 ? text : text[..200] + "…",
                Timestamp = DateTime.Now,
                Glyph = glyph,
            });
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
