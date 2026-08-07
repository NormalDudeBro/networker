using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;
using Networker.Core.Models.NetworkConfig;

namespace Networker.Core.Services.NetworkConfig.Parsers;

/// <summary>
/// Parser for Cisco IOS/IOS-XE configurations.
/// Ported from NetworkConfigPro <c>src/core/parsers/config_parser.py</c>
/// <c>CiscoIOSParser</c>.
/// </summary>
public sealed class CiscoIosParser : BaseConfigParser
{
    private static readonly Regex HostnameRegex = new(@"^hostname\s+(\S+)", RegexOptions.Multiline);
    private static readonly Regex DomainRegex = new(@"^ip domain[- ]name\s+(\S+)", RegexOptions.Multiline);
    private static readonly Regex SecretRegex = new(@"^enable secret\s+\d*\s*(\S+)", RegexOptions.Multiline);
    private static readonly Regex NameServerRegex = new(@"^ip name-server\s+(.+)$", RegexOptions.Multiline);
    private static readonly Regex NtpServerRegex = new(@"^ntp server\s+(\S+)", RegexOptions.Multiline);
    private static readonly Regex BannerRegex = new(@"^banner motd\s*(.)(.*?)\1", RegexOptions.Multiline | RegexOptions.Singleline);

    private static readonly Regex VlanRegex = new(@"^vlan\s+(\d+)\s*\n((?:\s+.+\n)*)", RegexOptions.Multiline);
    private static readonly Regex VlanNameRegex = new(@"name\s+(\S+)");

    private static readonly Regex IfaceRegex = new(@"^interface\s+(\S+)\s*\n((?:\s+.+\n)*)", RegexOptions.Multiline);
    private static readonly Regex IfaceDescRegex = new(@"description\s+(.+)");
    private static readonly Regex IfaceIpRegex = new(@"ip address\s+(\d+\.\d+\.\d+\.\d+)\s+(\d+\.\d+\.\d+\.\d+)");
    private static readonly Regex AccessVlanRegex = new(@"switchport access vlan\s+(\d+)");
    private static readonly Regex TrunkAllowedRegex = new(@"switchport trunk allowed vlan\s+(.+)");
    private static readonly Regex TrunkNativeRegex = new(@"switchport trunk native vlan\s+(\d+)");
    private static readonly Regex IfaceMtuRegex = new(@"mtu\s+(\d+)");
    private static readonly Regex ChannelGroupRegex = new(@"channel-group\s+(\d+)");

    private static readonly Regex ExtendedAclRegex = new(@"^ip access-list extended\s+(\S+)\s*\n((?:\s+.+\n)*)", RegexOptions.Multiline);
    private static readonly Regex StandardAclRegex = new(@"^ip access-list standard\s+(\S+)\s*\n((?:\s+.+\n)*)", RegexOptions.Multiline);
    private static readonly Regex RemarkRegex = new(@"^(\d+)?\s*remark\s+(.+)");

    private static readonly Regex RouteRegex = new(
        @"^ip route\s+(\d+\.\d+\.\d+\.\d+)\s+(\d+\.\d+\.\d+\.\d+)\s+(\d+\.\d+\.\d+\.\d+)(?:\s+(\d+))?(?:\s+name\s+(\S+))?",
        RegexOptions.Multiline);

    private static readonly Regex OspfRegex = new(@"^router ospf\s+(\d+)\s*\n((?:\s+.+\n)*)", RegexOptions.Multiline);
    private static readonly Regex RouterIdRegex = new(@"router-id\s+(\S+)");
    private static readonly Regex ReferenceBwRegex = new(@"auto-cost reference-bandwidth\s+(\d+)");
    private static readonly Regex OspfNetworkRegex = new(@"network\s+(\d+\.\d+\.\d+\.\d+)\s+(\d+\.\d+\.\d+\.\d+)\s+area\s+(\d+)");
    private static readonly Regex PassiveIfaceRegex = new(@"passive-interface\s+(\S+)");

    private static readonly Regex BgpRegex = new(@"^router bgp\s+(\d+)\s*\n((?:\s+.+\n)*)", RegexOptions.Multiline);
    private static readonly Regex BgpRouterIdRegex = new(@"bgp router-id\s+(\S+)");
    private static readonly Regex BgpNetworkRegex = new(@"^\s+network\s+(\S+)", RegexOptions.Multiline);
    private static readonly Regex NeighborRegex = new(@"neighbor\s+(\d+\.\d+\.\d+\.\d+)\s+remote-as\s+(\d+)");

