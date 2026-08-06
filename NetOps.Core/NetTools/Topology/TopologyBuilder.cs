using System.Text;
using System.Text.RegularExpressions;
using NetOps.Core.NetTools.Ip;

namespace NetOps.Core.NetTools.Topology;

public sealed record DeviceConfig(string Name, string Config);

public sealed record TopologyNode(string Name, string Kind, IReadOnlyList<string> Interfaces);

public sealed record TopologyLink(string Source, string Target, string Detail, string Kind);

public sealed record Topology(IReadOnlyList<TopologyNode> Nodes, IReadOnlyList<TopologyLink> Links);

/// <summary>
/// Builds a connectivity graph from a set of device configs by correlating
/// point-to-point interface subnets, BGP neighbors, and static-route next hops.
/// </summary>
public static class TopologyBuilder
{
    private sealed record InterfaceIp(string Device, string Interface, string Cidr);

    public static Topology Build(IEnumerable<DeviceConfig> configs)
    {
        var nodes = new List<TopologyNode>();
        var links = new List<TopologyLink>();
        var interfaces = new List<InterfaceIp>();
        var bgpNeighbors = new List<(string Device, string Peer)>();
        var staticRoutes = new List<(string Device, string NextHop)>();

        foreach (var config in configs)
        {
            var name = ParseHostname(config.Config) ?? config.Name;
            var ifaces = ParseInterfaces(config.Config);
            var bgp = ParseBgpNeighbors(config.Config);
            var statics = ParseStaticRoutes(config.Config);

            nodes.Add(new TopologyNode(name, "device", ifaces.Select(i => i.Interface).ToList()));
            interfaces.AddRange(ifaces.Select(i => new InterfaceIp(name, i.Interface, i.Cidr)));
            bgpNeighbors.AddRange(bgp.Select(p => (name, p)));
            staticRoutes.AddRange(statics.Select(s => (name, s)));
        }

        AddSubnetLinks(links, interfaces);
        AddBgpLinks(links, nodes, interfaces, bgpNeighbors);
        AddStaticLinks(links, nodes, interfaces, staticRoutes);

        return new Topology(nodes, links);
    }

    public static string RenderMermaid(Topology topology)
    {
        var sb = new StringBuilder();
        sb.AppendLine("graph LR");
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var node in topology.Nodes)
        {
            sb.AppendLine($"  {EscapeId(node.Name)}[\"{EscapeLabel(node.Name)}\"]");
        }

        foreach (var link in topology.Links)
        {
            var key = string.CompareOrdinal(link.Source, link.Target) <= 0
                ? $"{link.Source}|{link.Target}"
                : $"{link.Target}|{link.Source}";
            if (!seen.Add(key))
            {
                continue;
            }

            sb.AppendLine($"  {EscapeId(link.Source)} ---|{EscapeLabel(link.Detail)}| {EscapeId(link.Target)}");
        }

