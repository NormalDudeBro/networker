using System.Net;
using Networker.Core.Models.NetworkConfig;
using Networker.Core.Services.NetworkConfig.Parsers;

namespace Networker.Core.Tests.Services.NetworkConfig.Parsers;

/// <summary>
/// Shared config samples for the parser tests, ported verbatim from
/// NetworkConfigPro tests/unit/test_parser.py fixtures.
/// </summary>
internal static class TestConfigs
{
    /// <summary>
    /// Sample Cisco IOS configuration (Python <c>sample_ios_config</c>
    /// fixture). The leading blank line of the Python original is dropped;
    /// every assertion in the ported tests is unaffected.
    /// </summary>
    internal const string SampleIosConfig = """
        !
        version 15.2
        !
        hostname test-router
        !
        enable secret 5 $1$abc$xyz
        !
        ip domain-name example.com
        !
        ip name-server 8.8.8.8
        ip name-server 8.8.4.4
        !
        ntp server pool.ntp.org
        !
        banner motd ^
        Authorized access only!
        ^
        !
        vlan 10
         name DATA
        !
        vlan 20
         name VOICE
        !
        interface GigabitEthernet0/0
         description WAN Uplink
         ip address 10.0.0.1 255.255.255.0
         no shutdown
        !
        interface GigabitEthernet0/1
         description LAN Access
         switchport mode access
         switchport access vlan 10
         shutdown
        !
        interface GigabitEthernet0/2
         description Trunk to Switch
         switchport mode trunk
         switchport trunk allowed vlan 10,20,30
         switchport trunk native vlan 1
         no shutdown
        !
        ip access-list extended BLOCK-TELNET
         10 deny tcp any any eq 23 log
         20 permit ip any any
        !
        ip route 0.0.0.0 0.0.0.0 10.0.0.254
        ip route 192.168.100.0 255.255.255.0 10.0.0.2 200 name backup
        !
        router ospf 1
         router-id 1.1.1.1
         auto-cost reference-bandwidth 10000
         network 10.0.0.0 0.0.0.255 area 0
         network 192.168.1.0 0.0.0.255 area 1
         passive-interface GigabitEthernet0/1
         default-information originate
        !
        router bgp 65000
         bgp router-id 1.1.1.1
         bgp log-neighbor-changes
         network 10.0.0.0/24
         neighbor 10.0.0.2 remote-as 65001
         neighbor 10.0.0.2 description ISP Peer
         neighbor 10.0.0.2 password SecretBGP
         neighbor 10.0.0.2 update-source Loopback0
         neighbor 10.0.0.2 ebgp-multihop 2
        !
        end
        """;
}

/// <summary>
/// Tests for CiscoIOSParser, ported from NetworkConfigPro
/// tests/unit/test_parser.py <c>TestCiscoIOSParser</c>.
/// </summary>
public class TestCiscoIOSParser
{
    private readonly CiscoIosParser _parser = new();

    [Fact]
    public void DetectVendor_Positive()
    {
        Assert.True(_parser.DetectVendor(TestConfigs.SampleIosConfig));
    }

    [Fact]
    public void DetectVendor_Negative()
    {
        const string junosConfig = """
            system {
                host-name juniper-router;
            }
            """;

        Assert.False(_parser.DetectVendor(junosConfig));
    }

    [Fact]
    public void Parse_Hostname()
    {
        var result = _parser.Parse(TestConfigs.SampleIosConfig);
        var config = Assert.IsType<NetworkDeviceConfig>(result.Config);

        Assert.Equal("test-router", config.Hostname);
    }

    [Fact]
    public void Parse_DomainName()
    {
        var result = _parser.Parse(TestConfigs.SampleIosConfig);
        var config = Assert.IsType<NetworkDeviceConfig>(result.Config);

        Assert.Equal("example.com", config.DomainName);
    }

    [Fact]
    public void Parse_DnsServers()
    {
        var result = _parser.Parse(TestConfigs.SampleIosConfig);
        var config = Assert.IsType<NetworkDeviceConfig>(result.Config);

        Assert.Contains("8.8.8.8", config.DnsServers);
        Assert.Contains("8.8.4.4", config.DnsServers);
    }

