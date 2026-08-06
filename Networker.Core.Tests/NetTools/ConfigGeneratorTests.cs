using Networker.Core.NetTools.Config;

namespace Networker.Core.Tests.NetTools;

public class ConfigGeneratorTests
{
    private static DeviceSpec BuildSpec() => new()
    {
        Hostname = "edge-01",
        DomainName = "corp.example",
        EnableSecret = "$9$hashed",
        Username = "admin",
        UsernameSecret = "$9$adminhash",
        SnmpCommunity = "n0t-public",
        LoggingHost = "10.99.0.5",
        NtpServer = "192.0.2.123",
        OspfProcessId = "1",
        RouterId = "10.0.0.1",
        BgpAsn = "65001",
        BgpNetworks = new[] { "10.10.0.0/16" },
        BgpRedistributeConnected = true,
        Vlans = new[]
        {
            new VlanSpec { Id = "10", Name = "users", InterfaceVlanIp = "192.168.10.1/24" },
            new VlanSpec { Id = "20", Name = "servers", InterfaceVlanIp = "192.168.20.1/24" },
        },
        Interfaces = new[]
        {
            new InterfaceSpec { Name = "GigabitEthernet0/1", Description = "To Core", Mode = "trunk", AllowedVlans = "10,20" },
            new InterfaceSpec { Name = "GigabitEthernet0/2", Mode = "access", Vlan = "10" },
            new InterfaceSpec { Name = "GigabitEthernet0/0", Mode = "routed", Ip = "203.0.113.1/30", Mtu = "1500" },
        },
        OspfAreas = new[]
        {
            new OspfAreaSpec("192.168.10.0/24", "0"),
            new OspfAreaSpec("192.168.20.0/24", "0"),
        },
        BgpNeighbors = new[]
        {
            new BgpNeighborSpec("203.0.113.2", "64512", "Transit"),
        },
        Acls = new[]
        {
            new AclEntrySpec { Name = "MGMT-IN", Action = "permit", Protocol = "tcp", Source = "10.0.0.0/8", Destination = "any", DestinationPort = "22" },
            new AclEntrySpec { Name = "MGMT-IN", Action = "deny", Protocol = "tcp", Source = "any", Destination = "any", DestinationPort = "23", Log = true },
        },
        Nat = new NatSpec
        {
            Inside = new[] { "GigabitEthernet0/2" },
            Outside = "GigabitEthernet0/0",
            AclName = "NAT-ACL",
        },
    };

    [Fact]
    public void CiscoIosXe_RendersCoreDirectives()
    {
        var config = ConfigGenerator.Generate(ConfigPlatform.CiscoIosXe, BuildSpec());

        Assert.Contains("hostname edge-01", config);
        Assert.Contains("enable secret $9$hashed", config);
        Assert.Contains("username admin privilege 15 secret $9$adminhash", config);
        Assert.Contains("vlan 10", config);
        Assert.Contains(" name users", config);
        Assert.Contains("interface Vlan10", config);
        Assert.Contains(" ip address 192.168.10.1 255.255.255.0", config);
        Assert.Contains("interface GigabitEthernet0/1", config);
        Assert.Contains(" switchport mode trunk", config);
        Assert.Contains(" switchport trunk allowed vlan 10,20", config);
        Assert.Contains("interface GigabitEthernet0/0", config);
        Assert.Contains(" ip address 203.0.113.1 255.255.255.252", config);
        Assert.Contains("router ospf 1", config);
        Assert.Contains(" network 192.168.10.0 0.0.0.255 area 0", config);
        Assert.Contains("router bgp 65001", config);
        Assert.Contains(" neighbor 203.0.113.2 remote-as 64512", config);
        Assert.Contains(" network 10.10.0.0 mask 255.255.0.0", config);
        Assert.Contains("ip access-list extended MGMT-IN", config);
        Assert.Contains("deny tcp any any eq 23 log", config);
        Assert.Contains("ip nat inside source list NAT-ACL interface overload", config);
        Assert.Contains("logging host 10.99.0.5", config);
        Assert.Contains("ntp server 192.0.2.123", config);
        Assert.Contains("end", config);
    }

