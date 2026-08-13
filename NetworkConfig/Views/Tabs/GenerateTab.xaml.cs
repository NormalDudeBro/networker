using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using networker.Models;
using networker.Services;
using Networker.Core.Models.NetworkConfig;
using Networker.Core.Services.NetworkConfig;

namespace networker.NetworkConfig.Views.Tabs
{
    /// <summary>
    /// Generate tab — full device form ported from NetworkConfigPro's Generate
    /// tab (<c>_generate_config</c>). The form edits a <see cref="TemplateFormData"/>
    /// preset; predefined templates (from <see cref="ITemplateLibrary"/>) pre-fill
    /// the form, and generation runs the shared converter + generator + validator.
    /// </summary>
    public sealed partial class GenerateTab : UserControl
    {
        public event Action? WorkspaceChanged;
        public event Action<string>? ActionCompleted;
        public event Action<string>? ActionFailed;
        private static readonly (string DisplayName, Vendor Vendor)[] VendorOptions =
        {
            ("Cisco IOS/IOS-XE", Vendor.CiscoIos),
            ("Cisco NX-OS", Vendor.CiscoNxos),
            ("Arista EOS", Vendor.AristaEos),
            ("Juniper Junos", Vendor.JuniperJunos),
            ("SONiC", Vendor.Sonic),
            ("Fortinet FortiGate", Vendor.FortinetFortigate),
        };

        /// <summary>
        /// Canonical vendor display names — the single source of truth for the
        /// vendor ComboBox and the <see cref="AppSettings.DefaultVendor"/> setting.
        /// </summary>
        public static IReadOnlyList<string> VendorDisplayNames { get; } =
            VendorOptions.Select(v => v.DisplayName).ToArray();

        // Option lists for the combo boxes. Values must match TemplateFormData
        // defaults / TemplateFormConverter keys exactly (e.g. "VLAN",
        // "Port-Channel", "rapid-pvst").
        public static IReadOnlyList<string> InterfaceTypeOptions { get; } = new[]
        {
            "GigabitEthernet",
            "TenGigabitEthernet",
            "FortyGigabitEthernet",
            "HundredGigabitEthernet",
            "Ethernet",
            "Loopback",
            "VLAN",
            "Port-Channel",
            "Management",
        };

        public static IReadOnlyList<string> AclTypeOptions { get; } = new[] { "Standard", "Extended" };

        public static IReadOnlyList<string> AclActionOptions { get; } = new[] { "permit", "deny" };

        public static IReadOnlyList<string> AclProtocolOptions { get; } = new[] { "ip", "tcp", "udp", "icmp" };

        public static IReadOnlyList<string> LogOptions { get; } = new[] { "None", "log" };

        public static IReadOnlyList<string> StpModeOptions { get; } = new[] { "rapid-pvst", "pvst", "mst" };

        private readonly IConfigGenerator _generator;
        private readonly IConfigValidator _validator;
        private readonly ITemplateLibrary _templates;

        private readonly ObservableCollection<TemplateInterfaceEntry> _interfaces = new();
        private readonly ObservableCollection<TemplateAclEntry> _aclEntries = new();
        private readonly ObservableCollection<TemplateBgpNeighbor> _bgpNeighbors = new();

        public GenerateTab()
        {
            this.InitializeComponent();

            var services = ((App)Application.Current).Services;
            _generator = services.GetService<IConfigGenerator>()
                ?? throw new InvalidOperationException("IConfigGenerator is not registered in the DI container.");
            _validator = services.GetService<IConfigValidator>()
                ?? throw new InvalidOperationException("IConfigValidator is not registered in the DI container.");
            _templates = services.GetService<ITemplateLibrary>()
                ?? throw new InvalidOperationException("ITemplateLibrary is not registered in the DI container.");

            var vendorOptions = VendorOptions.Select(v => new VendorOption(v.DisplayName, v.Vendor)).ToList();
            VendorSelector.ItemsSource = vendorOptions;

            var defaultVendor = vendorOptions.FirstOrDefault(v => v.DisplayName == AppSettings.DefaultVendor);
            VendorSelector.SelectedIndex = defaultVendor is null ? 0 : vendorOptions.IndexOf(defaultVendor);

            InterfaceRows.ItemsSource = _interfaces;
            AclEntryRows.ItemsSource = _aclEntries;
            BgpNeighborRows.ItemsSource = _bgpNeighbors;

            LoadTemplates();
            HostnameInput.TextChanged += (_, _) => WorkspaceChanged?.Invoke();
        }

