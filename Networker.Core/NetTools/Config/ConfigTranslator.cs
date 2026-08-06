using System.Net;
using System.Text;
using Networker.Core.NetTools.Ip;

namespace Networker.Core.NetTools.Config;

/// <summary>
/// Deterministic, line-based translation between Cisco IOS-XE and Juniper
/// Junos for a pragmatic subset: hostname/domain, NTP, logging, interfaces,
/// switchports, static routes, OSPF, and BGP.
/// </summary>
public static class ConfigTranslator
{
    public static string IosToJunos(string iosConfig)
    {
        var sb = new StringBuilder();
        var lines = iosConfig.Split('\n').Select(l => l.TrimEnd('\r')).ToList();

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i].Trim();

            if (TryMatch(line, "hostname ", out var value))
            {
                sb.AppendLine($"set system host-name {value}");
            }
            else if (TryMatch(line, "ip domain-name ", out value))
            {
                sb.AppendLine($"set system domain-name {value}");
            }
            else if (TryMatch(line, "ntp server ", out value))
            {
                sb.AppendLine($"set system ntp server {value}");
            }
            else if (TryMatch(line, "logging host ", out value))
            {
                sb.AppendLine($"set system syslog host {value} any any");
            }
            else if (TryMatch(line, "interface ", out var intfName))
            {
                var (block, next) = ReadBlock(lines, i);
                i = next;
                var ip = FindCiscoLine(block, "ip address");
                var accessVlan = FindCiscoLine(block, "switchport access vlan");
                var trunkMode = block.Any(l => l.Contains("switchport mode trunk"));
                var allowedVlans = FindCiscoLine(block, "switchport trunk allowed vlan");

                sb.AppendLine($"set interfaces {intfName} description \"{FindCiscoLine(block, "description") ?? string.Empty}\"");

                if (ip is not null)
                {
                    var parts = ip.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2)
                    {
                        sb.AppendLine($"set interfaces {intfName} unit 0 family inet address {ToCidr(parts[0], parts[1])}");
                    }
                }
                else if (trunkMode)
                {
                    sb.AppendLine($"set interfaces {intfName} unit 0 family ethernet-switching interface-mode trunk");
                    sb.AppendLine($"set interfaces {intfName} unit 0 family ethernet-switching vlan members {allowedVlans ?? "all"}");
                }
                else if (accessVlan is not null)
                {
                    sb.AppendLine($"set interfaces {intfName} unit 0 family ethernet-switching interface-mode access");
                    sb.AppendLine($"set interfaces {intfName} unit 0 family ethernet-switching vlan members {accessVlan}");
                }
            }
            else if (TryMatch(line, "ip route ", out value))
            {
                var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3)
                {
                    sb.AppendLine($"set routing-options static route {ToCidr(parts[0], parts[1])} next-hop {parts[2]}");
                }
            }
            else if (line == "router ospf")
            {
                var (block, next) = ReadBlock(lines, i);
                i = next;
                foreach (var member in block)
                {
                    if (TryMatch(member.Trim(), "network ", out var netValue))
                    {
                        var parts = netValue.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 4)
                        {
                            sb.AppendLine($"set protocols ospf area {parts[3]} network {ToCidrWildcard(parts[0], parts[1])}");
                        }
                    }
                }
            }
            else if (TryMatch(line, "router ospf ", out var ospfProc) && int.TryParse(ospfProc, out _))
            {
                var (block, next) = ReadBlock(lines, i);
                i = next;
                foreach (var member in block)
                {
                    if (TryMatch(member.Trim(), "network ", out var netValue))
                    {
                        var parts = netValue.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 4)
                        {
                            sb.AppendLine($"set protocols ospf area {parts[3]} network {ToCidrWildcard(parts[0], parts[1])}");
                        }
                    }
                }
            }
            else if (TryMatch(line, "router bgp ", out var localAs) && int.TryParse(localAs, out _))
            {
                var (block, next) = ReadBlock(lines, i);
                i = next;
                sb.AppendLine($"set protocols bgp local-as {localAs}");
                foreach (var member in block)
                {
                    if (TryMatch(member.Trim(), "neighbor ", out var neigh))
                    {
                        var parts = neigh.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 3)
                        {
                            sb.AppendLine($"set protocols bgp group EXTERNAL neighbor {parts[0]} peer-as {parts[2]}");
                        }
                    }
                }
            }
            else if (TryMatch(line, "snmp-server community ", out value))
            {
                var community = value.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
                sb.AppendLine($"set snmp community {community}");
            }
        }

        return sb.ToString();
    }

    public static string JunosToIos(string junosConfig)
    {
        var sb = new StringBuilder();
        var lines = junosConfig.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();

        var interfaces = new Dictionary<string, (string? Ip, string? Mode, string? Members, string? Description)>(StringComparer.Ordinal);
        var ospfNetworks = new List<(string Cidr, string Area)>();
        var bgpNeighbors = new List<(string Peer, string As)>();
        string? bgpLocalAs = null;
        var staticRoutes = new List<(string Cidr, string NextHop)>();

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            if (TryMatch(line, "set system host-name ", out var value))
            {
                sb.AppendLine($"hostname {value}");
            }
            else if (TryMatch(line, "set system domain-name ", out value))
            {
                sb.AppendLine($"ip domain-name {value}");
            }
            else if (TryMatch(line, "set system ntp server ", out value))
            {
                sb.AppendLine($"ntp server {value}");
            }
            else if (TryMatch(line, "set system syslog host ", out value))
            {
                var host = value.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
                sb.AppendLine($"logging host {host}");
            }
            else if (TryMatch(line, "set interfaces ", out value))
            {
                var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && parts[0] is "irb" or "lo0" or "fxp0")
                {
                    continue;
                }

                var name = parts[0];
                var rest = string.Join(' ', parts.Skip(1));
                var address = FindJunosValue(rest, "address");
                var mode = rest.Contains("interface-mode trunk") ? "trunk"
                    : rest.Contains("interface-mode access") ? "access"
                    : null;
                var members = FindJunosValue(rest, "vlan members");
                var description = FindJunosValue(rest, "description");

                if (interfaces.TryGetValue(name, out var existing))
                {
                    interfaces[name] = (address ?? existing.Ip, mode ?? existing.Mode, members ?? existing.Members, description ?? existing.Description);
                }
                else
                {
                    interfaces[name] = (address, mode, members, description);
                }
            }
            else if (TryMatch(line, "set routing-options static route ", out value))
            {
                var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    staticRoutes.Add((parts[0], parts[^1]));
                }
            }
            else if (TryMatch(line, "set protocols ospf area ", out value))
            {
                var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    ospfNetworks.Add((parts[^1], parts[0]));
                }
            }
            else if (TryMatch(line, "set protocols bgp local-as ", out value))
            {
                bgpLocalAs = value;
            }
            else if (TryMatch(line, "set protocols bgp group ", out value) && value.Contains("neighbor "))
            {
                var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                // group NAME neighbor PEER peer-as AS
                var idx = Array.FindIndex(parts, p => p == "neighbor");
                if (idx >= 0 && idx + 2 < parts.Length)
                {
                    bgpNeighbors.Add((parts[idx + 1], parts[^1]));
                }
            }
        }

        foreach (var (name, intf) in interfaces)
        {
            sb.AppendLine($"interface {name}");
            if (!string.IsNullOrWhiteSpace(intf.Description))
            {
                sb.AppendLine($" description {intf.Description}");
            }

            if (intf.Ip is not null)
            {
                var (ip, mask) = SplitCidrPreservingHost(intf.Ip);
                sb.AppendLine($" ip address {ip} {mask}");
            }
            else if (intf.Mode is "trunk")
            {
                sb.AppendLine(" switchport mode trunk");
                sb.AppendLine($" switchport trunk allowed vlan {intf.Members ?? "all"}");
            }
            else if (intf.Mode is "access")
            {
                sb.AppendLine(" switchport mode access");
                sb.AppendLine($" switchport access vlan {intf.Members ?? "1"}");
            }

            sb.AppendLine(" no shutdown");
            sb.AppendLine("!");
        }

        foreach (var route in staticRoutes)
        {
            var (ip, mask) = SplitCidrPreservingHost(route.Cidr);
            sb.AppendLine($"ip route {ip} {mask} {route.NextHop}");
        }

        if (ospfNetworks.Count > 0)
        {
            sb.AppendLine("router ospf 1");
            foreach (var (cidr, area) in ospfNetworks)
            {
                var (ip, mask) = SplitCidrPreservingHost(cidr);
                sb.AppendLine($" network {ip} {mask} area {area}");
            }

            sb.AppendLine("!");
        }

        if (!string.IsNullOrWhiteSpace(bgpLocalAs) && bgpNeighbors.Count > 0)
        {
            sb.AppendLine($"router bgp {bgpLocalAs}");
            foreach (var (peer, asn) in bgpNeighbors)
            {
                sb.AppendLine($" neighbor {peer} remote-as {asn}");
            }

            sb.AppendLine("!");
        }

        return sb.ToString();
    }

    private static (List<string> Lines, int NextIndex) ReadBlock(List<string> lines, int start)
    {
        var block = new List<string>();
        var i = start + 1;
        while (i < lines.Count && lines[i].StartsWith(' '))
        {
            block.Add(lines[i]);
            i++;
        }

        return (block, i - 1);
    }

    private static string? FindCiscoLine(List<string> lines, string keyword)
    {
        foreach (var line in lines)
        {
            if (TryMatch(line.Trim(), $"{keyword} ", out var value))
            {
                return value;
            }
        }

        return null;
    }

    private static string? FindJunosValue(string rest, string keyword)
    {
        var marker = keyword + " ";
        var idx = rest.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0)
        {
            return null;
        }

        var value = rest[(idx + marker.Length)..].Trim();
        return value.Length == 0 ? null : value;
    }

    private static bool TryMatch(string line, string prefix, out string value)
    {
        if (line.StartsWith(prefix, StringComparison.Ordinal))
        {
            value = line[prefix.Length..].Trim();
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static string ToCidr(string ip, string mask)
    {
        var prefix = MaskToPrefix(mask);
        return prefix >= 0 ? $"{ip}/{prefix}" : $"{ip}/32";
    }

    private static string ToCidrWildcard(string ip, string wildcard)
    {
        if (!IPAddress.TryParse(wildcard, out var parsed))
        {
            return $"{ip}/32";
        }

        var ones = 0;
        foreach (var b in parsed.GetAddressBytes())
        {
            var x = b;
            while (x != 0)
            {
                ones += x & 1;
                x >>= 1;
            }
        }

        return $"{ip}/{32 - ones}";
    }

    private static (string Ip, string Mask) SplitCidrPreservingHost(string cidr)
    {
        var parts = cidr.Split('/');
        var ip = parts[0];
        var prefix = int.TryParse(parts.ElementAtOrDefault(1), out var p) ? p : 24;
        return (ip, PrefixToMask(prefix));
    }

    private static int MaskToPrefix(string mask)
    {
        if (IPAddress.TryParse(mask, out var parsed))
        {
            var bytes = parsed.GetAddressBytes();
            var bits = 0;
            foreach (var b in bytes)
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

        return -1;
    }

    private static string PrefixToMask(int prefix)
    {
        var subnet = IpToolkit.Calculate($"0.0.0.0/{prefix}");
        return subnet.Netmask;
    }
}

