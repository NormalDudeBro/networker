using NetOps.Core.NetTools.Topology;

namespace NetOps.Core.Tests.NetTools;

public class TopologyBuilderTests
{
    private const string RouterA = "hostname r1\n" +
        "interface GigabitEthernet0/0\n" +
        " ip address 10.0.0.1 255.255.255.252\n" +
        " no shutdown\n" +
        "router bgp 65001\n" +
        " neighbor 10.0.0.2 remote-as 64512\n" +
        "ip route 10.10.0.0 255.255.0.0 10.0.0.2\n";

    private const string RouterB = "hostname r2\n" +
        "interface GigabitEthernet0/0\n" +
        " ip address 10.0.0.2 255.255.255.252\n" +
        " no shutdown\n" +
        "router bgp 64512\n" +
        " neighbor 10.0.0.1 remote-as 65001\n";

    [Fact]
    public void Build_LinksPointToPointSubnets()
    {
        var topology = TopologyBuilder.Build(new[]
        {
            new DeviceConfig("r1", RouterA),
            new DeviceConfig("r2", RouterB),
        });

        var subnetLink = Assert.Single(topology.Links, l => l.Kind == "subnet");
        Assert.Contains("10.0.0.1/30", subnetLink.Detail);
        Assert.Equal("r1", subnetLink.Source);
        Assert.Equal("r2", subnetLink.Target);
    }

    [Fact]
    public void Build_CreatesBgpLinks()
    {
        var topology = TopologyBuilder.Build(new[]
        {
            new DeviceConfig("r1", RouterA),
            new DeviceConfig("r2", RouterB),
        });

        Assert.Contains(topology.Links, l => l.Kind == "bgp" && l.Source == "r1" && l.Target == "r2");
        Assert.Contains(topology.Links, l => l.Kind == "bgp" && l.Source == "r2" && l.Target == "r1");
        Assert.Contains(topology.Links, l => l.Kind == "static" && l.Source == "r1" && l.Target == "r2");
    }

    [Fact]
    public void Build_AddsExternalNodeForUnknownNextHop()
    {
        const string router = "hostname r3\n" +
            "interface GigabitEthernet0/0\n" +
            " ip address 10.0.0.1 255.255.255.252\n" +
            "ip route 0.0.0.0 0.0.0.0 192.0.2.1\n";

        var topology = TopologyBuilder.Build(new[] { new DeviceConfig("r3", router) });

        var external = Assert.Single(topology.Nodes, n => n.Kind == "external");
        Assert.Equal("external-192.0.2.1", external.Name);
        Assert.Contains(topology.Links, l => l.Kind == "static" && l.Target == "external-192.0.2.1");
    }

    [Fact]
    public void Build_SkipsWideSubnets()
    {
        const string mgmt = "hostname sw1\n" +
            "interface vlan 10\n" +
            " ip address 192.168.10.2 255.255.255.0\n";

        const string mgmt2 = "hostname sw2\n" +
            "interface vlan 10\n" +
            " ip address 192.168.10.3 255.255.255.0\n";

        var topology = TopologyBuilder.Build(new[]
        {
            new DeviceConfig("sw1", mgmt),
            new DeviceConfig("sw2", mgmt2),
        });

        Assert.DoesNotContain(topology.Links, l => l.Kind == "subnet");
        Assert.Equal(2, topology.Nodes.Count);
    }

    [Fact]
    public void Build_ReadsHostnameFromConfig()
    {
        var topology = TopologyBuilder.Build(new[] { new DeviceConfig("fallback", RouterA) });
        var node = Assert.Single(topology.Nodes, n => n.Kind == "device");
        Assert.Equal("r1", node.Name);
        Assert.Contains("GigabitEthernet0/0", node.Interfaces);
    }

    [Fact]
    public void RenderMermaid_DeduplicatesEdges()
    {
        var topology = TopologyBuilder.Build(new[]
        {
            new DeviceConfig("r1", RouterA),
            new DeviceConfig("r2", RouterB),
        });

        var mermaid = TopologyBuilder.RenderMermaid(topology);

        var edgeLines = mermaid.Split('\n').Where(l => l.Contains(" ---|")).ToList();
        // Subnet + BGP + static all describe the same r1-r2 pair -> one edge.
        Assert.Single(edgeLines);
        Assert.Contains("r1 ---|", edgeLines[0]);
        Assert.Contains("| r2", edgeLines[0]);
    }
}