        private void LoadTemplates()
        {
            var items = new List<TemplateItem>();
            foreach (var info in _templates.GetTemplates())
            {
                var detail = _templates.GetTemplate(info.Name);
                if (detail is not null)
                {
                    items.Add(new TemplateItem(info.Name, info.Description, detail));
                }
            }

            TemplateSelector.ItemsSource = items;
        }

        private void ApplyTemplate_Click(object sender, RoutedEventArgs e)
        {
            if (TemplateSelector.SelectedItem is TemplateItem item && item.Detail.FormData is { } data)
            {
                ApplyFormData(data);
                InvalidateOutput();
                SetStatus($"Applied template '{item.Name}'. Generate to refresh the output.");
                LogActivity("Template Applied", $"'{item.Name}' pre-filled the generate form", "\uE8A5");
                return;
            }

            SetStatus("Select a template with an editable form preset.", error: true);
        }

        private void Generate_Click(object sender, RoutedEventArgs e)
        {
            var form = CollectFormData();
            if (string.IsNullOrWhiteSpace(form.Basic.Hostname))
            {
                InvalidateOutput();
                SetStatus("Hostname is required.", error: true);
                HostnameInput.StartBringIntoView();
                HostnameInput.Focus(FocusState.Programmatic);
                ActionFailed?.Invoke("Hostname is required.");
                return;
            }

            GenerateButton.IsEnabled = false;
            SetStatus("Generating configuration...");
            try
            {
                var config = TemplateFormConverter.Convert(form);
                var output = _generator.Generate(config);

                GenerateOutput.DataContext = new ChatMessage
                {
                    IsCode = true,
                    CodeTitle = $"{form.Basic.Hostname.Trim()} — generated configuration",
                    Text = output,
                };
                GenerateOutput.Visibility = Visibility.Visible;

                ShowValidation(_validator.Validate(config));
                OutputEmptyState.Visibility = Visibility.Collapsed;
                GeneratedResultPanel.Visibility = Visibility.Visible;
                SetStatus($"Generated configuration for {form.Basic.Hostname.Trim()}.", success: true);
                AppSettings.DefaultVendor = form.Basic.Vendor;
                LogActivity("Config Generator", $"{form.Basic.Hostname.Trim()} — {form.Basic.Vendor}", "\uE943");
                ActionCompleted?.Invoke($"Generated configuration for {form.Basic.Hostname.Trim()}.");

                DispatcherQueue.TryEnqueue(() =>
                {
                    OutputWorkbench.StartBringIntoView();
                    GenerateOutput.Focus(FocusState.Programmatic);
                });
            }
            catch (Exception ex)
            {
                InvalidateOutput();
                SetStatus($"Generation failed: {ex.Message}", error: true);
                ActionFailed?.Invoke(ex.Message);
            }
            finally
            {
                GenerateButton.IsEnabled = true;
            }
        }

        private TemplateFormData CollectFormData() => new()
        {
            Basic = new TemplateBasic
            {
                Vendor = (VendorSelector.SelectedItem as VendorOption)?.DisplayName ?? string.Empty,
                Hostname = HostnameInput.Text,
                Domain = DomainInput.Text,
                EnableSecret = EnableInput.Password,
                DnsServers = DnsInput.Text,
                NtpServers = NtpInput.Text,
            },
            Interfaces = _interfaces.ToList(),
            Vlans = VlansInput.Text,
            Acl = new TemplateAcl
            {
                Name = AclNameInput.Text,
                Type = AclTypeCombo.SelectedItem as string ?? "Extended",
                Entries = _aclEntries.ToList(),
            },
            StaticRoutes = RoutesInput.Text,
            Ospf = new TemplateOspf
            {
                ProcessId = OspfProcessInput.Text,
                RouterId = OspfRouterIdInput.Text,
                ReferenceBandwidth = OspfRefBwInput.Text,
                Networks = OspfNetworksInput.Text,
                PassiveInterfaces = OspfPassiveInput.Text,
            },
            Bgp = new TemplateBgp
            {
                LocalAs = BgpAsInput.Text,
                RouterId = BgpRouterIdInput.Text,
                Neighbors = _bgpNeighbors.ToList(),
                Networks = BgpNetworksInput.Text,
            },
            Eigrp = new TemplateEigrp
            {
                AsNumber = EigrpAsInput.Text,
                RouterId = EigrpRouterIdInput.Text,
                NamedMode = EigrpNamedModeCheck.IsChecked == true,
                Name = EigrpNameInput.Text,
                Networks = EigrpNetworksInput.Text,
                PassiveInterfaces = EigrpPassiveInput.Text,
            },
            Stp = new TemplateStp
            {
                Mode = StpModeCombo.SelectedItem as string ?? "rapid-pvst",
                Priority = StpPriorityInput.Text,
                RootPrimaryVlans = StpRootPrimaryInput.Text,
                RootSecondaryVlans = StpRootSecondaryInput.Text,
                PortfastDefault = StpPortfastCheck.IsChecked == true,
                BpduguardDefault = StpBpduguardCheck.IsChecked == true,
            },
        };