    /// <inheritdoc />
    public override bool DetectVendor(string configText)
    {
        var text = NormalizeNewlines(configText);
        var indicators = new[]
        {
            @"^hostname\s+\S+",
            @"^interface\s+(GigabitEthernet|FastEthernet|Ethernet|Loopback)",
            @"^ip route\s+",
            @"^router (ospf|bgp|eigrp)",
            @"^enable secret",
            @"^version\s+\d+\.\d+",
        };

        foreach (var pattern in indicators)
        {
            if (Regex.IsMatch(text, pattern, RegexOptions.Multiline | RegexOptions.IgnoreCase))
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
            // Parse hostname
            var hostname = "";
            var hostnameMatch = HostnameRegex.Match(text);
            if (hostnameMatch.Success)
            {
                hostname = hostnameMatch.Groups[1].Value;
            }
            else
            {
                warnings.Add("No hostname found in configuration");
            }

            // Parse domain name
            string? domainName = null;
            var domainMatch = DomainRegex.Match(text);
            if (domainMatch.Success)
            {
                domainName = domainMatch.Groups[1].Value;
            }

            // Parse enable secret
            string? enableSecret = null;
            var secretMatch = SecretRegex.Match(text);
            if (secretMatch.Success)
            {
                enableSecret = secretMatch.Groups[1].Value;
            }

            // Parse DNS servers (only valid IP addresses are kept)
            var dnsServers = new List<string>();
            foreach (Match match in NameServerRegex.Matches(text))
            {
                foreach (var server in match.Groups[1].Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (IsValidIp(server))
                    {
                        dnsServers.Add(server);
                    }
                }
            }

            // Parse NTP servers (can be IP addresses or hostnames)
            var ntpServers = new List<string>();
            foreach (Match match in NtpServerRegex.Matches(text))
            {
                ntpServers.Add(match.Groups[1].Value);
            }

            // Parse banner
            string? bannerMotd = null;
            var bannerMatch = BannerRegex.Match(text);
            if (bannerMatch.Success)
            {
                bannerMotd = bannerMatch.Groups[2].Value.Trim();
            }

            config = new NetworkDeviceConfig
            {
                Hostname = hostname,
                Vendor = Vendor.CiscoIos,
                DomainName = domainName,
                EnableSecret = enableSecret,
                DnsServers = dnsServers,
                NtpServers = ntpServers,
                BannerMotd = bannerMotd ?? string.Empty,
                Vlans = ParseVlans(text),
                Interfaces = ParseInterfaces(text),
                Acls = ParseAcls(text),
                StaticRoutes = ParseStaticRoutes(text),
                Ospf = ParseOspf(text),
                Bgp = ParseBgp(text),
            };
        }
        catch (Exception ex)
        {
            errors.Add($"Parse error: {ex.Message}");
            return new ParseResult
            {
                Config = null,
                Vendor = Vendor.CiscoIos,
                Errors = errors,
                Warnings = warnings,
            };
        }

        return new ParseResult
        {
            Config = config,
            Vendor = Vendor.CiscoIos,
            Errors = errors,
            Warnings = warnings,
        };
    }

    private static List<Vlan> ParseVlans(string configText)
    {
        var vlans = new List<Vlan>();

        foreach (Match match in VlanRegex.Matches(configText))
        {
            var vlanId = int.Parse(match.Groups[1].Value);
            var vlanBlock = match.Groups[2].Value;

            var name = $"VLAN{vlanId}";
            var nameMatch = VlanNameRegex.Match(vlanBlock);
            if (nameMatch.Success)
            {
                name = nameMatch.Groups[1].Value;
            }

            vlans.Add(new Vlan { VlanId = vlanId, Name = name });
        }

        return vlans;
    }