        return sb.ToString();
    }

    private static void AddSubnetLinks(List<TopologyLink> links, List<InterfaceIp> interfaces)
    {
        for (var i = 0; i < interfaces.Count; i++)
        {
            for (var j = i + 1; j < interfaces.Count; j++)
            {
                var a = interfaces[i];
                var b = interfaces[j];
                if (a.Device == b.Device)
                {
                    continue;
                }

                var network = TrySharedNetwork(a.Cidr, b.Cidr);
                if (network is null)
                {
                    continue;
                }

                links.Add(new TopologyLink(
                    a.Device,
                    b.Device,
                    $"{a.Interface} {a.Cidr} / {b.Interface} {b.Cidr} ({network})",
                    "subnet"));
            }
        }
    }

    private static void AddBgpLinks(
        List<TopologyLink> links,
        List<TopologyNode> nodes,
        List<InterfaceIp> interfaces,
        List<(string Device, string Peer)> neighbors)
    {
        var ipIndex = interfaces
            .Select(i => (i.Device, Ip: i.Cidr.Split('/')[0]))
            .GroupBy(x => x.Ip, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        foreach (var (device, peer) in neighbors)
        {
            if (!ipIndex.TryGetValue(peer, out var matches))
            {
                var ext = $"external-{peer}";
                if (nodes.All(n => n.Name != ext))
                {
                    nodes.Add(new TopologyNode(ext, "external", Array.Empty<string>()));
                }

                links.Add(new TopologyLink(device, ext, $"BGP neighbor {peer}", "bgp"));
                continue;
            }

            foreach (var match in matches)
            {
                if (match.Device != device)
                {
                    links.Add(new TopologyLink(device, match.Device, $"BGP neighbor {peer}", "bgp"));
                }
            }
        }
    }

    private static void AddStaticLinks(
        List<TopologyLink> links,
        List<TopologyNode> nodes,
        List<InterfaceIp> interfaces,
        List<(string Device, string NextHop)> statics)
    {
        var ipIndex = interfaces
            .Select(i => (i.Device, Ip: i.Cidr.Split('/')[0]))
            .GroupBy(x => x.Ip, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        foreach (var (device, nextHop) in statics)
        {
            if (!ipIndex.TryGetValue(nextHop, out var matches))
            {
                var ext = $"external-{nextHop}";
                if (nodes.All(n => n.Name != ext))
                {
                    nodes.Add(new TopologyNode(ext, "external", Array.Empty<string>()));
                }

                links.Add(new TopologyLink(device, ext, $"static via {nextHop}", "static"));
                continue;
            }

            foreach (var match in matches)
            {
                if (match.Device != device)
                {
                    links.Add(new TopologyLink(device, match.Device, $"static via {nextHop}", "static"));
                }
            }
        }
    }

    private static string? TrySharedNetwork(string cidrA, string cidrB)
    {
        var a = IpToolkit.Calculate(cidrA);
        var b = IpToolkit.Calculate(cidrB);

        if (a.IpVersion != b.IpVersion || a.PrefixLength != b.PrefixLength)
        {
            return null;
        }

        if (a.PrefixLength is < 29 or > 32 && a.PrefixLength != 128)
        {
            return null;
        }

        if (a.NetworkAddress != b.NetworkAddress)
        {
            return null;
        }

        return $"{a.NetworkAddress}/{a.PrefixLength}";
    }

    private static string? ParseHostname(string config)
    {
        return Regex.Match(config, @"(?m)^hostname\s+(\S+)\s*$", RegexOptions.IgnoreCase) is { Success: true } m
            ? m.Groups[1].Value
            : null;
    }

    private static List<(string Interface, string Cidr)> ParseInterfaces(string config)
    {
        var result = new List<(string Interface, string Cidr)>();
        var interfaceRe = new Regex(@"(?m)^interface\s+(\S+)\s*$", RegexOptions.IgnoreCase);
        foreach (Match iface in interfaceRe.Matches(config))
        {
            var block = ReadBlock(config, iface.Index + iface.Length);
            var ip = Regex.Match(block, @"ip\s+address\s+(\S+)\s+(\S+)", RegexOptions.IgnoreCase);
            if (ip.Success)
            {
                result.Add((iface.Groups[1].Value, CidrFrom(ip.Groups[1].Value, ip.Groups[2].Value)));
            }
        }

        return result;
    }

    private static List<string> ParseBgpNeighbors(string config)
    {
        var bgp = Regex.Match(config, @"(?m)^router\s+bgp\s+\S+\s*$", RegexOptions.IgnoreCase);
        if (!bgp.Success)
        {
            return new List<string>();
        }

        var block = ReadBlock(config, bgp.Index + bgp.Length);
        return Regex.Matches(block, @"neighbor\s+(\d{1,3}(?:\.\d{1,3}){3})\s+remote-as", RegexOptions.IgnoreCase)
            .Select(m => m.Groups[1].Value)
            .ToList();
    }

    private static List<string> ParseStaticRoutes(string config)
    {
        return Regex.Matches(config, @"(?m)^ip\s+route\s+(\S+)\s+(\S+)\s+(\d{1,3}(?:\.\d{1,3}){3})", RegexOptions.IgnoreCase)
            .Select(m => m.Groups[3].Value)
            .ToList();
    }

    private static string ReadBlock(string config, int start)
    {
        var lines = config.Substring(start).Split('\n');
        var sb = new StringBuilder();
        foreach (var line in lines)
        {
            if (line.Length == 0 || string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (line[0] != ' ')
            {
                break;
            }

            sb.AppendLine(line);
        }

        return sb.ToString();
    }

    private static string CidrFrom(string ip, string mask)
    {
        var prefix = MaskToPrefix(mask);
        return prefix >= 0 ? $"{ip}/{prefix}" : $"{ip}/32";
    }

    private static int MaskToPrefix(string mask)
    {
        if (!System.Net.IPAddress.TryParse(mask, out var parsed))
        {
            return -1;
        }

        var bits = 0;
        foreach (var b in parsed.GetAddressBytes())
        {
            for (var bit = 7; bit >= 0; bit--)
            {
                if ((b & (1 << bit)) == 0)
                {
                    return bits;
                }

                bits++;
            }
        }

        return bits;
    }

    private static string EscapeId(string id) => id.Replace("-", "_").Replace(".", "_");

    private static string EscapeLabel(string label) => label.Replace("\"", "'");
}
