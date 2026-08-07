using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using networker.Models;
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
        private static readonly (string DisplayName, Vendor Vendor)[] VendorOptions =
        {
            ("Cisco IOS/IOS-XE", Vendor.CiscoIos),
            ("Cisco NX-OS", Vendor.CiscoNxos),
            ("Arista EOS", Vendor.AristaEos),
            ("Juniper Junos", Vendor.JuniperJunos),
            ("SONiC", Vendor.Sonic),
            ("Fortinet FortiGate", Vendor.FortinetFortigate),
        };

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

            VendorSelector.ItemsSource = VendorOptions.Select(v => new VendorOption(v.DisplayName, v.Vendor)).ToList();
            VendorSelector.SelectedIndex = 0;

            InterfaceRows.ItemsSource = _interfaces;
            AclEntryRows.ItemsSource = _aclEntries;
            BgpNeighborRows.ItemsSource = _bgpNeighbors;

            LoadTemplates();
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
                StatusText.Text = $"Applied template '{item.Name}'.";
            }
        }

        private void Generate_Click(object sender, RoutedEventArgs e)
        {
            var form = CollectFormData();
            if (string.IsNullOrWhiteSpace(form.Basic.Hostname))
            {
                StatusText.Text = "Hostname is required.";
                GenerateOutput.Visibility = Visibility.Collapsed;
                ValidationText.Visibility = Visibility.Collapsed;
                return;
            }

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
            StatusText.Text = $"Generated configuration for {form.Basic.Hostname.Trim()}";
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

        private void ShowValidation(IReadOnlyList<ValidationIssue> issues)
        {
            if (issues.Count == 0)
            {
                ValidationText.Text = "No issues found!";
            }
            else
            {
                var errors = issues.Count(i => i.Severity == ValidationSeverity.Error);
                var warnings = issues.Count(i => i.Severity == ValidationSeverity.Warning);
                var infos = issues.Count(i => i.Severity == ValidationSeverity.Info);

                var sb = new StringBuilder();
                sb.AppendLine($"Found: {errors} errors, {warnings} warnings, {infos} info");
                sb.AppendLine();

                foreach (var issue in issues)
                {
                    var icon = issue.Severity switch
                    {
                        ValidationSeverity.Error => "[ERROR]",
                        ValidationSeverity.Warning => "[WARN]",
                        _ => "[INFO]",
                    };
                    sb.AppendLine($"{icon} {issue.Message}");
                    sb.AppendLine($"       Location: {issue.Location}");
                    if (!string.IsNullOrEmpty(issue.Recommendation))
                    {
                        sb.AppendLine($"       Tip: {issue.Recommendation}");
                    }
                }

                ValidationText.Text = sb.ToString();
            }

            ValidationText.Visibility = Visibility.Visible;
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