    [Fact]
    public void CiscoIosXe_SectionsClosedAndEndsWithEnd()
    {
        var config = ConfigGenerator.Generate(ConfigPlatform.CiscoIosXe, BuildSpec());
        var lines = config.TrimEnd('\n', '\r').Split('\n').Select(l => l.TrimEnd('\r')).ToList();

        Assert.Equal("end", lines.Last().Trim());

        var starters = new[] { "interface ", "router ", "vlan ", "ip access-list" };
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (!starters.Any(s => line.TrimStart().StartsWith(s, StringComparison.Ordinal)))
            {
                continue;
            }

            var j = i + 1;
            while (j < lines.Count && lines[j].Trim().Length > 0 && lines[j].Trim() != "!")
            {
                j++;
            }

            Assert.True(
                j < lines.Count && lines[j].Trim() == "!",
                $"Section starting at '{line.Trim()}' must be closed with '!'");
        }
    }

    [Fact]
    public void JuniperJunos_RendersSetCommands()
    {
        var config = ConfigGenerator.Generate(ConfigPlatform.JuniperJunos, BuildSpec());

        Assert.Contains("set system host-name edge-01", config);
        Assert.Contains("set system domain-name corp.example", config);
        Assert.Contains("set system services ssh", config);
        Assert.Contains("set vlans users vlan-id 10", config);
        Assert.Contains("set vlans users l3-interface irb.10", config);
        Assert.Contains("set interfaces irb unit 10 family inet address 192.168.10.1/24", config);
        Assert.Contains("set interfaces GigabitEthernet0/1 unit 0 family ethernet-switching", config);
        Assert.Contains("set interfaces GigabitEthernet0/1 unit 0 family ethernet-switching interface-mode trunk", config);
        Assert.Contains("set interfaces GigabitEthernet0/0 unit 0 family inet address 203.0.113.1/30", config);
        Assert.Contains("set protocols ospf area 0 network 192.168.10.0/24", config);
        Assert.Contains("set protocols bgp local-as 65001", config);
        Assert.Contains("set protocols bgp group EXTERNAL neighbor 203.0.113.2 peer-as 64512", config);
        Assert.Contains("set policy-options prefix-list ADVERTISED 10.10.0.0/16", config);
        Assert.Contains("set snmp community n0t-public", config);
        Assert.Contains("set system ntp server 192.0.2.123", config);
    }

    [Fact]
    public void AristaEos_RendersDirectives()
    {
        var config = ConfigGenerator.Generate(ConfigPlatform.AristaEos, BuildSpec());

        Assert.Contains("hostname edge-01", config);
        Assert.Contains("vlan 10", config);
        Assert.Contains("   ip address 192.168.10.1/24", config);
        Assert.Contains("interface GigabitEthernet0/1", config);
        Assert.Contains("   switchport mode trunk", config);
        Assert.Contains("   no switchport", config);
        Assert.Contains("   ip address 203.0.113.1/30", config);
        Assert.Contains("router ospf 1", config);
        Assert.Contains("   network 192.168.10.0/24 area 0", config);
        Assert.Contains("router bgp 65001", config);
        Assert.Contains("   neighbor 203.0.113.2 remote-as 64512", config);
        Assert.Contains("   redistribute connected", config);
        Assert.Contains("ip access-list MGMT-IN", config);
        Assert.Contains("   10 permit tcp 10.0.0.0/8 any eq 22", config);
        Assert.Contains("   20 deny tcp any any eq 23 log", config);
        Assert.Contains("snmp-server community n0t-public ro", config);
    }

    [Fact]
    public void Vyos_RendersSetCommands()
    {
        var config = ConfigGenerator.Generate(ConfigPlatform.Vyos, BuildSpec());

        Assert.Contains("set system host-name 'edge-01'", config);
        Assert.Contains("set system login user admin authentication encrypted-password '$9$adminhash'", config);
        Assert.Contains("set service ssh", config);
        Assert.Contains("set interfaces ethernet GigabitEthernet0/1 vif 10 description 'users'", config);
        Assert.Contains("set interfaces ethernet GigabitEthernet0/1 vif 10 address 192.168.10.1/24", config);
        Assert.Contains("set interfaces ethernet GigabitEthernet0/0 address 203.0.113.1/30", config);
        Assert.Contains("set protocols ospf area 0 network 192.168.10.0/24", config);
        Assert.Contains("set protocols bgp 65001 neighbor 203.0.113.2 remote-as '64512'", config);
        Assert.Contains("set protocols bgp 65001 network 10.10.0.0/16", config);
        Assert.Contains("set service snmp community 'n0t-public'", config);
        Assert.Contains("set system ntp server 192.0.2.123", config);
    }

    [Fact]
    public void Generate_WithEmptySpec_ProducesMinimalConfig()
    {
        var spec = new DeviceSpec { Hostname = "bare" };
        var cisco = ConfigGenerator.Generate(ConfigPlatform.CiscoIosXe, spec);
        var junos = ConfigGenerator.Generate(ConfigPlatform.JuniperJunos, spec);

        Assert.Contains("hostname bare", cisco);
        Assert.Contains("set system host-name bare", junos);
        Assert.DoesNotContain("vlan ", cisco);
        Assert.DoesNotContain("router ospf", junos);
    }

    [Fact]
    public void Generate_UnknownPlatform_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ConfigGenerator.Generate((ConfigPlatform)99, new DeviceSpec()));
    }
}