    private static List<Interface> ParseInterfaces(string configText)
    {
        var interfaces = new List<Interface>();

        foreach (Match match in IfaceRegex.Matches(configText))
        {
            var ifaceName = match.Groups[1].Value;
            var ifaceBlock = match.Groups[2].Value;

            // Description
            var descMatch = IfaceDescRegex.Match(ifaceBlock);
            var description = descMatch.Success ? descMatch.Groups[1].Value.Trim() : string.Empty;

            // IP address
            var ipMatch = IfaceIpRegex.Match(ifaceBlock);
            var ipAddress = ipMatch.Success ? TryParseIp(ipMatch.Groups[1].Value) : null;
            var subnetMask = ipMatch.Success ? TryParseIp(ipMatch.Groups[2].Value) : null;

            // Shutdown status
            var enabled = !ifaceBlock.Contains("shutdown") || ifaceBlock.Contains("no shutdown");

            // VLAN configuration
            int? accessVlan = null;
            var accessVlanMatch = AccessVlanRegex.Match(ifaceBlock);
            if (accessVlanMatch.Success)
            {
                accessVlan = int.Parse(accessVlanMatch.Groups[1].Value);
            }

            // Trunk configuration
            var isTrunk = ifaceBlock.Contains("switchport mode trunk");
            string? trunkAllowedVlans = null;
            int? trunkNativeVlan = null;
            if (isTrunk)
            {
                var allowedMatch = TrunkAllowedRegex.Match(ifaceBlock);
                if (allowedMatch.Success)
                {
                    trunkAllowedVlans = allowedMatch.Groups[1].Value.Trim();
                }

                var nativeMatch = TrunkNativeRegex.Match(ifaceBlock);
                if (nativeMatch.Success)
                {
                    trunkNativeVlan = int.Parse(nativeMatch.Groups[1].Value);
                }
            }

            // MTU
            var mtu = 1500;
            var mtuMatch = IfaceMtuRegex.Match(ifaceBlock);
            if (mtuMatch.Success)
            {
                mtu = int.Parse(mtuMatch.Groups[1].Value);
            }

            // Channel group
            int? channelGroup = null;
            var channelMatch = ChannelGroupRegex.Match(ifaceBlock);
            if (channelMatch.Success)
            {
                channelGroup = int.Parse(channelMatch.Groups[1].Value);
            }

            // The C# model splits the Python fields into modern + legacy
            // properties; both are populated so regeneration is lossless.
            interfaces.Add(new Interface
            {
                Name = ifaceName,
                InterfaceType = DetectInterfaceType(ifaceName),
                Description = description,
                IpAddress = ipAddress,
                SubnetMask = subnetMask,
                Enabled = enabled,
                Mtu = mtu,
                VlanId = accessVlan,
                AccessVlan = accessVlan,
                SwitchportMode = accessVlan is not null ? SwitchportMode.Access : (isTrunk ? SwitchportMode.Trunk : null),
                IsTrunk = isTrunk,
                TrunkAllowedVlans = trunkAllowedVlans,
                TrunkNativeVlan = trunkNativeVlan,
                NativeVlan = trunkNativeVlan,
                ChannelGroup = channelGroup,
            });
        }

        return interfaces;
    }

    private static List<Acl> ParseAcls(string configText)
    {
        var acls = new List<Acl>();

        // Extended ACLs
        foreach (Match match in ExtendedAclRegex.Matches(configText))
        {
            var acl = new Acl { Name = match.Groups[1].Value, IsExtended = true };
            foreach (var line in match.Groups[2].Value.Split('\n'))
            {
                var entry = ParseAclEntry(line.Trim());
                if (entry is not null)
                {
                    acl.Entries.Add(entry);
                }
            }

            acls.Add(acl);
        }

        // Standard ACLs
        foreach (Match match in StandardAclRegex.Matches(configText))
        {
            var acl = new Acl { Name = match.Groups[1].Value, IsExtended = false };
            foreach (var line in match.Groups[2].Value.Split('\n'))
            {
                var entry = ParseAclEntry(line.Trim(), isStandard: true);
                if (entry is not null)
                {
                    acl.Entries.Add(entry);
                }
            }

            acls.Add(acl);
        }

        return acls;
    }

    private static AclEntry? ParseAclEntry(string line, bool isStandard = false)
    {
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith('!'))
        {
            return null;
        }

