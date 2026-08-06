using Networker.Core.NetTools.Config;

namespace Networker.Core.Tests.NetTools;

public class ConfigTranslatorTests
{
    private const string IosConfig = """
        hostname r1
        ip domain-name corp.example
        interface GigabitEthernet0/0
         description uplink
         ip address 203.0.113.1 255.255.255.252
         no shutdown
        interface GigabitEthernet0/1
         switchport mode access
         switchport access vlan 10
        interface GigabitEthernet0/2
         switchport mode trunk
         switchport trunk allowed vlan 10,20
        ip route 10.10.0.0 255.255.0.0 203.0.113.2
        router ospf 1
         network 192.168.10.0 0.0.0.255 area 0
        router bgp 65001
         neighbor 203.0.113.2 remote-as 64512
        ntp server 192.0.2.123
        logging host 10.99.0.5
        snmp-server community n0t-public RO
        """;

    [Fact]
    public void IosToJunos_TranslatesCoreDirectives()
    {
        var junos = ConfigTranslator.IosToJunos(IosConfig);

        Assert.Contains("set system host-name r1", junos);
        Assert.Contains("set system domain-name corp.example", junos);
        Assert.Contains("set interfaces GigabitEthernet0/0 description \"uplink\"", junos);
        Assert.Contains("set interfaces GigabitEthernet0/0 unit 0 family inet address 203.0.113.1/30", junos);
        Assert.Contains("set interfaces GigabitEthernet0/1 unit 0 family ethernet-switching interface-mode access", junos);
        Assert.Contains("set interfaces GigabitEthernet0/1 unit 0 family ethernet-switching vlan members 10", junos);
        Assert.Contains("set interfaces GigabitEthernet0/2 unit 0 family ethernet-switching interface-mode trunk", junos);
        Assert.Contains("set interfaces GigabitEthernet0/2 unit 0 family ethernet-switching vlan members 10,20", junos);
        Assert.Contains("set routing-options static route 10.10.0.0/16 next-hop 203.0.113.2", junos);
        Assert.Contains("set protocols ospf area 0 network 192.168.10.0/24", junos);
        Assert.Contains("set protocols bgp local-as 65001", junos);
        Assert.Contains("set protocols bgp group EXTERNAL neighbor 203.0.113.2 peer-as 64512", junos);
        Assert.Contains("set system ntp server 192.0.2.123", junos);
        Assert.Contains("set system syslog host 10.99.0.5 any any", junos);
        Assert.Contains("set snmp community n0t-public", junos);
    }

    [Fact]
    public void JunosToIos_TranslatesCoreDirectives()
    {
        var junos = """
            set system host-name r1
            set system domain-name corp.example
            set interfaces GigabitEthernet0/0 unit 0 family inet address 203.0.113.1/30
            set interfaces GigabitEthernet0/1 unit 0 family ethernet-switching interface-mode access vlan members 10
            set interfaces GigabitEthernet0/2 unit 0 family ethernet-switching interface-mode trunk vlan members 10,20
            set routing-options static route 10.10.0.0/16 next-hop 203.0.113.2
            set protocols ospf area 0 network 192.168.10.0/24
            set protocols bgp local-as 65001
            set protocols bgp group EXTERNAL neighbor 203.0.113.2 peer-as 64512
            set system ntp server 192.0.2.123
            set system syslog host 10.99.0.5 any any
            """;

        var ios = ConfigTranslator.JunosToIos(junos);

        Assert.Contains("hostname r1", ios);
        Assert.Contains("ip domain-name corp.example", ios);
        Assert.Contains("interface GigabitEthernet0/0", ios);
        Assert.Contains(" ip address 203.0.113.1 255.255.255.252", ios);
        Assert.Contains("interface GigabitEthernet0/1", ios);
        Assert.Contains(" switchport mode access", ios);
        Assert.Contains(" switchport access vlan 10", ios);
        Assert.Contains("interface GigabitEthernet0/2", ios);
        Assert.Contains(" switchport trunk allowed vlan 10,20", ios);
        Assert.Contains("ip route 10.10.0.0 255.255.0.0 203.0.113.2", ios);
        Assert.Contains("router ospf 1", ios);
        Assert.Contains(" network 192.168.10.0 255.255.255.0 area 0", ios);
        Assert.Contains("router bgp 65001", ios);
        Assert.Contains(" neighbor 203.0.113.2 remote-as 64512", ios);
        Assert.Contains("ntp server 192.0.2.123", ios);
        Assert.Contains("logging host 10.99.0.5", ios);
    }

    [Fact]
    public void RoundTrip_IosToJunosToIos_PreservesKeyFields()
    {
        var junos = ConfigTranslator.IosToJunos(IosConfig);
        var roundTripped = ConfigTranslator.JunosToIos(junos);

        Assert.Contains("hostname r1", roundTripped);
        Assert.Contains(" ip address 203.0.113.1 255.255.255.252", roundTripped);
        Assert.Contains(" switchport access vlan 10", roundTripped);
        Assert.Contains("ip route 10.10.0.0 255.255.0.0 203.0.113.2", roundTripped);
        Assert.Contains(" neighbor 203.0.113.2 remote-as 64512", roundTripped);
    }

    [Fact]
    public void IosToJunos_EmptyInput_ProducesEmptyOutput()
    {
        Assert.Equal(string.Empty, ConfigTranslator.IosToJunos(""));
        Assert.Equal(string.Empty, ConfigTranslator.JunosToIos(""));
    }
}