        public TemplateFormData CaptureState()
        {
            TemplateFormData state = CollectFormData();
            state.Basic.EnableSecret = string.Empty;
            return state;
        }

        public void RestoreState(TemplateFormData? state)
        {
            if (state is null) return;
            ApplyFormData(state);
            EnableInput.Password = string.Empty;
        }

        /// <summary>
        /// Populates the form from a template preset. Row items are cloned so
        /// two-way bindings never write back into the shared template data.
        /// </summary>
        private void ApplyFormData(TemplateFormData data)
        {
            var comboItems = VendorSelector.Items.Cast<VendorOption>().ToList();
            VendorSelector.SelectedItem = comboItems.FirstOrDefault(v => v.DisplayName == data.Basic.Vendor) ?? comboItems[0];

            HostnameInput.Text = data.Basic.Hostname;
            DomainInput.Text = data.Basic.Domain;
            EnableInput.Password = data.Basic.EnableSecret;
            DnsInput.Text = data.Basic.DnsServers;
            NtpInput.Text = data.Basic.NtpServers;

            _interfaces.Clear();
            foreach (var entry in data.Interfaces)
            {
                _interfaces.Add(new TemplateInterfaceEntry
                {
                    Type = entry.Type,
                    Number = entry.Number,
                    Description = entry.Description,
                    Ip = entry.Ip,
                    Mask = entry.Mask,
                });
            }

            VlansInput.Text = data.Vlans;
            RoutesInput.Text = data.StaticRoutes;

            AclNameInput.Text = data.Acl.Name;
            AclTypeCombo.SelectedItem = data.Acl.Type;
            _aclEntries.Clear();
            foreach (var entry in data.Acl.Entries)
            {
                _aclEntries.Add(new TemplateAclEntry
                {
                    Sequence = entry.Sequence,
                    Action = entry.Action,
                    Protocol = entry.Protocol,
                    Source = entry.Source,
                    SourceWildcard = entry.SourceWildcard,
                    Destination = entry.Destination,
                    DestinationWildcard = entry.DestinationWildcard,
                    DestinationPort = entry.DestinationPort,
                    Log = string.IsNullOrEmpty(entry.Log) ? "None" : entry.Log,
                });
            }

            OspfProcessInput.Text = data.Ospf.ProcessId;
            OspfRouterIdInput.Text = data.Ospf.RouterId;
            OspfRefBwInput.Text = data.Ospf.ReferenceBandwidth;
            OspfNetworksInput.Text = data.Ospf.Networks;
            OspfPassiveInput.Text = data.Ospf.PassiveInterfaces;

            BgpAsInput.Text = data.Bgp.LocalAs;
            BgpRouterIdInput.Text = data.Bgp.RouterId;
            _bgpNeighbors.Clear();
            foreach (var neighbor in data.Bgp.Neighbors)
            {
                _bgpNeighbors.Add(new TemplateBgpNeighbor
                {
                    IpAddress = neighbor.IpAddress,
                    RemoteAs = neighbor.RemoteAs,
                    Description = neighbor.Description,
                    UpdateSource = neighbor.UpdateSource,
                    EbgpMultihop = neighbor.EbgpMultihop,
                });
            }

            BgpNetworksInput.Text = data.Bgp.Networks;

            EigrpAsInput.Text = data.Eigrp.AsNumber;
            EigrpRouterIdInput.Text = data.Eigrp.RouterId;
            EigrpNamedModeCheck.IsChecked = data.Eigrp.NamedMode;
            EigrpNameInput.Text = data.Eigrp.Name;
            EigrpNetworksInput.Text = data.Eigrp.Networks;
            EigrpPassiveInput.Text = data.Eigrp.PassiveInterfaces;

            StpModeCombo.SelectedItem = data.Stp.Mode;
            StpPriorityInput.Text = data.Stp.Priority;
            StpRootPrimaryInput.Text = data.Stp.RootPrimaryVlans;
            StpRootSecondaryInput.Text = data.Stp.RootSecondaryVlans;
            StpPortfastCheck.IsChecked = data.Stp.PortfastDefault;
            StpBpduguardCheck.IsChecked = data.Stp.BpduguardDefault;

        }

