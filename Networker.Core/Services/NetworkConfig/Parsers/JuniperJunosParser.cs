using System.Collections.Generic;
using System.Text.RegularExpressions;
using Networker.Core.Models.NetworkConfig;

namespace Networker.Core.Services.NetworkConfig.Parsers;

/// <summary>
/// Parser for Juniper Junos configurations.
/// Ported from NetworkConfigPro <c>src/core/parsers/config_parser.py</c>
/// <c>JuniperJunosParser</c>.
/// </summary>
public sealed class JuniperJunosParser : BaseConfigParser
{
    private static readonly Regex SystemRegex = new(@"system\s*\{([^{}]*(?:\{[^{}]*\}[^{}]*)*)\}", RegexOptions.Singleline);
    private static readonly Regex HostNameRegex = new(@"host-name\s+(\S+);");
    private static readonly Regex DomainNameRegex = new(@"domain-name\s+(\S+);");
    private static readonly Regex NameserverBlockRegex = new(@"name-server\s*\{([^}]*)\}");
    private static readonly Regex IpSemicolonRegex = new(@"(\d+\.\d+\.\d+\.\d+);");
    private static readonly Regex NameserverLineRegex = new(@"name-server\s+(\d+\.\d+\.\d+\.\d+);");
    private static readonly Regex NtpBlockRegex = new(@"ntp\s*\{([^}]*)\}");
    private static readonly Regex NtpServerRegex = new(@"server\s+(\S+);");
    private static readonly Regex LoginBannerRegex = new(@"login\s*\{[^}]*message\s+""([^""]+)""", RegexOptions.Singleline);

    private static readonly Regex InterfacesBlockRegex = new(@"interfaces\s*\{");
    private static readonly Regex IfaceStartRegex = new(@"^\s*([\w\-/]+)\s*\{", RegexOptions.Multiline);
    private static readonly Regex QuotedDescRegex = new(@"description\s+""([^""]+)""");
    private static readonly Regex BareDescRegex = new(@"description\s+(\S+);");
    private static readonly Regex AddressRegex = new(@"address\s+(\d+\.\d+\.\d+\.\d+)/(\d+)");
    private static readonly Regex VlanMembersRegex = new(@"members\s+(\S+);");
    private static readonly Regex MtuRegex = new(@"mtu\s+(\d+);");
    private static readonly Regex AggregationRegex = new(@"802\.3ad\s+(\S+);");
    private static readonly Regex AeNumRegex = new(@"ae(\d+)");

    private static readonly Regex VlansRegex = new(@"vlans\s*\{([^{}]*(?:\{[^{}]*\}[^{}]*)*)\}", RegexOptions.Singleline);
    private static readonly Regex VlanBlockRegex = new(@"(\S+)\s*\{([^{}]*)\}", RegexOptions.Singleline);
    private static readonly Regex VlanIdRegex = new(@"vlan-id\s+(\d+);");

    private static readonly Regex RoutingOptionsRegex = new(@"routing-options\s*\{([^{}]*(?:\{[^{}]*(?:\{[^{}]*\}[^{}]*)*\}[^{}]*)*)\}", RegexOptions.Singleline);
    private static readonly Regex StaticBlockRegex = new(@"static\s*\{([^{}]*(?:\{[^{}]*\}[^{}]*)*)\}", RegexOptions.Singleline);
    private static readonly Regex RouteRegex = new(
        @"route\s+(\d+\.\d+\.\d+\.\d+)/(\d+)\s*(?:\{[^}]*next-hop\s+(\d+\.\d+\.\d+\.\d+)|next-hop\s+(\d+\.\d+\.\d+\.\d+))",
        RegexOptions.Singleline);

    private static readonly Regex OspfBlockRegex = new(@"ospf\s*\{([^{}]*(?:\{[^{}]*(?:\{[^{}]*\}[^{}]*)*\}[^{}]*)*)\}", RegexOptions.Singleline);
    private static readonly Regex RouterIdRegex = new(@"router-id\s+(\d+\.\d+\.\d+\.\d+);");
    private static readonly Regex ReferenceBwRegex = new(@"reference-bandwidth\s+(\S+);");
    private static readonly Regex AreaRegex = new(@"area\s+(\S+)\s*\{([^{}]*(?:\{[^{}]*\}[^{}]*)*)\}", RegexOptions.Singleline);
    private static readonly Regex AreaIfaceRegex = new(@"interface\s+(\S+)");