    [Fact]
    public void Parse_NtpServers()
    {
        var result = _parser.Parse(TestConfigs.SampleIosConfig);
        var config = Assert.IsType<NetworkDeviceConfig>(result.Config);

        Assert.Contains("pool.ntp.org", config.NtpServers);
    }

    [Fact]
    public void Parse_Banner()
    {
        var result = _parser.Parse(TestConfigs.SampleIosConfig);
        var config = Assert.IsType<NetworkDeviceConfig>(result.Config);

        Assert.Contains("Authorized access only!", config.BannerMotd);
    }

    [Fact]
    public void Parse_Vlans()
    {
        var result = _parser.Parse(TestConfigs.SampleIosConfig);
        var config = Assert.IsType<NetworkDeviceConfig>(result.Config);

        Assert.Equal(2, config.Vlans.Count);

        var vlanIds = config.Vlans.Select(v => v.VlanId).ToList();
        Assert.Contains(10, vlanIds);
        Assert.Contains(20, vlanIds);

        var dataVlan = config.Vlans.First(v => v.VlanId == 10);
        Assert.Equal("DATA", dataVlan.Name);
    }

    [Fact]
    public void Parse_Interfaces()
    {
        var result = _parser.Parse(TestConfigs.SampleIosConfig);
        var config = Assert.IsType<NetworkDeviceConfig>(result.Config);

        Assert.Equal(3, config.Interfaces.Count);

        // Routed interface
        var gi0_0 = config.Interfaces.First(i => i.Name.Contains("0/0"));
        Assert.Equal("WAN Uplink", gi0_0.Description);
        Assert.Equal(IPAddress.Parse("10.0.0.1"), gi0_0.IpAddress);
        Assert.Equal(IPAddress.Parse("255.255.255.0"), gi0_0.SubnetMask);
        Assert.True(gi0_0.Enabled);

        // Access interface
        var gi0_1 = config.Interfaces.First(i => i.Name.Contains("0/1"));
        Assert.Equal(10, gi0_1.VlanId);
        Assert.False(gi0_1.Enabled);

        // Trunk interface
        var gi0_2 = config.Interfaces.First(i => i.Name.Contains("0/2"));
        Assert.True(gi0_2.IsTrunk);
        Assert.Equal("10,20,30", gi0_2.TrunkAllowedVlans);
        Assert.Equal(1, gi0_2.NativeVlan);
    }

    [Fact]
    public void Parse_Acls()
    {
        var result = _parser.Parse(TestConfigs.SampleIosConfig);
        var config = Assert.IsType<NetworkDeviceConfig>(result.Config);

        Assert.Single(config.Acls);

        var acl = config.Acls[0];
        Assert.Equal("BLOCK-TELNET", acl.Name);
        Assert.True(acl.IsExtended);
        Assert.Equal(2, acl.Entries.Count);
    }

    [Fact]
    public void Parse_StaticRoutes()
    {
        var result = _parser.Parse(TestConfigs.SampleIosConfig);
        var config = Assert.IsType<NetworkDeviceConfig>(result.Config);

        Assert.Equal(2, config.StaticRoutes.Count);

        // Default route
        var defaultRoute = config.StaticRoutes.First(r => r.Destination == "0.0.0.0");
        Assert.Equal("10.0.0.254", defaultRoute.NextHop);
        Assert.Equal(1, defaultRoute.AdminDistance);

        // Named route with admin distance
        var backup = config.StaticRoutes.First(r => r.Destination == "192.168.100.0");
        Assert.Equal(200, backup.AdminDistance);
        Assert.Equal("backup", backup.Name);
    }

    [Fact]
    public void Parse_Ospf()
    {
        var result = _parser.Parse(TestConfigs.SampleIosConfig);
        var config = Assert.IsType<NetworkDeviceConfig>(result.Config);

        var ospf = Assert.IsType<OspfConfig>(config.Ospf);
        Assert.Equal(1, ospf.ProcessId);
        Assert.Equal("1.1.1.1", ospf.RouterId);
        Assert.Equal(10000, ospf.ReferenceBandwidth);
        Assert.Equal(2, ospf.Networks.Count);
        Assert.True(ospf.DefaultInformationOriginate);
        Assert.Contains("GigabitEthernet0/1", ospf.PassiveInterfaces);
    }