        private void InvalidateOutput()
        {
            OutputEmptyState.Visibility = Visibility.Visible;
            GeneratedResultPanel.Visibility = Visibility.Collapsed;
            GenerateOutput.Visibility = Visibility.Collapsed;
            GenerateOutput.DataContext = null;
            ValidationSummaryText.Visibility = Visibility.Collapsed;
            ValidationList.Visibility = Visibility.Collapsed;
        }

        private void SetStatus(string message, bool error = false, bool success = false)
        {
            StatusText.Text = message;
            string styleKey = error
                ? "InlineErrorTextStyle"
                : success ? "InlineSuccessTextStyle" : "InlineStatusTextStyle";
            StatusText.Style = (Style)Application.Current.Resources[styleKey];
        }

        private void ShowValidation(IReadOnlyList<ValidationIssue> issues)
        {
            if (issues.Count == 0)
            {
                ValidationSummaryText.Text = "No issues found — configuration validated clean.";
                ValidationSummaryText.Visibility = Visibility.Visible;
                ValidationList.Visibility = Visibility.Collapsed;
                return;
            }

            var errors = issues.Count(i => i.Severity == ValidationSeverity.Error);
            var warnings = issues.Count(i => i.Severity == ValidationSeverity.Warning);
            var infos = issues.Count(i => i.Severity == ValidationSeverity.Info);

            ValidationSummaryText.Text = $"Found: {errors} errors, {warnings} warnings, {infos} info";
            ValidationSummaryText.Visibility = Visibility.Visible;

            ValidationList.ItemsSource = issues.Select(issue => new ValidationRow
            {
                Severity = issue.Severity.ToString(),
                SeverityBrush = SeverityBrush(issue.Severity),
                Message = issue.Message,
                Location = issue.Location,
                Recommendation = string.IsNullOrEmpty(issue.Recommendation)
                    ? string.Empty
                    : $"Tip: {issue.Recommendation}",
            }).ToList();
            ValidationList.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Display row for the validation findings list — mirrors the severity
        /// pill pattern used across the workflow (see WorkflowPage FindingItem).
        /// </summary>
        private sealed class ValidationRow
        {
            public required string Severity { get; init; }
            public required SolidColorBrush SeverityBrush { get; init; }
            public required string Message { get; init; }
            public required string Location { get; init; }
            public required string Recommendation { get; init; }
        }

        private static SolidColorBrush SeverityBrush(ValidationSeverity severity) => severity switch
        {
            ValidationSeverity.Error => Brush("AppDangerBrush"),
            ValidationSeverity.Warning => Brush("AppWarningBrush"),
            _ => Brush("AppTextSecondaryBrush"),
        };

        private static SolidColorBrush Brush(string key)
        {
            if (Application.Current.Resources.TryGetValue(key, out object value) && value is SolidColorBrush brush)
            {
                return brush;
            }

            return new SolidColorBrush(Colors.Gray);
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

        private void AddInterface_Click(object sender, RoutedEventArgs e) =>
            _interfaces.Add(new TemplateInterfaceEntry { Type = "GigabitEthernet" });

        private void RemoveInterface_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is TemplateInterfaceEntry entry)
            {
                _interfaces.Remove(entry);
            }
        }

        private void AddAclEntry_Click(object sender, RoutedEventArgs e) =>
            _aclEntries.Add(new TemplateAclEntry { Action = "permit", Protocol = "ip", Log = "None" });

        private void RemoveAclEntry_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is TemplateAclEntry entry)
            {
                _aclEntries.Remove(entry);
            }
        }

        private void AddBgpNeighbor_Click(object sender, RoutedEventArgs e) =>
            _bgpNeighbors.Add(new TemplateBgpNeighbor());

        private void RemoveBgpNeighbor_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is TemplateBgpNeighbor neighbor)
            {
                _bgpNeighbors.Remove(neighbor);
            }
        }

        private sealed record VendorOption(string DisplayName, Vendor Vendor)
        {
            public override string ToString() => DisplayName;
        }

        private sealed record TemplateItem(string Name, string Description, TemplateDetail Detail);
    }
}
