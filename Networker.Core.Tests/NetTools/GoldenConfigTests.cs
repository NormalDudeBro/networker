using System.Net;
using Networker.Core.Models.NetworkConfig;
using Networker.Core.Services.NetworkConfig;

namespace Networker.Core.Tests.NetTools;

/// <summary>
/// Byte-for-byte golden tests: renders the sample config for each vendor and
/// compares against reference output captured from the Python NetworkConfigPro
/// generator.
/// </summary>
public class GoldenConfigTests
{
    [Theory]
    [InlineData(Vendor.CiscoIos, "cisco_ios.txt")]
    [InlineData(Vendor.CiscoNxos, "cisco_nxos.txt")]
    [InlineData(Vendor.AristaEos, "arista_eos.txt")]
    [InlineData(Vendor.JuniperJunos, "juniper_junos.txt")]
    [InlineData(Vendor.Sonic, "sonic.txt")]
    [InlineData(Vendor.FortinetFortigate, "fortinet_fortigate.txt")]
    public void Generate_MatchesGoldenOutput(Vendor vendor, string goldenFile)
    {
        var generator = new NetworkConfigGenerator();
        var config = BuildSampleConfig() with { Vendor = vendor };

        var actual = generator.Generate(config);
        var expected = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "NetTools", "Golden", goldenFile));

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(Vendor.CiscoIos, "cisco_ios.txt")]
    [InlineData(Vendor.CiscoNxos, "cisco_nxos.txt")]
    [InlineData(Vendor.AristaEos, "arista_eos.txt")]
    [InlineData(Vendor.JuniperJunos, "juniper_junos.txt")]
    [InlineData(Vendor.Sonic, "sonic.txt")]
    [InlineData(Vendor.FortinetFortigate, "fortinet_fortigate.txt")]
    public void GenerateFromDict_MatchesGoldenOutput(Vendor vendor, string goldenFile)
    {
        var generator = new NetworkConfigGenerator();
        var dict = BuildSampleDict();

        var actual = generator.GenerateFromDict(vendor, dict);
        var expected = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "NetTools", "Golden", goldenFile));

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void GetSupportedVendors_ReturnsAllSix()
    {
        var generator = new NetworkConfigGenerator();

        Assert.Equal(6, generator.GetSupportedVendors().Count);
        Assert.Contains(Vendor.CiscoIos, generator.GetSupportedVendors());
        Assert.Contains(Vendor.CiscoNxos, generator.GetSupportedVendors());
        Assert.Contains(Vendor.AristaEos, generator.GetSupportedVendors());
        Assert.Contains(Vendor.JuniperJunos, generator.GetSupportedVendors());
        Assert.Contains(Vendor.Sonic, generator.GetSupportedVendors());
        Assert.Contains(Vendor.FortinetFortigate, generator.GetSupportedVendors());
    }

    /// <summary>
    /// Performance smoke test: generating one config per vendor must stay well
    /// under the plan's 500ms budget (typical wall time is a few milliseconds).
    /// A generous bound guards against pathological regressions without being
    /// flaky on slow CI machines.
    /// </summary>
    [Fact]
    public void Generate_AllVendors_CompletesWithinTimeBudget()
    {
        var generator = new NetworkConfigGenerator();
        var vendors = new[]
        {
            Vendor.CiscoIos,
            Vendor.CiscoNxos,
            Vendor.AristaEos,
            Vendor.JuniperJunos,
            Vendor.Sonic,
            Vendor.FortinetFortigate,
        };

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        foreach (var vendor in vendors)
        {
            generator.Generate(BuildSampleConfig() with { Vendor = vendor });
        }
        stopwatch.Stop();

        Assert.True(
            stopwatch.ElapsedMilliseconds < 500,
            $"All-vendor generation took {stopwatch.ElapsedMilliseconds}ms (budget is 500ms).");
    }

    private static NetworkDeviceConfig BuildSampleConfig() => new()
    {
        Hostname = "core-router-01",
        Vendor = Vendor.CiscoIos,
        Interfaces = new List<Interface>
        {
            new()
            {
                Name = "GigabitEthernet0/0",
                InterfaceType = InterfaceType.Gigabit,
                Description = "Uplink to core",
                IpAddress = IPAddress.Parse("10.0.0.1"),
                SubnetMask = IPAddress.Parse("255.255.255.252"),
                Enabled = true,
                Speed = "1000",
                Duplex = "full",
                Mtu = 1500,
            },
            new()
            {
                Name = "GigabitEthernet0/1",
                InterfaceType = InterfaceType.Gigabit,
                Description = "Access port",
                SwitchportMode = SwitchportMode.Access,
                AccessVlan = 10,
                VoiceVlan = 110,
                Enabled = true,
            },
            new()
            {
                Name = "GigabitEthernet0/2",
                InterfaceType = InterfaceType.Gigabit,
                SwitchportMode = SwitchportMode.Trunk,
                TrunkNativeVlan = 99,
                TrunkAllowedVlans = "10,20,30",
                Enabled = true,
            },
            new()
            {
                Name = "GigabitEthernet0/3",
                InterfaceType = InterfaceType.Gigabit,
                Description = "Disabled port",
                Enabled = false,
                SwitchportMode = SwitchportMode.Access,
                AccessVlan = 10,
            },
            new()
            {
                Name = "Loopback0",
                InterfaceType = InterfaceType.Loopback,
                IpAddress = IPAddress.Parse("1.1.1.1"),
                SubnetMask = IPAddress.Parse("255.255.255.255"),
                Enabled = true,
            },
            new()
            {
                Name = "GigabitEthernet1/0",
                InterfaceType = InterfaceType.Gigabit,
                IsTrunk = true,
                NativeVlan = 99,
                TrunkAllowedVlans = "10,20",
                Enabled = true,
                ChannelGroup = 1,
                ChannelGroupMode = "active",
            },
            new()
            {
                Name = "GigabitEthernet1/1",
                InterfaceType = InterfaceType.Gigabit,
                Enabled = true,
                ChannelGroup = 1,
                ChannelGroupMode = "active",
            },
            new()
            {
                Name = "Vlan100",
                InterfaceType = InterfaceType.Vlan,
                IpAddress = IPAddress.Parse("192.168.100.1"),
                SubnetMask = IPAddress.Parse("255.255.255.0"),
                Enabled = true,
            },
            new()
            {
                Name = "Port-channel1",
                InterfaceType = InterfaceType.PortChannel,
                Description = "LACP bundle",
                IpAddress = IPAddress.Parse("10.99.0.1"),
                SubnetMask = IPAddress.Parse("255.255.255.252"),
                Enabled = true,
            },
            new()
            {
                Name = "TenGigabitEthernet0/1",
                InterfaceType = InterfaceType.TenGigabit,
                Description = "Dynamic mode",
                SwitchportMode = SwitchportMode.DynamicAuto,
                Enabled = true,
            },
            new()
            {
                Name = "GigabitEthernet2/0",
                InterfaceType = InterfaceType.Gigabit,
                IpAddress = IPAddress.Parse("192.168.200.1"),
                SubnetMask = IPAddress.Parse("255.255.255.0"),
                Mtu = 9000,
                Enabled = true,
            },
        },
        Vlans = new List<Vlan>
        {
            new() { VlanId = 10, Name = "USERS", Description = "User segment" },
            new() { VlanId = 20, Name = "SERVERS" },
            new() { VlanId = 100, Name = "MGMT", State = "suspended" },
        },
        Acls = new List<Acl>
        {
            new()
            {
                Name = "EXT-ACL",
                Entries = new List<AclEntry>
                {
                    new()
                    {
                        Sequence = 10,
                        Action = AclAction.Permit,
                        Protocol = AclProtocol.Tcp,
                        Source = "10.0.0.0",
                        SourceWildcard = "0.0.0.255",
                        Destination = "any",
                        DestinationPort = "443",
                        Log = true,
                    },
                    new()
                    {
                        Sequence = 20,
                        Action = AclAction.Deny,
                        Protocol = AclProtocol.Ip,
                        Source = "any",
                        Destination = "192.168.100.0",
                        DestinationWildcard = "0.0.0.255",
                    },
                    new()
                    {
                        Sequence = 30,
                        Action = AclAction.Permit,
                        Protocol = AclProtocol.Ip,
                        Source = "any",
                        Destination = "any",
                    },
                },
            },
            new()
            {
                Name = "STD-ACL",
                IsExtended = false,
                Entries = new List<AclEntry>
                {
                    new()
                    {
                        Sequence = 5,
                        Action = AclAction.Permit,
                        Protocol = AclProtocol.Ip,
                        Source = "10.1.0.0",
                        SourceWildcard = "0.0.255.255",
                        Destination = "any",
                    },
                    new()
                    {
                        Sequence = 15,
                        Action = AclAction.Deny,
                        Protocol = AclProtocol.Ip,
                        Source = "any",
                        Destination = "any",
                    },
                    new()
                    {
                        Sequence = 1,
                        Action = AclAction.Permit,
                        Protocol = AclProtocol.Ip,
                        Source = "any",
                        Destination = "any",
                        Remark = "management hosts",
                    },
                },
            },
        },
        StaticRoutes = new List<StaticRoute>
        {
            new()
            {
                Destination = "0.0.0.0",
                Mask = "0.0.0.0",
                NextHop = "10.0.0.2",
                Name = "default",
                Permanent = true,
            },
            new()
            {
                Destination = "172.16.0.0",
                Mask = "255.255.0.0",
                NextHop = "10.0.0.2",
                AdminDistance = 10,
            },
        },
        PrefixLists = new List<PrefixList>
        {
            new()
            {
                Name = "PL-INTERNAL",
                Entries = new List<PrefixListEntry>
                {
                    new() { Sequence = 10, Action = "permit", Prefix = "10.0.0.0/8", Ge = 16, Le = 24 },
                    new() { Sequence = 20, Action = "deny", Prefix = "0.0.0.0/0" },
                },
            },
        },
        RouteMaps = new List<RouteMap>
        {
            new()
            {
                Name = "RM-INTERNAL",
                Entries = new List<RouteMapEntry>
                {
                    new()
                    {
                        Sequence = 10,
                        Action = "permit",
                        MatchPrefixList = "PL-INTERNAL",
                        SetLocalPref = 150,
                        SetMed = 50,
                        SetCommunity = "65000:100",
                    },
                },
            },
        },
        Ospf = new OspfConfig
        {
            ProcessId = 1,
            RouterId = "1.1.1.1",
            Networks = new List<OspfNetwork>
            {
                new() { Network = "10.0.0.0", Wildcard = "0.0.0.255", Area = 0 },
                new() { Network = "192.168.100.0", Wildcard = "0.0.0.255", Area = 10 },
            },
            PassiveInterfaces = new List<string> { "GigabitEthernet0/2" },
            DefaultInformationOriginate = true,
            ReferenceBandwidth = 1000,
        },
        Eigrp = new EigrpConfig
        {
            AsNumber = 100,
            RouterId = "1.1.1.1",
            Networks = new List<EigrpNetwork>
            {
                new() { Network = "10.0.0.0", Wildcard = "0.0.0.255" },
                new() { Network = "192.168.100.0" },
            },
            PassiveInterfaces = new List<string> { "GigabitEthernet0/2" },
            Redistribute = new List<string> { "connected", "static" },
            NamedMode = true,
            Name = "CORE_EIGRP",
        },
        Bgp = new BgpConfig
        {
            LocalAs = 65000,
            RouterId = "1.1.1.1",
            Networks = new List<string> { "10.0.0.0/8", "192.168.100.0/24" },
            Neighbors = new List<BgpNeighbor>
            {
                new()
                {
                    IpAddress = "10.0.0.2",
                    RemoteAs = 65001,
                    Description = "core-peer",
                    UpdateSource = "Loopback0",
                    EbgpMultihop = 2,
                    RouteMapIn = "RM-INTERNAL",
                    Password = "s3cret",
                },
                new()
                {
                    IpAddress = "10.0.1.2",
                    RemoteAs = 65000,
                    Description = "ibgp-peer",
                },
            },
            Redistribute = new List<string> { "connected", "static" },
        },
        Stp = new StpConfig
        {
            Mode = StpMode.RapidPvst,
            Priority = 8192,
            RootPrimaryVlans = new List<int> { 10, 20 },
            RootSecondaryVlans = new List<int> { 30 },
            PortfastDefault = true,
            BpduguardDefault = true,
        },
        EnableSecret = "5 $1$secret$hash",
        DomainName = "example.com",
        DnsServers = new List<string> { "8.8.8.8", "8.8.4.4" },
        NtpServers = new List<string> { "10.0.0.10", "10.0.0.11" },
        BannerMotd = "Authorized access only",
    };

    private static IReadOnlyDictionary<string, object> BuildSampleDict() => new Dictionary<string, object>
    {
        ["hostname"] = "core-router-01",
        ["interfaces"] = new List<object>
        {
            Dict(
                ("name", "GigabitEthernet0/0"), ("interface_type", "gigabit"),
                ("description", "Uplink to core"), ("ip_address", "10.0.0.1"),
                ("subnet_mask", "255.255.255.252"), ("enabled", true),
                ("speed", "1000"), ("duplex", "full"), ("mtu", 1500)),
            Dict(
                ("name", "GigabitEthernet0/1"), ("interface_type", "gigabit"),
                ("description", "Access port"), ("switchport_mode", "access"),
                ("access_vlan", 10), ("voice_vlan", 110), ("enabled", true)),
            Dict(
                ("name", "GigabitEthernet0/2"), ("interface_type", "gigabit"),
                ("switchport_mode", "trunk"), ("trunk_native_vlan", 99),
                ("trunk_allowed_vlans", "10,20,30"), ("enabled", true)),
            Dict(
                ("name", "GigabitEthernet0/3"), ("interface_type", "gigabit"),
                ("description", "Disabled port"), ("enabled", false),
                ("switchport_mode", "access"), ("access_vlan", 10)),
            Dict(
                ("name", "Loopback0"), ("interface_type", "loopback"),
                ("ip_address", "1.1.1.1"), ("subnet_mask", "255.255.255.255"),
                ("enabled", true)),
            Dict(
                ("name", "GigabitEthernet1/0"), ("interface_type", "gigabit"),
                ("is_trunk", true), ("native_vlan", 99),
                ("trunk_allowed_vlans", "10,20"), ("enabled", true),
                ("channel_group", 1), ("channel_group_mode", "active")),
            Dict(
                ("name", "GigabitEthernet1/1"), ("interface_type", "gigabit"),
                ("enabled", true), ("channel_group", 1),
                ("channel_group_mode", "active")),
            Dict(
                ("name", "Vlan100"), ("interface_type", "vlan"),
                ("ip_address", "192.168.100.1"), ("subnet_mask", "255.255.255.0"),
                ("enabled", true)),
            Dict(
                ("name", "Port-channel1"), ("interface_type", "port_channel"),
                ("description", "LACP bundle"), ("ip_address", "10.99.0.1"),
                ("subnet_mask", "255.255.255.252"), ("enabled", true)),
            Dict(
                ("name", "TenGigabitEthernet0/1"), ("interface_type", "ten_gigabit"),
                ("description", "Dynamic mode"), ("switchport_mode", "dynamic_auto"),
                ("enabled", true)),
            Dict(
                ("name", "GigabitEthernet2/0"), ("interface_type", "gigabit"),
                ("ip_address", "192.168.200.1"), ("subnet_mask", "255.255.255.0"),
                ("mtu", 9000), ("enabled", true)),
        },
        ["vlans"] = new List<object>
        {
            Dict(("vlan_id", 10), ("name", "USERS"), ("description", "User segment")),
            Dict(("vlan_id", 20), ("name", "SERVERS")),
            Dict(("vlan_id", 100), ("name", "MGMT"), ("state", "suspended")),
        },
        ["acls"] = new List<object>
        {
            Dict(
                ("name", "EXT-ACL"),
                ("entries", new List<object>
                {
                    Dict(
                        ("sequence", 10), ("action", "permit"), ("protocol", "tcp"),
                        ("source", "10.0.0.0"), ("source_wildcard", "0.0.0.255"),
                        ("destination", "any"), ("destination_port", "443"),
                        ("log", true)),
                    Dict(
                        ("sequence", 20), ("action", "deny"), ("protocol", "ip"),
                        ("source", "any"), ("destination", "192.168.100.0"),
                        ("destination_wildcard", "0.0.0.255")),
                    Dict(
                        ("sequence", 30), ("action", "permit"), ("protocol", "ip"),
                        ("source", "any"), ("destination", "any")),
                })),
            Dict(
                ("name", "STD-ACL"), ("is_extended", false),
                ("entries", new List<object>
                {
                    Dict(
                        ("sequence", 5), ("action", "permit"), ("protocol", "ip"),
                        ("source", "10.1.0.0"), ("source_wildcard", "0.0.255.255"),
                        ("destination", "any")),
                    Dict(
                        ("sequence", 15), ("action", "deny"), ("protocol", "ip"),
                        ("source", "any"), ("destination", "any")),
                    Dict(
                        ("sequence", 1), ("action", "permit"), ("protocol", "ip"),
                        ("source", "any"), ("destination", "any"),
                        ("remark", "management hosts")),
                })),
        },
        ["static_routes"] = new List<object>
        {
            Dict(
                ("destination", "0.0.0.0"), ("mask", "0.0.0.0"),
                ("next_hop", "10.0.0.2"), ("name", "default"), ("permanent", true)),
            Dict(
                ("destination", "172.16.0.0"), ("mask", "255.255.0.0"),
                ("next_hop", "10.0.0.2"), ("admin_distance", 10)),
        },
        ["prefix_lists"] = new List<object>
        {
            Dict(
                ("name", "PL-INTERNAL"),
                ("entries", new List<object>
                {
                    Dict(
                        ("sequence", 10), ("action", "permit"), ("prefix", "10.0.0.0/8"),
                        ("ge", 16), ("le", 24)),
                    Dict(("sequence", 20), ("action", "deny"), ("prefix", "0.0.0.0/0")),
                })),
        },
        ["route_maps"] = new List<object>
        {
            Dict(
                ("name", "RM-INTERNAL"),
                ("entries", new List<object>
                {
                    Dict(
                        ("sequence", 10), ("action", "permit"),
                        ("match_prefix_list", "PL-INTERNAL"),
                        ("set_local_pref", 150), ("set_med", 50),
                        ("set_community", "65000:100")),
                })),
        },
        ["ospf"] = Dict(
            ("process_id", 1), ("router_id", "1.1.1.1"),
            ("networks", new List<object>
            {
                Dict(("network", "10.0.0.0"), ("wildcard", "0.0.0.255"), ("area", 0)),
                Dict(("network", "192.168.100.0"), ("wildcard", "0.0.0.255"), ("area", 10)),
            }),
            ("passive_interfaces", new List<object> { "GigabitEthernet0/2" }),
            ("default_information_originate", true), ("reference_bandwidth", 1000)),
        ["eigrp"] = Dict(
            ("as_number", 100), ("router_id", "1.1.1.1"),
            ("networks", new List<object>
            {
                Dict(("network", "10.0.0.0"), ("wildcard", "0.0.0.255")),
                Dict(("network", "192.168.100.0")),
            }),
            ("passive_interfaces", new List<object> { "GigabitEthernet0/2" }),
            ("redistribute", new List<object> { "connected", "static" }),
            ("named_mode", true), ("name", "CORE_EIGRP")),
        ["bgp"] = Dict(
            ("local_as", 65000), ("router_id", "1.1.1.1"),
            ("networks", new List<object> { "10.0.0.0/8", "192.168.100.0/24" }),
            ("neighbors", new List<object>
            {
                Dict(
                    ("ip_address", "10.0.0.2"), ("remote_as", 65001),
                    ("description", "core-peer"), ("update_source", "Loopback0"),
                    ("ebgp_multihop", 2), ("route_map_in", "RM-INTERNAL"),
                    ("password", "s3cret")),
                Dict(("ip_address", "10.0.1.2"), ("remote_as", 65000), ("description", "ibgp-peer")),
            }),
            ("redistribute", new List<object> { "connected", "static" })),
        ["stp"] = Dict(
            ("mode", "rapid_pvst"), ("priority", 8192),
            ("root_primary_vlans", new List<object> { 10, 20 }),
            ("root_secondary_vlans", new List<object> { 30 }),
            ("portfast_default", true), ("bpduguard_default", true)),
        ["enable_secret"] = "5 $1$secret$hash",
        ["domain_name"] = "example.com",
        ["dns_servers"] = new List<object> { "8.8.8.8", "8.8.4.4" },
        ["ntp_servers"] = new List<object> { "10.0.0.10", "10.0.0.11" },
        ["banner_motd"] = "Authorized access only",
    };

    private static Dictionary<string, object> Dict(params (string Key, object Value)[] entries)
    {
        var dict = new Dictionary<string, object>();
        foreach (var (key, value) in entries)
        {
            dict[key] = value;
        }

        return dict;
    }
}