        // Handle remarks
        var remarkMatch = RemarkRegex.Match(line);
        if (remarkMatch.Success)
        {
            var remarkSequence = remarkMatch.Groups[1].Success ? int.Parse(remarkMatch.Groups[1].Value) : 10;
            return new AclEntry
            {
                Sequence = remarkSequence,
                Action = AclAction.Permit,
                Protocol = AclProtocol.Ip,
                Source = "any",
                Remark = remarkMatch.Groups[2].Value,
            };
        }

        // Parse regular entry
        var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3)
        {
            return null;
        }

        // Check for sequence number
        var idx = 0;
        var sequence = 10;
        if (IsDigits(parts[0]))
        {
            sequence = int.Parse(parts[0]);
            idx = 1;
        }

        var actionStr = parts[idx].ToLowerInvariant();
        AclAction action;
        if (actionStr == "permit")
        {
            action = AclAction.Permit;
        }
        else if (actionStr == "deny")
        {
            action = AclAction.Deny;
        }
        else
        {
            return null;
        }

        idx++;

        // Protocol
        var protocolStr = idx < parts.Length ? parts[idx].ToLowerInvariant() : "ip";
        var protocol = protocolStr switch
        {
            "tcp" => AclProtocol.Tcp,
            "udp" => AclProtocol.Udp,
            "icmp" => AclProtocol.Icmp,
            _ => AclProtocol.Ip,
        };
        idx++;

        // Source
        var source = idx < parts.Length ? parts[idx] : "any";
        idx++;

        // Source wildcard
        var sourceWildcard = "0.0.0.0";
        if (idx < parts.Length && IsValidIp(parts[idx]))
        {
            sourceWildcard = parts[idx];
            idx++;
        }

        // For extended ACLs, get destination
        var destination = "any";
        var destinationWildcard = "0.0.0.0";
        if (!isStandard && idx < parts.Length)
        {
            destination = parts[idx];
            idx++;
            if (idx < parts.Length && IsValidIp(parts[idx]))
            {
                destinationWildcard = parts[idx];
            }
        }

        return new AclEntry
        {
            Sequence = sequence,
            Action = action,
            Protocol = protocol,
            Source = source,
            SourceWildcard = sourceWildcard,
            Destination = destination,
            DestinationWildcard = destinationWildcard,
            Log = line.ToLowerInvariant().Contains("log"),
        };
    }

    private static List<StaticRoute> ParseStaticRoutes(string configText)
    {
        var routes = new List<StaticRoute>();

        foreach (Match match in RouteRegex.Matches(configText))
        {
            var adminDistance = match.Groups[4].Success ? int.Parse(match.Groups[4].Value) : 1;
            var name = match.Groups[5].Success ? match.Groups[5].Value : "";

            routes.Add(new StaticRoute
            {
                Destination = match.Groups[1].Value,
                Mask = match.Groups[2].Value,
                NextHop = match.Groups[3].Value,
                AdminDistance = adminDistance,
                Name = name,
            });
        }

        return routes;
    }

    private static OspfConfig? ParseOspf(string configText)
    {
        var ospfMatch = OspfRegex.Match(configText);
        if (!ospfMatch.Success)
        {
            return null;
        }

        var processId = int.Parse(ospfMatch.Groups[1].Value);
        var ospfBlock = ospfMatch.Groups[2].Value;

        var ospf = new OspfConfig { ProcessId = processId };

        // Router ID
        var ridMatch = RouterIdRegex.Match(ospfBlock);
        if (ridMatch.Success)
        {
            ospf = ospf with { RouterId = ridMatch.Groups[1].Value };
        }

        // Reference bandwidth
        var bwMatch = ReferenceBwRegex.Match(ospfBlock);
        if (bwMatch.Success)
        {
            ospf = ospf with { ReferenceBandwidth = int.Parse(bwMatch.Groups[1].Value) };
        }

        // Networks
        foreach (Match match in OspfNetworkRegex.Matches(ospfBlock))
        {
            ospf.Networks.Add(new OspfNetwork
            {
                Network = match.Groups[1].Value,
                Wildcard = match.Groups[2].Value,
                Area = int.Parse(match.Groups[3].Value),
            });
        }

        // Passive interfaces
        foreach (Match match in PassiveIfaceRegex.Matches(ospfBlock))
        {
            ospf.PassiveInterfaces.Add(match.Groups[1].Value);
        }

        // Default information originate
        ospf = ospf with { DefaultInformationOriginate = ospfBlock.Contains("default-information originate") };

        return ospf;
    }

    private static BgpConfig? ParseBgp(string configText)
    {
        var bgpMatch = BgpRegex.Match(configText);
        if (!bgpMatch.Success)
        {
            return null;
        }

        var localAs = int.Parse(bgpMatch.Groups[1].Value);
        var bgpBlock = bgpMatch.Groups[2].Value;

        var bgp = new BgpConfig { LocalAs = localAs };

        // Router ID
        var ridMatch = RouterIdRegex.Match(bgpBlock);
        if (ridMatch.Success)
        {
            bgp = bgp with { RouterId = ridMatch.Groups[1].Value };
        }
        else
        {
            var ridMatch2 = BgpRouterIdRegex.Match(bgpBlock);
            if (ridMatch2.Success)
            {
                bgp = bgp with { RouterId = ridMatch2.Groups[1].Value };
            }
        }

        // Log neighbor changes
        bgp = bgp with { LogNeighborChanges = bgpBlock.Contains("log-neighbor-changes") };

        // Networks
        foreach (Match match in BgpNetworkRegex.Matches(bgpBlock))
        {
            bgp.Networks.Add(match.Groups[1].Value);
        }

        // Neighbors
        foreach (Match match in NeighborRegex.Matches(bgpBlock))
        {
            var neighborIp = match.Groups[1].Value;
            var remoteAs = int.Parse(match.Groups[2].Value);

            string? description = null;
            var descMatch = new Regex($@"neighbor\s+{Regex.Escape(neighborIp)}\s+description\s+(.+)").Match(bgpBlock);
            if (descMatch.Success)
            {
                description = descMatch.Groups[1].Value.Trim();
            }

            string? password = null;
            var passMatch = new Regex($@"neighbor\s+{Regex.Escape(neighborIp)}\s+password\s+(\S+)").Match(bgpBlock);
            if (passMatch.Success)
            {
                password = passMatch.Groups[1].Value;
            }

            string? updateSource = null;
            var sourceMatch = new Regex($@"neighbor\s+{Regex.Escape(neighborIp)}\s+update-source\s+(\S+)").Match(bgpBlock);
            if (sourceMatch.Success)
            {
                updateSource = sourceMatch.Groups[1].Value;
            }

            var ebgpMultihop = 0;
            var multihopMatch = new Regex($@"neighbor\s+{Regex.Escape(neighborIp)}\s+ebgp-multihop\s+(\d+)").Match(bgpBlock);
            if (multihopMatch.Success)
            {
                ebgpMultihop = int.Parse(multihopMatch.Groups[1].Value);
            }

            bgp.Neighbors.Add(new BgpNeighbor
            {
                IpAddress = neighborIp,
                RemoteAs = remoteAs,
                Description = description ?? string.Empty,
                Password = password,
                UpdateSource = updateSource,
                EbgpMultihop = ebgpMultihop,
            });
        }

        return bgp;
    }

    private static InterfaceType DetectInterfaceType(string name)
    {
        var nameLower = name.ToLowerInvariant();
        if (nameLower.Contains("gigabit") || nameLower.StartsWith("gi", StringComparison.Ordinal))
        {
            return InterfaceType.Gigabit;
        }
        else if (nameLower.Contains("tengigabit") || nameLower.StartsWith("te", StringComparison.Ordinal))
        {
            return InterfaceType.TenGigabit;
        }
        else if (nameLower.Contains("loopback") || nameLower.StartsWith("lo", StringComparison.Ordinal))
        {
            return InterfaceType.Loopback;
        }
        else if (nameLower.Contains("vlan"))
        {
            return InterfaceType.Vlan;
        }
        else if (nameLower.Contains("port-channel") || nameLower.StartsWith("po", StringComparison.Ordinal))
        {
            return InterfaceType.PortChannel;
        }
        else if (nameLower.Contains("mgmt") || nameLower.Contains("management"))
        {
            return InterfaceType.Management;
        }

        return InterfaceType.Ethernet;
    }

    private static bool IsDigits(string value)
    {
        if (value.Length == 0)
        {
            return false;
        }

        foreach (var c in value)
        {
            if (c is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }
}