    private static readonly Regex BgpBlockRegex = new(@"bgp\s*\{([^{}]*(?:\{[^{}]*(?:\{[^{}]*\}[^{}]*)*\}[^{}]*)*)\}", RegexOptions.Singleline);
    private static readonly Regex AutonomousSystemRegex = new(@"autonomous-system\s+(\d+);");
    private static readonly Regex GroupRegex = new(@"group\s+(\S+)\s*\{([^{}]*(?:\{[^{}]*\}[^{}]*)*)\}", RegexOptions.Singleline);
    private static readonly Regex PeerAsRegex = new(@"peer-as\s+(\d+);");
    private static readonly Regex NeighborRegex = new(@"neighbor\s+(\d+\.\d+\.\d+\.\d+)");
    private static readonly Regex AuthKeyRegex = new(@"authentication-key\s+""([^""]+)""");
    private static readonly Regex MultihopRegex = new(@"multihop\s*\{[^}]*ttl\s+(\d+)");
    private static readonly Regex LocalAddressRegex = new(@"local-address\s+(\S+);");

    /// <inheritdoc />
    public override bool DetectVendor(string configText)
    {
        var text = NormalizeNewlines(configText);
        var indicators = new[]
        {
            @"system\s*\{",
            @"interfaces\s*\{",
            @"host-name\s+\S+;",
            @"protocols\s*\{",
            @"routing-options\s*\{",
            @"vlans\s*\{",
        };

        foreach (var pattern in indicators)
        {
            if (Regex.IsMatch(text, pattern, RegexOptions.Multiline))
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public override ParseResult Parse(string configText)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var text = NormalizeNewlines(configText);

        NetworkDeviceConfig config;
        try
        {
            // Parse system block
            var (hostname, domainName, dnsServers, ntpServers, bannerMotd) = ParseSystemBlock(text, warnings);

            var interfaces = ParseInterfaces(text);
            var vlans = ParseVlans(text);
            var staticRoutes = ParseStaticRoutes(text);
            var ospf = ParseOspf(text);
            var bgp = ParseBgp(text);

            config = new NetworkDeviceConfig
            {
                Hostname = hostname,
                Vendor = Vendor.JuniperJunos,
                DomainName = domainName,
                DnsServers = dnsServers,
                NtpServers = ntpServers,
                BannerMotd = bannerMotd,
                Interfaces = interfaces,
                Vlans = vlans,
                StaticRoutes = staticRoutes,
                Ospf = ospf,
                Bgp = bgp,
            };
        }
        catch (Exception ex)
        {
            errors.Add($"Parse error: {ex.Message}");
            return new ParseResult
            {
                Config = null,
                Vendor = Vendor.JuniperJunos,
                Errors = errors,
                Warnings = warnings,
            };
        }

        return new ParseResult
        {
            Config = config,
            Vendor = Vendor.JuniperJunos,
            Errors = errors,
            Warnings = warnings,
        };
    }

    private static (string Hostname, string? DomainName, List<string> DnsServers, List<string> NtpServers, string BannerMotd) ParseSystemBlock(
        string configText, List<string> warnings)
    {
        var systemMatch = SystemRegex.Match(configText);
        if (!systemMatch.Success)
        {
            warnings.Add("No system block found in configuration");
            return ("", null, new List<string>(), new List<string>(), string.Empty);
        }

        var systemBlock = systemMatch.Groups[1].Value;

        // Hostname
        var hostname = "";
        var hostnameMatch = HostNameRegex.Match(systemBlock);
        if (hostnameMatch.Success)
        {
            hostname = hostnameMatch.Groups[1].Value;
        }
        else
        {
            warnings.Add("No hostname found in configuration");
        }

        // Domain name
        string? domainName = null;
        var domainMatch = DomainNameRegex.Match(systemBlock);
        if (domainMatch.Success)
        {
            domainName = domainMatch.Groups[1].Value;
        }

        // Name servers (DNS)
        var dnsServers = new List<string>();
        var nameserverBlock = NameserverBlockRegex.Match(systemBlock);
        if (nameserverBlock.Success)
        {
            foreach (Match match in IpSemicolonRegex.Matches(nameserverBlock.Groups[1].Value))
            {
                dnsServers.Add(match.Groups[1].Value);
            }
        }
        else
        {
            // Single name-server format
            foreach (Match match in NameserverLineRegex.Matches(systemBlock))
            {
                dnsServers.Add(match.Groups[1].Value);
            }
        }

        // NTP servers
        var ntpServers = new List<string>();
        var ntpBlock = NtpBlockRegex.Match(systemBlock);
        if (ntpBlock.Success)
        {
            foreach (Match match in NtpServerRegex.Matches(ntpBlock.Groups[1].Value))
            {
                ntpServers.Add(match.Groups[1].Value);
            }
        }

        // Login banner (motd)
        var bannerMotd = string.Empty;
        var bannerMatch = LoginBannerRegex.Match(systemBlock);
        if (bannerMatch.Success)
        {
            bannerMotd = bannerMatch.Groups[1].Value;
        }

        return (hostname, domainName, dnsServers, ntpServers, bannerMotd);
    }

    private static List<Interface> ParseInterfaces(string configText)
    {
        var interfaces = new List<Interface>();

        // Find the interfaces block using brace counting
        var interfacesMatch = InterfacesBlockRegex.Match(configText);
        if (!interfacesMatch.Success)
        {
            return interfaces;
        }

        var interfacesBlock = ExtractBraceBlock(configText, interfacesMatch.Index);
        if (interfacesBlock is null)
        {
            return interfaces;
        }

        // Find interface names at the start of blocks within interfaces { }
        // Skip the outer "interfaces {" wrapper
        var innerContent = interfacesBlock[1..^1];

        foreach (Match match in IfaceStartRegex.Matches(innerContent))
        {
            var ifaceName = match.Groups[1].Value;

            // Skip non-interface entries
            if (ifaceName is "apply-groups" or "apply-macro")
            {
                continue;
            }

            // Extract this interface's block
            var ifaceBlock = ExtractBraceBlock(innerContent, match.Index);
            if (ifaceBlock is null)
            {
                continue;
            }

            // Description (quoted or bare)
            var description = string.Empty;
            var descMatch = QuotedDescRegex.Match(ifaceBlock);
            if (descMatch.Success)
            {
                description = descMatch.Groups[1].Value;
            }
            else
            {
                var bareDescMatch = BareDescRegex.Match(ifaceBlock);
                if (bareDescMatch.Success)
                {
                    description = bareDescMatch.Groups[1].Value;
                }
            }

            // IP address - look anywhere in the interface block
            var ipMatch = AddressRegex.Match(ifaceBlock);
            var ipAddress = ipMatch.Success ? TryParseIp(ipMatch.Groups[1].Value) : null;
            var subnetMask = ipMatch.Success ? TryParseIp(CidrToNetmask(int.Parse(ipMatch.Groups[2].Value))) : null;

            // Check for VLAN membership (used as a description fallback)
            var vlanMatch = VlanMembersRegex.Match(ifaceBlock);
            if (vlanMatch.Success && description.Length == 0)
            {
                description = $"VLAN: {vlanMatch.Groups[1].Value}";
            }

            // MTU
            var mtu = 1500;
            var mtuMatch = MtuRegex.Match(ifaceBlock);
            if (mtuMatch.Success)
            {
                mtu = int.Parse(mtuMatch.Groups[1].Value);
            }

            // Disable status
            var enabled = !ifaceBlock.Contains("disable;");

            // Aggregated ethernet (port-channel equivalent)
            int? channelGroup = null;
            var aeMatch = AggregationRegex.Match(ifaceBlock);
            if (aeMatch.Success)
            {
                var aeNum = AeNumRegex.Match(aeMatch.Groups[1].Value);
                if (aeNum.Success)
                {
                    channelGroup = int.Parse(aeNum.Groups[1].Value);
                }
            }

            interfaces.Add(new Interface
            {
                Name = ifaceName,
                InterfaceType = DetectInterfaceType(ifaceName),
                Description = description,
                IpAddress = ipAddress,
                SubnetMask = subnetMask,
                Enabled = enabled,
                Mtu = mtu,
                ChannelGroup = channelGroup,
            });
        }

        return interfaces;
    }

    private static string? ExtractBraceBlock(string text, int startPos)
    {
        var bracePos = text.IndexOf('{', startPos);
        if (bracePos < 0)
        {
            return null;
        }

        var depth = 0;
        for (var i = bracePos; i < text.Length; i++)
        {
            if (text[i] == '{')
            {
                depth++;
            }
            else if (text[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return text[bracePos..(i + 1)];
                }
            }
        }

        return null;
    }

    private static List<Vlan> ParseVlans(string configText)
    {
        var vlans = new List<Vlan>();

        var vlansMatch = VlansRegex.Match(configText);
        if (!vlansMatch.Success)
        {
            return vlans;
        }

        var vlansBlock = vlansMatch.Groups[1].Value;

        // Match individual VLAN blocks
        foreach (Match match in VlanBlockRegex.Matches(vlansBlock))
        {
            var vlanName = match.Groups[1].Value;
            var vlanBlock = match.Groups[2].Value;

            // Get VLAN ID
            var vlanIdMatch = VlanIdRegex.Match(vlanBlock);
            if (vlanIdMatch.Success)
            {
                vlans.Add(new Vlan
                {
                    VlanId = int.Parse(vlanIdMatch.Groups[1].Value),
                    Name = vlanName,
                });
            }
        }

        return vlans;
    }

    private static List<StaticRoute> ParseStaticRoutes(string configText)
    {
        var routes = new List<StaticRoute>();

        var routingMatch = RoutingOptionsRegex.Match(configText);
        if (!routingMatch.Success)
        {
            return routes;
        }

        var routingBlock = routingMatch.Groups[1].Value;

        // Find static block
        var staticMatch = StaticBlockRegex.Match(routingBlock);
        if (!staticMatch.Success)
        {
            return routes;
        }

        var staticBlock = staticMatch.Groups[1].Value;

        // Parse routes
        foreach (Match match in RouteRegex.Matches(staticBlock))
        {
            var destination = match.Groups[1].Value;
            var prefix = int.Parse(match.Groups[2].Value);
            var nextHop = match.Groups[3].Success ? match.Groups[3].Value : match.Groups[4].Value;

            if (nextHop.Length > 0)
            {
                routes.Add(new StaticRoute
                {
                    Destination = destination,
                    Mask = CidrToNetmask(prefix),
                    NextHop = nextHop,
                });
            }
        }

        return routes;
    }

    private static OspfConfig? ParseOspf(string configText)
    {
        // Searched directly rather than via a "protocols" wrapper: the C#
        // generator emits a separate "protocols" block per protocol, so
        // anchoring to the first block would miss OSPF when BGP's block
        // comes first.
        var ospfMatch = OspfBlockRegex.Match(configText);
        if (!ospfMatch.Success)
        {
            return null;
        }

        var ospfBlock = ospfMatch.Groups[1].Value;
        // Junos doesn't use process IDs the same way
        var ospf = new OspfConfig { ProcessId = 0 };

        // Router ID from routing-options
        var ridMatch = RouterIdRegex.Match(configText);
        if (ridMatch.Success)
        {
            ospf = ospf with { RouterId = ridMatch.Groups[1].Value };
        }

        // Reference bandwidth
        var bwMatch = ReferenceBwRegex.Match(ospfBlock);
        if (bwMatch.Success)
        {
            var bwStr = bwMatch.Groups[1].Value;
            // Handle values like "10g" or "1000m"
            if (bwStr.EndsWith('g'))
            {
                ospf = ospf with { ReferenceBandwidth = int.Parse(bwStr[..^1]) * 1000 };
            }
            else if (bwStr.EndsWith('m'))
            {
                ospf = ospf with { ReferenceBandwidth = int.Parse(bwStr[..^1]) };
            }
            else if (int.TryParse(bwStr, out var bw))
            {
                ospf = ospf with { ReferenceBandwidth = bw };
            }
        }

        // Parse areas and their interfaces
        foreach (Match areaMatch in AreaRegex.Matches(ospfBlock))
        {
            var areaId = areaMatch.Groups[1].Value;
            var areaBlock = areaMatch.Groups[2].Value;

            // Convert area to integer if possible (dotted format like 0.0.0.0
            // uses the last octet)
            var areaNum = 0;
            var dotIndex = areaId.LastIndexOf('.');
            var areaStr = dotIndex >= 0 ? areaId[(dotIndex + 1)..] : areaId;
            if (int.TryParse(areaStr, out var parsedArea))
            {
                areaNum = parsedArea;
            }

            // Find interfaces in this area
            foreach (Match ifaceMatch in AreaIfaceRegex.Matches(areaBlock))
            {
                var ifaceName = ifaceMatch.Groups[1].Value.TrimEnd(';');

                // Create a network entry (approximation - Junos doesn't
                // specify networks the same way)
                ospf.Networks.Add(new OspfNetwork
                {
                    Network = "0.0.0.0",
                    Wildcard = "0.0.0.0",
                    Area = areaNum,
                });

                // Check for passive
                if (areaBlock.Contains("passive"))
                {
                    ospf.PassiveInterfaces.Add(ifaceName);
                }
            }
        }

        return ospf.Networks.Count > 0 ? ospf : null;
    }

    private static BgpConfig? ParseBgp(string configText)
    {
        // Same rationale as ParseOspf: search directly so BGP is found even
        // when the config carries more than one "protocols" block.
        var bgpMatch = BgpBlockRegex.Match(configText);
        if (!bgpMatch.Success)
        {
            return null;
        }

        var bgpBlock = bgpMatch.Groups[1].Value;

        // Get local AS from routing-options
        var asMatch = AutonomousSystemRegex.Match(configText);
        if (!asMatch.Success)
        {
            return null;
        }

        var localAs = int.Parse(asMatch.Groups[1].Value);
        var bgp = new BgpConfig { LocalAs = localAs };

        // Router ID
        var ridMatch = RouterIdRegex.Match(configText);
        if (ridMatch.Success)
        {
            bgp = bgp with { RouterId = ridMatch.Groups[1].Value };
        }

        // Parse groups and neighbors
        foreach (Match groupMatch in GroupRegex.Matches(bgpBlock))
        {
            var groupName = groupMatch.Groups[1].Value;
            var groupBlock = groupMatch.Groups[2].Value;

            // Get peer-as for the group
            var peerAsMatch = PeerAsRegex.Match(groupBlock);
            var peerAs = peerAsMatch.Success ? int.Parse(peerAsMatch.Groups[1].Value) : 0;

            // Find neighbors in this group
            foreach (Match neighborMatch in NeighborRegex.Matches(groupBlock))
            {
                var neighborIp = neighborMatch.Groups[1].Value;

                string? password = null;
                var authMatch = AuthKeyRegex.Match(groupBlock);
                if (authMatch.Success)
                {
                    password = authMatch.Groups[1].Value;
                }

                // Multihop
                var ebgpMultihop = 0;
                var multihopMatch = MultihopRegex.Match(groupBlock);
                if (multihopMatch.Success)
                {
                    ebgpMultihop = int.Parse(multihopMatch.Groups[1].Value);
                }
                else if (groupBlock.Contains("multihop"))
                {
                    ebgpMultihop = 2;
                }

                // Local address (update-source equivalent)
                string? updateSource = null;
                var localMatch = LocalAddressRegex.Match(groupBlock);
                if (localMatch.Success)
                {
                    updateSource = localMatch.Groups[1].Value;
                }

                bgp.Neighbors.Add(new BgpNeighbor
                {
                    IpAddress = neighborIp,
                    RemoteAs = peerAs,
                    Description = groupName,
                    Password = password,
                    EbgpMultihop = ebgpMultihop,
                    UpdateSource = updateSource,
                });
            }
        }

        return bgp;
    }

    private static InterfaceType DetectInterfaceType(string name)
    {
        var nameLower = name.ToLowerInvariant();
        if (nameLower.StartsWith("ge-", StringComparison.Ordinal))
        {
            return InterfaceType.Gigabit;
        }
        else if (nameLower.StartsWith("xe-", StringComparison.Ordinal))
        {
            return InterfaceType.TenGigabit;
        }
        else if (nameLower.StartsWith("et-", StringComparison.Ordinal))
        {
            return InterfaceType.HundredGigabit;
        }
        else if (nameLower.StartsWith("lo", StringComparison.Ordinal))
        {
            return InterfaceType.Loopback;
        }
        else if (nameLower.StartsWith("ae", StringComparison.Ordinal))
        {
            return InterfaceType.PortChannel;
        }
        else if (nameLower.StartsWith("vlan", StringComparison.Ordinal) || nameLower.StartsWith("irb", StringComparison.Ordinal))
        {
            return InterfaceType.Vlan;
        }
        else if (nameLower.StartsWith("em", StringComparison.Ordinal) || nameLower.StartsWith("fxp", StringComparison.Ordinal))
        {
            return InterfaceType.Management;
        }

        return InterfaceType.Ethernet;
    }
}
