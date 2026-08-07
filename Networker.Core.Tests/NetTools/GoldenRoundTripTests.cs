using System.Net;
using Networker.Core.Models.NetworkConfig;
using Networker.Core.Services.NetworkConfig.Parsers;

namespace Networker.Core.Tests.NetTools;

/// <summary>
/// Round-trip tests: each golden vendor config (the exact bytes emitted by
/// the C# generator and frozen by <see cref="GoldenConfigTests"/>) is parsed
/// back through the ported parser, and the recovered model is verified.
///
/// Sections the Python parser never handled are asserted as absent/unparsed
/// to pin the faithful behavior: Junos firewall filters, SONiC OSPF_* tables,
/// Cisco prefix-lists/route-maps/VLAN state/static-route permanence, ACL
/// source/destination ports, interface speed/duplex, and EIGRP.
/// </summary>
public class GoldenRoundTripTests
{
    private readonly ConfigParserFactory _factory = new();

    private static string ReadGolden(string goldenFile) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "NetTools", "Golden", goldenFile));

    [Fact]
    public void CiscoIos_Golden_RoundTrips()
    {
        var result = _factory.DetectAndParse(ReadGolden("cisco_ios.txt"));
        Assert.Equal(Vendor.CiscoIos, result.Vendor);

        var config = Assert.IsType<NetworkDeviceConfig>(result.Config);

        Assert.Equal("core-router-01", config.Hostname);
        Assert.Equal("$1$secret$hash", config.EnableSecret);
        Assert.Equal("example.com", config.DomainName);
        Assert.Equal(new List<string> { "8.8.8.8", "8.8.4.4" }, config.DnsServers);
        Assert.Equal(new List<string> { "10.0.0.10", "10.0.0.11" }, config.NtpServers);
        Assert.Equal("Authorized access only", config.BannerMotd);

        // VLAN 100's "state suspended" is not parsed (faithful to Python).
        Assert.Collection(config.Vlans,
            v => { Assert.Equal(10, v.VlanId); Assert.Equal("USERS", v.Name); },
            v => { Assert.Equal(20, v.VlanId); Assert.Equal("SERVERS", v.Name); },
            v =>
            {
                Assert.Equal(100, v.VlanId);
                Assert.Equal("MGMT", v.Name);
                Assert.Equal("active", v.State);
            });

        Assert.Collection(config.Interfaces,
            i =>
            {
                Assert.Equal("GigabitEthernet0/0", i.Name);
                Assert.Equal(InterfaceType.Gigabit, i.InterfaceType);
                Assert.Equal("Uplink to core", i.Description);
                Assert.Equal(IPAddress.Parse("10.0.0.1"), i.IpAddress);
                Assert.Equal(IPAddress.Parse("255.255.255.252"), i.SubnetMask);
                Assert.True(i.Enabled);
                Assert.Null(i.SwitchportMode);
            },
            i =>
            {
                Assert.Equal("GigabitEthernet0/1", i.Name);
                Assert.Equal("Access port", i.Description);
                Assert.Equal(SwitchportMode.Access, i.SwitchportMode);
                Assert.Equal(10, i.AccessVlan);
                Assert.Equal(10, i.VlanId);
                Assert.True(i.Enabled);
            },
            i =>
            {
                Assert.Equal("GigabitEthernet0/2", i.Name);
                Assert.Equal(SwitchportMode.Trunk, i.SwitchportMode);
                Assert.True(i.IsTrunk);
                Assert.Equal(99, i.TrunkNativeVlan);
                Assert.Equal(99, i.NativeVlan);
                Assert.Equal("10,20,30", i.TrunkAllowedVlans);
                Assert.True(i.Enabled);
            },
            i =>
            {
                Assert.Equal("GigabitEthernet0/3", i.Name);
                Assert.Equal("Disabled port", i.Description);
                Assert.False(i.Enabled);
                Assert.Equal(SwitchportMode.Access, i.SwitchportMode);
                Assert.Equal(10, i.AccessVlan);
            },
            i =>
            {
                Assert.Equal("Loopback0", i.Name);
                Assert.Equal(InterfaceType.Loopback, i.InterfaceType);
                Assert.Equal(IPAddress.Parse("1.1.1.1"), i.IpAddress);
                Assert.Equal(IPAddress.Parse("255.255.255.255"), i.SubnetMask);
                Assert.True(i.Enabled);
            },
            i =>
            {
                Assert.Equal("GigabitEthernet1/0", i.Name);
                Assert.Equal(SwitchportMode.Trunk, i.SwitchportMode);
                Assert.True(i.IsTrunk);
                Assert.Equal(99, i.TrunkNativeVlan);
                Assert.Equal("10,20", i.TrunkAllowedVlans);
                Assert.Equal(1, i.ChannelGroup);
                Assert.True(i.Enabled);
            },
            i =>
            {
                Assert.Equal("GigabitEthernet1/1", i.Name);
                Assert.Equal(1, i.ChannelGroup);
                Assert.True(i.Enabled);
            },
            i =>
            {
                Assert.Equal("Vlan100", i.Name);
                Assert.Equal(InterfaceType.Vlan, i.InterfaceType);
                Assert.Equal(IPAddress.Parse("192.168.100.1"), i.IpAddress);
                Assert.Equal(IPAddress.Parse("255.255.255.0"), i.SubnetMask);
                Assert.True(i.Enabled);
            },
            i =>
            {
                Assert.Equal("Port-channel1", i.Name);
                Assert.Equal(InterfaceType.PortChannel, i.InterfaceType);
                Assert.Equal("LACP bundle", i.Description);
                Assert.Equal(IPAddress.Parse("10.99.0.1"), i.IpAddress);
                Assert.Equal(IPAddress.Parse("255.255.255.252"), i.SubnetMask);
                Assert.True(i.Enabled);
            },
            i =>
            {
                Assert.Equal("TenGigabitEthernet0/1", i.Name);
                // Python's "_detect_interface_type" checks "gigabit" in name
                // before "tengigabit", so this interface types as Gigabit
                // (faithful quirk).
                Assert.Equal(InterfaceType.Gigabit, i.InterfaceType);
                Assert.Equal("Dynamic mode", i.Description);
                // "dynamic auto" yields null switchport mode (faithful).
                Assert.Null(i.SwitchportMode);
                Assert.True(i.Enabled);
            },
            i =>
            {
                Assert.Equal("GigabitEthernet2/0", i.Name);
                Assert.Equal(IPAddress.Parse("192.168.200.1"), i.IpAddress);
                Assert.Equal(IPAddress.Parse("255.255.255.0"), i.SubnetMask);
                Assert.Equal(9000, i.Mtu);
                Assert.True(i.Enabled);
            });

        Assert.Collection(config.Acls,
            acl =>
            {
                Assert.Equal("EXT-ACL", acl.Name);
                Assert.True(acl.IsExtended);
                Assert.Collection(acl.Entries,
                    e =>
                    {
                        Assert.Equal(10, e.Sequence);
                        Assert.Equal(AclAction.Permit, e.Action);
                        Assert.Equal(AclProtocol.Tcp, e.Protocol);
                        Assert.Equal("10.0.0.0", e.Source);
                        Assert.Equal("0.0.0.255", e.SourceWildcard);
                        Assert.Equal("any", e.Destination);
                        Assert.True(e.Log);
                        // Ports are not parsed (faithful).
                        Assert.Null(e.DestinationPort);
                    },
                    e =>
                    {
                        Assert.Equal(20, e.Sequence);
                        Assert.Equal(AclAction.Deny, e.Action);
                        Assert.Equal(AclProtocol.Ip, e.Protocol);
                        Assert.Equal("any", e.Source);
                        // Python token-walk quirk: "any" fills source, so
                        // "192.168.100.0" lands in source-wildcard position
                        // and "0.0.0.255" becomes the destination.
                        Assert.Equal("192.168.100.0", e.SourceWildcard);
                        Assert.Equal("0.0.0.255", e.Destination);
                    },
                    e =>
                    {
                        Assert.Equal(30, e.Sequence);
                        Assert.Equal(AclAction.Permit, e.Action);
                        Assert.Equal(AclProtocol.Ip, e.Protocol);
                        Assert.Equal("any", e.Source);
                        Assert.Equal("0.0.0.0", e.SourceWildcard);
                        Assert.Equal("any", e.Destination);
                    });
            },
            acl =>
            {
                Assert.Equal("STD-ACL", acl.Name);
                Assert.False(acl.IsExtended);
                Assert.Collection(acl.Entries,
                    e =>
                    {
                        Assert.Equal(5, e.Sequence);
                        Assert.Equal(AclAction.Permit, e.Action);
                        Assert.Equal("10.1.0.0", e.Source);
                        Assert.Equal("0.0.255.255", e.SourceWildcard);
                        Assert.Equal("any", e.Destination);
                    },
                    e =>
                    {
                        Assert.Equal(15, e.Sequence);
                        Assert.Equal(AclAction.Deny, e.Action);
                        Assert.Equal("any", e.Source);
                        Assert.Equal("0.0.0.0", e.SourceWildcard);
                    },
                    e =>
                    {
                        Assert.Equal(1, e.Sequence);
                        Assert.Equal("management hosts", e.Remark);
                    });
            });

        Assert.Collection(config.StaticRoutes,
            r =>
            {
                Assert.Equal("0.0.0.0", r.Destination);
                Assert.Equal("0.0.0.0", r.Mask);
                Assert.Equal("10.0.0.2", r.NextHop);
                Assert.Equal(1, r.AdminDistance);
                Assert.Equal("default", r.Name);
                // "permanent" is not parsed (faithful).
                Assert.False(r.Permanent);
            },
            r =>
            {
                Assert.Equal("172.16.0.0", r.Destination);
                Assert.Equal("255.255.0.0", r.Mask);
                Assert.Equal("10.0.0.2", r.NextHop);
                Assert.Equal(10, r.AdminDistance);
            });

        // Prefix lists and route maps are not parsed (faithful).
        Assert.Empty(config.PrefixLists);
        Assert.Empty(config.RouteMaps);

        var ospf = Assert.IsType<OspfConfig>(config.Ospf);
        Assert.Equal(1, ospf.ProcessId);
        Assert.Equal("1.1.1.1", ospf.RouterId);
        Assert.Equal(1000, ospf.ReferenceBandwidth);
        Assert.Collection(ospf.Networks,
            n => { Assert.Equal("10.0.0.0", n.Network); Assert.Equal("0.0.0.255", n.Wildcard); Assert.Equal(0, n.Area); },
            n => { Assert.Equal("192.168.100.0", n.Network); Assert.Equal("0.0.0.255", n.Wildcard); Assert.Equal(10, n.Area); });
        Assert.Equal(new List<string> { "GigabitEthernet0/2" }, ospf.PassiveInterfaces);
        Assert.True(ospf.DefaultInformationOriginate);

        var bgp = Assert.IsType<BgpConfig>(config.Bgp);
        Assert.Equal(65000, bgp.LocalAs);
        Assert.Equal("1.1.1.1", bgp.RouterId);
        Assert.True(bgp.LogNeighborChanges);
        Assert.Equal(new List<string> { "10.0.0.0/8", "192.168.100.0/24" }, bgp.Networks);
        Assert.Collection(bgp.Neighbors,
            n =>
            {
                Assert.Equal("10.0.0.2", n.IpAddress);
                Assert.Equal(65001, n.RemoteAs);
                Assert.Equal("core-peer", n.Description);
                Assert.Equal("s3cret", n.Password);
                Assert.Equal("Loopback0", n.UpdateSource);
                Assert.Equal(2, n.EbgpMultihop);
                // route-map in/out is not parsed (faithful).
                Assert.Null(n.RouteMapIn);
            },
            n =>
            {
                Assert.Equal("10.0.1.2", n.IpAddress);
                Assert.Equal(65000, n.RemoteAs);
                Assert.Equal("ibgp-peer", n.Description);
                Assert.Equal(0, n.EbgpMultihop);
            });

        // EIGRP is not parsed (faithful).
        Assert.Null(config.Eigrp);
    }

    [Fact]
    public void JuniperJunos_Golden_RoundTrips()
    {
        var result = _factory.DetectAndParse(ReadGolden("juniper_junos.txt"));
        Assert.Equal(Vendor.JuniperJunos, result.Vendor);

        var config = Assert.IsType<NetworkDeviceConfig>(result.Config);

        Assert.Equal("core-router-01", config.Hostname);
        Assert.Equal("example.com", config.DomainName);
        Assert.Equal(new List<string> { "8.8.8.8", "8.8.4.4" }, config.DnsServers);
        Assert.Equal(new List<string> { "10.0.0.10", "10.0.0.11" }, config.NtpServers);
        Assert.Equal("Authorized access only", config.BannerMotd);
        // root-authentication encrypted-password is not parsed (faithful).
        Assert.Null(config.EnableSecret);

        Assert.Collection(config.Vlans,
            v => { Assert.Equal(10, v.VlanId); Assert.Equal("USERS", v.Name); },
            v => { Assert.Equal(20, v.VlanId); Assert.Equal("SERVERS", v.Name); },
            v => { Assert.Equal(100, v.VlanId); Assert.Equal("MGMT", v.Name); });

        // 10 real interfaces + 3 phantom blocks ("ether-options" x2 and one
        // "vlan" block inside ge-1/0's ethernet-switching family) that the
        // IfaceStartRegex picks up — faithful to the Python port.
        Assert.Collection(config.Interfaces,
            i =>
            {
                Assert.Equal("ge-0/0", i.Name);
                Assert.Equal(InterfaceType.Gigabit, i.InterfaceType);
                Assert.Equal("Uplink to core", i.Description);
                Assert.Equal(IPAddress.Parse("10.0.0.1"), i.IpAddress);
                Assert.Equal(IPAddress.Parse("255.255.255.252"), i.SubnetMask);
                Assert.True(i.Enabled);
            },
            i =>
            {
                Assert.Equal("ge-0/1", i.Name);
                Assert.Equal("Access port", i.Description);
                Assert.True(i.Enabled);
            },
            i =>
            {
                Assert.Equal("ge-0/2", i.Name);
                Assert.Equal(string.Empty, i.Description);
                Assert.True(i.Enabled);
            },
            i =>
            {
                Assert.Equal("ge-0/3", i.Name);
                Assert.Equal("Disabled port", i.Description);
                Assert.False(i.Enabled);
            },
            i =>
            {
                Assert.Equal("lo0", i.Name);
                Assert.Equal(InterfaceType.Loopback, i.InterfaceType);
                Assert.Equal(IPAddress.Parse("1.1.1.1"), i.IpAddress);
                Assert.Equal(IPAddress.Parse("255.255.255.255"), i.SubnetMask);
                Assert.True(i.Enabled);
            },
            i =>
            {
                Assert.Equal("ge-1/0", i.Name);
                Assert.Equal(1, i.ChannelGroup);
                Assert.True(i.Enabled);
            },
            i =>
            {
                // Phantom block: "ether-options { 802.3ad ae1; }" parsed as an
                // interface. Python's "_detect_interface_type" checks
                // startswith("et-"), which "ether-options" does not match, so
                // it falls through to the Ethernet default (faithful).
                Assert.Equal("ether-options", i.Name);
                Assert.Equal(InterfaceType.Ethernet, i.InterfaceType);
                Assert.Equal(1, i.ChannelGroup);
            },
            i =>
            {
                // Phantom block: "vlan { members [ 10,20 ]; }".
                Assert.Equal("vlan", i.Name);
                Assert.Equal(InterfaceType.Vlan, i.InterfaceType);
                Assert.True(i.Enabled);
            },
            i =>
            {
                Assert.Equal("ge-1/1", i.Name);
                Assert.Equal(1, i.ChannelGroup);
                Assert.True(i.Enabled);
            },
            i =>
            {
                Assert.Equal("ether-options", i.Name);
                Assert.Equal(InterfaceType.Ethernet, i.InterfaceType);
                Assert.Equal(1, i.ChannelGroup);
            },
            i =>
            {
                Assert.Equal("ae1", i.Name);
                Assert.Equal(InterfaceType.PortChannel, i.InterfaceType);
                Assert.Equal("LACP bundle", i.Description);
                Assert.Equal(IPAddress.Parse("10.99.0.1"), i.IpAddress);
                Assert.Equal(IPAddress.Parse("255.255.255.252"), i.SubnetMask);
                Assert.True(i.Enabled);
            },
            i =>
            {
                Assert.Equal("xe-0/1", i.Name);
                Assert.Equal(InterfaceType.TenGigabit, i.InterfaceType);
                Assert.Equal("Dynamic mode", i.Description);
                Assert.True(i.Enabled);
            },
            i =>
            {
                Assert.Equal("ge-2/0", i.Name);
                Assert.Equal(IPAddress.Parse("192.168.200.1"), i.IpAddress);
                Assert.Equal(IPAddress.Parse("255.255.255.0"), i.SubnetMask);
                Assert.Equal(9000, i.Mtu);
                Assert.True(i.Enabled);
            });

        // firewall filters are not parsed (faithful).
        Assert.Empty(config.Acls);

        // "preference 10" on the second route is not parsed (faithful), so
        // both routes carry the default admin distance.
        Assert.Collection(config.StaticRoutes,
            r =>
            {
                Assert.Equal("0.0.0.0", r.Destination);
                Assert.Equal("0.0.0.0", r.Mask);
                Assert.Equal("10.0.0.2", r.NextHop);
                Assert.Equal(1, r.AdminDistance);
            },
            r =>
            {
                Assert.Equal("172.16.0.0", r.Destination);
                Assert.Equal("255.255.0.0", r.Mask);
                Assert.Equal("10.0.0.2", r.NextHop);
                Assert.Equal(1, r.AdminDistance);
            });

        // OSPF/BGP are found via direct search of the config text (a
        // documented deviation: the C# generator emits one "protocols" block
        // per protocol).
        var ospf = Assert.IsType<OspfConfig>(config.Ospf);
        Assert.Equal(0, ospf.ProcessId);
        Assert.Equal("1.1.1.1", ospf.RouterId);
        Assert.Equal(1000, ospf.ReferenceBandwidth);
        Assert.Collection(ospf.Networks,
            n => { Assert.Equal("0.0.0.0", n.Network); Assert.Equal("0.0.0.0", n.Wildcard); Assert.Equal(0, n.Area); },
            n => { Assert.Equal("0.0.0.0", n.Network); Assert.Equal("0.0.0.0", n.Wildcard); Assert.Equal(10, n.Area); });
        Assert.Empty(ospf.PassiveInterfaces);
        Assert.False(ospf.DefaultInformationOriginate);

        var bgp = Assert.IsType<BgpConfig>(config.Bgp);
        Assert.Equal(65000, bgp.LocalAs);
        Assert.Equal("1.1.1.1", bgp.RouterId);
        Assert.Collection(bgp.Neighbors,
            n =>
            {
                Assert.Equal("10.0.0.2", n.IpAddress);
                Assert.Equal(65001, n.RemoteAs);
                Assert.Equal("core_peer", n.Description);
                Assert.Equal("s3cret", n.Password);
                Assert.Equal("Loopback0", n.UpdateSource);
                Assert.Equal(2, n.EbgpMultihop);
            },
            n =>
            {
                Assert.Equal("10.0.1.2", n.IpAddress);
                // Internal group has no peer-as, so remote AS defaults to 0.
                Assert.Equal(0, n.RemoteAs);
                Assert.Equal("ibgp_peer", n.Description);
                Assert.Equal(0, n.EbgpMultihop);
            });
    }

    [Fact]
    public void Sonic_Golden_RoundTrips()
    {
        var result = _factory.DetectAndParse(ReadGolden("sonic.txt"));
        Assert.Equal(Vendor.Sonic, result.Vendor);

        var config = Assert.IsType<NetworkDeviceConfig>(result.Config);

        Assert.Equal("core-router-01", config.Hostname);

        Assert.Collection(config.Interfaces,
            i =>
            {
                Assert.Equal("Ethernet0", i.Name);
                Assert.Equal(InterfaceType.Ethernet, i.InterfaceType);
                Assert.Equal("Uplink to core", i.Description);
                Assert.Equal(IPAddress.Parse("10.0.0.1"), i.IpAddress);
                Assert.Equal(IPAddress.Parse("255.255.255.252"), i.SubnetMask);
                Assert.True(i.Enabled);
                Assert.Equal(9100, i.Mtu);
                Assert.Equal("1000", i.Speed);
            },
            i =>
            {
                Assert.Equal("Ethernet1", i.Name);
                // Duplicate PORT key: last value wins ("Dynamic mode").
                Assert.Equal("Dynamic mode", i.Description);
                Assert.Equal(10, i.VlanId);
                Assert.Equal(SwitchportMode.Access, i.SwitchportMode);
                Assert.True(i.Enabled);
                Assert.Equal(9100, i.Mtu);
            },
            i =>
            {
                Assert.Equal("Ethernet2", i.Name);
                Assert.Equal(string.Empty, i.Description);
                Assert.True(i.Enabled);
                Assert.Equal(9100, i.Mtu);
            },
            i =>
            {
                Assert.Equal("Ethernet3", i.Name);
                Assert.Equal("Disabled port", i.Description);
                Assert.False(i.Enabled);
                Assert.Equal(10, i.VlanId);
                Assert.Equal(SwitchportMode.Access, i.SwitchportMode);
                Assert.Equal(9100, i.Mtu);
            },
            i =>
            {
                Assert.Equal("Ethernet48", i.Name);
                Assert.True(i.IsTrunk);
                Assert.Equal(SwitchportMode.Trunk, i.SwitchportMode);
                Assert.Equal("10,20", i.TrunkAllowedVlans);
                Assert.True(i.Enabled);
                Assert.Equal(9100, i.Mtu);
            },
            i =>
            {
                Assert.Equal("Ethernet49", i.Name);
                Assert.True(i.Enabled);
                Assert.Equal(9100, i.Mtu);
                Assert.Null(i.VlanId);
            },
            i =>
            {
                Assert.Equal("Ethernet96", i.Name);
                Assert.Equal(IPAddress.Parse("192.168.200.1"), i.IpAddress);
                Assert.Equal(IPAddress.Parse("255.255.255.0"), i.SubnetMask);
                Assert.True(i.Enabled);
                Assert.Equal(9000, i.Mtu);
            },
            i =>
            {
                Assert.Equal("PortChannel0001", i.Name);
                Assert.Equal(InterfaceType.PortChannel, i.InterfaceType);
                Assert.Equal(IPAddress.Parse("10.99.0.1"), i.IpAddress);
                Assert.Equal(IPAddress.Parse("255.255.255.252"), i.SubnetMask);
                Assert.True(i.Enabled);
            },
            i =>
            {
                Assert.Equal("Loopback0", i.Name);
                Assert.Equal(InterfaceType.Loopback, i.InterfaceType);
                Assert.Equal(IPAddress.Parse("1.1.1.1"), i.IpAddress);
                Assert.Equal(IPAddress.Parse("255.255.255.255"), i.SubnetMask);
                Assert.True(i.Enabled);
            },
            i =>
            {
                Assert.Equal("Vlan100", i.Name);
                Assert.Equal(InterfaceType.Vlan, i.InterfaceType);
                Assert.Equal(IPAddress.Parse("192.168.100.1"), i.IpAddress);
                Assert.Equal(IPAddress.Parse("255.255.255.0"), i.SubnetMask);
                Assert.True(i.Enabled);
            });

        Assert.Collection(config.Vlans,
            v => { Assert.Equal(10, v.VlanId); Assert.Equal("Vlan10", v.Name); },
            v => { Assert.Equal(20, v.VlanId); Assert.Equal("Vlan20", v.Name); },
            v => { Assert.Equal(100, v.VlanId); Assert.Equal("Vlan100", v.Name); });

        Assert.Collection(config.Acls,
            acl =>
            {
                Assert.Equal("EXT-ACL", acl.Name);
                Assert.True(acl.IsExtended);
                Assert.Collection(acl.Entries,
                    e =>
                    {
                        Assert.Equal(10, e.Sequence);
                        Assert.Equal(AclAction.Permit, e.Action);
                        Assert.Equal(AclProtocol.Tcp, e.Protocol);
                        Assert.Equal("10.0.0.0", e.Source);
                        Assert.Equal("0.0.0.255", e.SourceWildcard);
                        Assert.Equal("any", e.Destination);
                        Assert.Equal("443", e.DestinationPort);
                    },
                    e =>
                    {
                        Assert.Equal(20, e.Sequence);
                        Assert.Equal(AclAction.Deny, e.Action);
                        Assert.Equal(AclProtocol.Ip, e.Protocol);
                        Assert.Equal("any", e.Source);
                        Assert.Equal("192.168.100.0", e.Destination);
                        Assert.Equal("0.0.0.255", e.DestinationWildcard);
                    },
                    e =>
                    {
                        Assert.Equal(30, e.Sequence);
                        Assert.Equal(AclAction.Permit, e.Action);
                        Assert.Equal(AclProtocol.Ip, e.Protocol);
                        Assert.Equal("any", e.Source);
                        Assert.Equal("any", e.Destination);
                    });
            },
            acl =>
            {
                Assert.Equal("STD-ACL", acl.Name);
                Assert.True(acl.IsExtended);
                Assert.Collection(acl.Entries,
                    e =>
                    {
                        Assert.Equal(5, e.Sequence);
                        Assert.Equal(AclAction.Permit, e.Action);
                        Assert.Equal("10.1.0.0", e.Source);
                        Assert.Equal("0.0.255.255", e.SourceWildcard);
                    },
                    e =>
                    {
                        Assert.Equal(15, e.Sequence);
                        Assert.Equal(AclAction.Deny, e.Action);
                        Assert.Equal("any", e.Source);
                    });
            });

        Assert.Collection(config.StaticRoutes,
            r =>
            {
                Assert.Equal("0.0.0.0", r.Destination);
                Assert.Equal("0.0.0.0", r.Mask);
                Assert.Equal("10.0.0.2", r.NextHop);
                Assert.Equal(1, r.AdminDistance);
            },
            r =>
            {
                Assert.Equal("172.16.0.0", r.Destination);
                Assert.Equal("255.255.0.0", r.Mask);
                Assert.Equal("10.0.0.2", r.NextHop);
                Assert.Equal(10, r.AdminDistance);
            });

        Assert.Equal(new List<string> { "8.8.8.8", "8.8.4.4" }, config.DnsServers);
        Assert.Equal(new List<string> { "10.0.0.10", "10.0.0.11" }, config.NtpServers);

        var bgp = Assert.IsType<BgpConfig>(config.Bgp);
        Assert.Equal(65000, bgp.LocalAs);
        Assert.Collection(bgp.Neighbors,
            n =>
            {
                Assert.Equal("10.0.0.2", n.IpAddress);
                Assert.Equal(65001, n.RemoteAs);
                Assert.Equal("core-peer", n.Description);
            },
            n =>
            {
                Assert.Equal("10.0.1.2", n.IpAddress);
                Assert.Equal(65000, n.RemoteAs);
                Assert.Equal("ibgp-peer", n.Description);
            });

        // OSPF_* tables are not parsed (faithful).
        Assert.Null(config.Ospf);
        Assert.Null(config.Eigrp);
    }
}
