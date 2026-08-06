using System;
using System.Collections.Generic;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using networker.Controls;
using networker.Models;
using Networker.Core.Models.NetworkConfig;
using Networker.Core.Services.NetworkConfig;

namespace networker.NetworkConfig.Views
{
    /// <summary>
    /// Network Config feature page — hosts the Generate, Import/Analyze, Diff,
    /// Vault, and Templates tabs ported from NetworkConfigPro.
    /// </summary>
    public sealed partial class NetworkConfigPage : Page
    {
        private readonly IConfigGenerator _generator;

        public NetworkConfigPage()
        {
            this.InitializeComponent();
            _generator = ((App)Application.Current).Services.GetService<IConfigGenerator>()
                ?? throw new InvalidOperationException("IConfigGenerator is not registered in the DI container.");
            VendorSelector.ItemsSource = GetVendorOptions();
            VendorSelector.SelectedIndex = 0;
        }

        private void GenerateSample_Click(object sender, RoutedEventArgs e)
        {
            if (VendorSelector.SelectedItem is not VendorOption option)
            {
                return;
            }

            ShowCode(GenerateResult, $"{option.DisplayName} sample configuration", _generator.Generate(BuildSampleConfig(option.Vendor)));
        }

        private static IReadOnlyList<VendorOption> GetVendorOptions() => new List<VendorOption>
        {
            new(Vendor.CiscoIos, "Cisco IOS"),
            new(Vendor.CiscoNxos, "Cisco NX-OS"),
            new(Vendor.AristaEos, "Arista EOS"),
            new(Vendor.JuniperJunos, "Juniper Junos"),
            new(Vendor.Sonic, "SONiC"),
            new(Vendor.FortinetFortigate, "Fortinet FortiGate"),
        };

        private static void ShowCode(CodeBlockView view, string title, string text)
        {
            view.DataContext = new ChatMessage { IsCode = true, CodeTitle = title, Text = text };
            view.Visibility = Visibility.Visible;
        }

        private static NetworkDeviceConfig BuildSampleConfig(Vendor vendor) => new()
        {
            Hostname = "core-router-01",
            Vendor = vendor,
            DomainName = "lab.local",
            BannerMotd = "Authorized access only",
            DnsServers = new() { "8.8.8.8" },
            NtpServers = new() { "10.0.0.10" },
            EnableSecret = "hashed-secret",
            Interfaces = new()
            {
                new Interface
                {
                    Name = "GigabitEthernet0/0",
                    InterfaceType = InterfaceType.Gigabit,
                    Description = "Uplink to core",
                    IpAddress = IPAddress.Parse("10.0.0.1"),
                    SubnetMask = IPAddress.Parse("255.255.255.252"),
                    Enabled = true,
                    Speed = "1000",
                    Duplex = "full",
                },
                new Interface
                {
                    Name = "GigabitEthernet0/1",
                    InterfaceType = InterfaceType.Gigabit,
                    Description = "Access port",
                    SwitchportMode = SwitchportMode.Access,
                    AccessVlan = 10,
                    VoiceVlan = 110,
                    Enabled = true,
                },
            },
            Vlans = new()
            {
                new Vlan { VlanId = 10, Name = "Users" },
                new Vlan { VlanId = 110, Name = "Voice" },
            },
            StaticRoutes = new()
            {
                new StaticRoute
                {
                    Destination = "0.0.0.0",
                    Mask = "0.0.0.0",
                    NextHop = "10.0.0.2",
                },
            },
        };

        private sealed record VendorOption(Vendor Vendor, string DisplayName)
        {
            public override string ToString() => DisplayName;
        }
    }
}