    [Fact]
    public void Parse_Bgp()
    {
        var result = _parser.Parse(TestConfigs.SampleIosConfig);
        var config = Assert.IsType<NetworkDeviceConfig>(result.Config);

        var bgp = Assert.IsType<BgpConfig>(config.Bgp);
        Assert.Equal(65000, bgp.LocalAs);
        Assert.Equal("1.1.1.1", bgp.RouterId);
        Assert.True(bgp.LogNeighborChanges);
        Assert.Single(bgp.Neighbors);

        var neighbor = bgp.Neighbors[0];
        Assert.Equal("10.0.0.2", neighbor.IpAddress);
        Assert.Equal(65001, neighbor.RemoteAs);
        Assert.Equal("ISP Peer", neighbor.Description);
        Assert.Equal("SecretBGP", neighbor.Password);
        Assert.Equal("Loopback0", neighbor.UpdateSource);
        Assert.Equal(2, neighbor.EbgpMultihop);
    }

    [Fact]
    public void Parse_Result_HasNoErrors()
    {
        var result = _parser.Parse(TestConfigs.SampleIosConfig);

        Assert.IsType<NetworkDeviceConfig>(result.Config);
        Assert.Empty(result.Errors);
        Assert.Equal(Vendor.CiscoIos, result.Vendor);
    }
}

/// <summary>
/// Tests for ConfigParserFactory, ported from NetworkConfigPro
/// tests/unit/test_parser.py <c>TestConfigParserFactory</c>. The C# factory
/// uses instance methods where the Python classmethod equivalent exists.
/// </summary>
public class TestConfigParserFactory
{
    private readonly ConfigParserFactory _factory = new();

    [Fact]
    public void DetectAndParse_Ios()
    {
        var result = _factory.DetectAndParse(TestConfigs.SampleIosConfig);

        var config = Assert.IsType<NetworkDeviceConfig>(result.Config);
        Assert.Equal(Vendor.CiscoIos, result.Vendor);
        Assert.Equal("test-router", config.Hostname);
    }

    [Fact]
    public void ParseWithKnownVendor()
    {
        var result = _factory.ParseWithVendor(TestConfigs.SampleIosConfig, Vendor.CiscoIos);

        var config = Assert.IsType<NetworkDeviceConfig>(result.Config);
        Assert.Equal("test-router", config.Hostname);
    }

    [Fact]
    public void UnknownFormat_ReturnsError()
    {
        const string unknownConfig = """
            some random text
            that is not a network config
            """;

        var result = _factory.DetectAndParse(unknownConfig);

        Assert.Null(result.Config);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void MissingHostname_GeneratesWarning()
    {
        const string minimalConfig = """
            interface GigabitEthernet0/0
             ip address 10.0.0.1 255.255.255.0
            !
            """;

        var result = new CiscoIosParser().Parse(minimalConfig);

        Assert.Contains(result.Warnings, w => w.ToLowerInvariant().Contains("hostname"));
    }
}

/// <summary>
/// Tests for interface type detection, ported from NetworkConfigPro
/// tests/unit/test_parser.py <c>TestInterfaceTypeParsing</c>.
/// </summary>
public class TestInterfaceTypeParsing
{
    private readonly CiscoIosParser _parser = new();

    [Fact]
    public void DetectGigabit()
    {
        const string config = """
            hostname router
            interface GigabitEthernet0/0
             ip address 10.0.0.1 255.255.255.0
            """;

        var result = _parser.Parse(config);
        var iface = Assert.IsType<NetworkDeviceConfig>(result.Config).Interfaces[0];

        Assert.Equal(InterfaceType.Gigabit, iface.InterfaceType);
    }

    [Fact]
    public void DetectLoopback()
    {
        const string config = """
            hostname router
            interface Loopback0
             ip address 1.1.1.1 255.255.255.255
            """;

        var result = _parser.Parse(config);
        var iface = Assert.IsType<NetworkDeviceConfig>(result.Config).Interfaces[0];

        Assert.Equal(InterfaceType.Loopback, iface.InterfaceType);
    }

    [Fact]
    public void DetectVlanInterface()
    {
        const string config = """
            hostname router
            interface Vlan100
             ip address 192.168.100.1 255.255.255.0
            """;

        var result = _parser.Parse(config);
        var iface = Assert.IsType<NetworkDeviceConfig>(result.Config).Interfaces[0];

        Assert.Equal(InterfaceType.Vlan, iface.InterfaceType);
    }
}
