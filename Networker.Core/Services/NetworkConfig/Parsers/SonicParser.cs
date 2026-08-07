using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using Networker.Core.Models.NetworkConfig;

namespace Networker.Core.Services.NetworkConfig.Parsers;

/// <summary>
/// Parser for SONiC config_db.json configurations.
/// Ported from NetworkConfigPro <c>src/core/parsers/config_parser.py</c>
/// <c>SONiCParser</c>.
///
/// Deviation from Python: <see cref="JsonDocumentOptions.AllowTrailingCommas"/>
/// is enabled because the C# SONiC generator emits a trailing comma in the
/// <c>ACL_RULE</c> table (frozen by golden parity), which Python's strict
/// <c>json.loads</c> would reject. Duplicate object keys are also tolerated
/// (last value wins, matching Python dict semantics) because the C# SONiC
/// generator emits a duplicate "Ethernet1" PORT entry.
/// </summary>
public sealed class SonicParser : BaseConfigParser
{
    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
    };

    private static readonly string[] SonicTables =
    [
        "DEVICE_METADATA",
        "PORT",
        "INTERFACE",
        "VLAN",
        "BGP_NEIGHBOR",
        "LOOPBACK_INTERFACE",
    ];

    private static readonly Regex VlanIdRegex = new(@"(\d+)");
    private static readonly Regex RuleSequenceRegex = new(@"RULE_(\d+)");

    /// <inheritdoc />
    public override bool DetectVendor(string configText)
    {
        try
        {
            using var doc = JsonDocument.Parse(RemoveDuplicateKeys(configText), JsonOptions);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            // Check if any SONiC-specific tables exist
            foreach (var table in SonicTables)
            {
                if (doc.RootElement.TryGetProperty(table, out _))
                {
                    return true;
                }
            }

            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public override ParseResult Parse(string configText)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        NetworkDeviceConfig? config = null;
        try
        {
            using var doc = JsonDocument.Parse(RemoveDuplicateKeys(configText), JsonOptions);
            var root = doc.RootElement;

            // Parse DEVICE_METADATA
            var (hostname, bgpAsn) = ParseDeviceMetadata(root, warnings);

            // Parse interfaces (PORT, INTERFACE, LOOPBACK_INTERFACE)
            var interfaces = ParseInterfaces(root);

            // Parse VLANs
            var vlans = ParseVlans(root);

            // Parse VLAN members and update interfaces
            ParseVlanMembers(root, interfaces);

            // Parse BGP
            var bgp = ParseBgp(root, bgpAsn);

            // Parse static routes
            var staticRoutes = ParseStaticRoutes(root);

            // Parse ACLs
            var acls = ParseAcls(root);

            // Parse NTP servers
            var ntpServers = ReadTableKeys(root, "NTP_SERVER");

            // Parse DNS servers
            var dnsServers = ReadTableKeys(root, "DNS_NAMESERVER");

            config = new NetworkDeviceConfig
            {
                Hostname = hostname,
                Vendor = Vendor.Sonic,
                Interfaces = interfaces,
                Vlans = vlans,
                Bgp = bgp,
                StaticRoutes = staticRoutes,
                Acls = acls,
                NtpServers = ntpServers,
                DnsServers = dnsServers,
            };
        }
        catch (JsonException ex)
        {
            errors.Add($"Invalid JSON: {ex.Message}");
        }
        catch (Exception ex)
        {
            errors.Add($"Parse error: {ex.Message}");
        }

        return new ParseResult
        {
            Config = config,
            Vendor = Vendor.Sonic,
            Errors = errors,
            Warnings = warnings,
        };
    }

    private static (string Hostname, int? BgpAsn) ParseDeviceMetadata(JsonElement root, List<string> warnings)
    {
        var metadata = ReadTable(root, "DEVICE_METADATA");
        var localhost = metadata.TryGetValue("localhost", out var localhostElement) && localhostElement.ValueKind == JsonValueKind.Object
            ? localhostElement
            : default;

        var hostname = "";
        if (localhost.ValueKind == JsonValueKind.Object && localhost.TryGetProperty("hostname", out var hostnameElement))
        {
            hostname = GetString(hostnameElement) ?? "";
        }
        else
        {
            warnings.Add("No hostname found in DEVICE_METADATA");
        }

        // BGP ASN is often stored in metadata
        int? bgpAsn = null;
        if (localhost.ValueKind == JsonValueKind.Object && localhost.TryGetProperty("bgp_asn", out var asnElement)
            && TryGetInt(asnElement, out var asn))
        {
            bgpAsn = asn;
        }

        return (hostname, bgpAsn);
    }

    private static List<Interface> ParseInterfaces(JsonElement root)
    {
        var interfaces = new List<Interface>();
        var processedNames = new HashSet<string>();

        // Parse PORT table (physical interfaces)
        foreach (var (portName, portConfig) in ReadTable(root, "PORT"))
        {
            var adminStatus = GetStringProperty(portConfig, "admin_status", "up");
            var description = GetStringProperty(portConfig, "description", "");
            var mtu = 1500;
            if (portConfig.TryGetProperty("mtu", out var mtuElement) && TryGetInt(mtuElement, out var parsedMtu))
            {
                mtu = parsedMtu;
            }

            var speed = portConfig.TryGetProperty("speed", out var speedElement) ? GetString(speedElement) : null;

            interfaces.Add(new Interface
            {
                Name = portName,
                InterfaceType = DetectInterfaceType(portName),
                Enabled = adminStatus == "up",
                Description = description,
                Mtu = mtu,
                Speed = speed,
            });
            processedNames.Add(portName);
        }

        // Parse INTERFACE table (L3 addresses on physical interfaces)
        foreach (var (key, _) in ReadTable(root, "INTERFACE"))
        {
            var sep = key.IndexOf('|');
            if (sep < 0)
            {
                continue;
            }

            var ifaceName = key[..sep];
            var ipPrefix = key[(sep + 1)..];

            // Find existing interface or create new one
            var existing = interfaces.FirstOrDefault(i => i.Name == ifaceName);
            if (existing is not null)
            {
                if (TrySplitIpPrefix(ipPrefix, out var ip, out var mask))
                {
                    interfaces[interfaces.IndexOf(existing)] = existing with
                    {
                        IpAddress = TryParseIp(ip),
                        SubnetMask = TryParseIp(mask),
                    };
                }
            }
            else if (!processedNames.Contains(ifaceName))
            {
                if (TrySplitIpPrefix(ipPrefix, out var ip, out var mask))
                {
                    interfaces.Add(new Interface
                    {
                        Name = ifaceName,
                        InterfaceType = DetectInterfaceType(ifaceName),
                        IpAddress = TryParseIp(ip),
                        SubnetMask = TryParseIp(mask),
                    });
                    processedNames.Add(ifaceName);
                }
            }
        }

        // Parse LOOPBACK_INTERFACE table
        foreach (var (key, _) in ReadTable(root, "LOOPBACK_INTERFACE"))
        {
            var sep = key.IndexOf('|');
            if (sep < 0)
            {
                continue;
            }

            var ifaceName = key[..sep];
            var ipPrefix = key[(sep + 1)..];
            if (!processedNames.Contains(ifaceName) && TrySplitIpPrefix(ipPrefix, out var ip, out var mask))
            {
                interfaces.Add(new Interface
                {
                    Name = ifaceName,
                    InterfaceType = InterfaceType.Loopback,
                    IpAddress = TryParseIp(ip),
                    SubnetMask = TryParseIp(mask),
                });
                processedNames.Add(ifaceName);
            }
        }

        // Parse VLAN_INTERFACE table
        foreach (var (key, _) in ReadTable(root, "VLAN_INTERFACE"))
        {
            var sep = key.IndexOf('|');
            if (sep < 0)
            {
                continue;
            }

            var ifaceName = key[..sep];
            var ipPrefix = key[(sep + 1)..];
            if (!processedNames.Contains(ifaceName) && TrySplitIpPrefix(ipPrefix, out var ip, out var mask))
            {
                interfaces.Add(new Interface
                {
                    Name = ifaceName,
                    InterfaceType = InterfaceType.Vlan,
                    IpAddress = TryParseIp(ip),
                    SubnetMask = TryParseIp(mask),
                });
                processedNames.Add(ifaceName);
            }
        }

        // Parse PORTCHANNEL table
        foreach (var (pcName, pcConfig) in ReadTable(root, "PORTCHANNEL"))
        {
            if (processedNames.Contains(pcName))
            {
                continue;
            }

            var adminStatus = GetStringProperty(pcConfig, "admin_status", "up");
            var mtu = 1500;
            if (pcConfig.TryGetProperty("mtu", out var mtuElement) && TryGetInt(mtuElement, out var parsedMtu))
            {
                mtu = parsedMtu;
            }

            interfaces.Add(new Interface
            {
                Name = pcName,
                InterfaceType = InterfaceType.PortChannel,
                Enabled = adminStatus == "up",
                Mtu = mtu,
            });
            processedNames.Add(pcName);
        }

        // Parse PORTCHANNEL_INTERFACE table for IPs
        foreach (var (key, _) in ReadTable(root, "PORTCHANNEL_INTERFACE"))
        {
            var sep = key.IndexOf('|');
            if (sep < 0)
            {
                continue;
            }

            var ifaceName = key[..sep];
            var ipPrefix = key[(sep + 1)..];
            var existing = interfaces.FirstOrDefault(i => i.Name == ifaceName);
            if (existing is not null && TrySplitIpPrefix(ipPrefix, out var ip, out var mask))
            {
                interfaces[interfaces.IndexOf(existing)] = existing with
                {
                    IpAddress = TryParseIp(ip),
                    SubnetMask = TryParseIp(mask),
                };
            }
        }

        return interfaces;
    }

    private static List<Vlan> ParseVlans(JsonElement root)
    {
        var vlans = new List<Vlan>();

        foreach (var (vlanName, vlanConfig) in ReadTable(root, "VLAN"))
        {
            // Extract VLAN ID from name (e.g., "Vlan1000" -> 1000)
            var idMatch = VlanIdRegex.Match(vlanName);
            int vlanId;
            if (idMatch.Success)
            {
                vlanId = int.Parse(idMatch.Groups[1].Value);
            }
            else if (vlanConfig.TryGetProperty("vlanid", out var vlanIdElement))
            {
                if (!TryGetInt(vlanIdElement, out vlanId))
                {
                    throw new FormatException($"Invalid vlanid in VLAN table: {GetString(vlanIdElement) ?? vlanIdElement.GetRawText()}");
                }
            }
            else
            {
                continue;
            }

            vlans.Add(new Vlan { VlanId = vlanId, Name = vlanName });
        }

        return vlans;
    }

    private static void ParseVlanMembers(JsonElement root, List<Interface> interfaces)
    {
        foreach (var (key, memberConfig) in ReadTable(root, "VLAN_MEMBER"))
        {
            var sep = key.IndexOf('|');
            if (sep < 0)
            {
                continue;
            }

            var vlanName = key[..sep];
            var ifaceName = key[(sep + 1)..];
            var taggingMode = GetStringProperty(memberConfig, "tagging_mode", "untagged");

            // Extract VLAN ID
            var vlanIdMatch = VlanIdRegex.Match(vlanName);
            if (!vlanIdMatch.Success)
            {
                continue;
            }

            var vlanId = int.Parse(vlanIdMatch.Groups[1].Value);

            // Find the interface
            var index = interfaces.FindIndex(i => i.Name == ifaceName);
            if (index < 0)
            {
                continue;
            }

            var iface = interfaces[index];
            if (taggingMode == "untagged")
            {
                interfaces[index] = iface with
                {
                    VlanId = vlanId,
                    SwitchportMode = SwitchportMode.Access,
                };
            }
            else if (taggingMode == "tagged")
            {
                var trunkAllowed = iface.TrunkAllowedVlans is { Length: > 0 } allowed ? $"{allowed},{vlanId}" : vlanId.ToString();
                interfaces[index] = iface with
                {
                    IsTrunk = true,
                    SwitchportMode = SwitchportMode.Trunk,
                    TrunkAllowedVlans = trunkAllowed,
                };
            }
        }
    }

    private static BgpConfig? ParseBgp(JsonElement root, int? metadataAsn)
    {
        var neighborTable = ReadTable(root, "BGP_NEIGHBOR");
        if (neighborTable.Count == 0)
        {
            return null;
        }

        // Get ASN from DEVICE_METADATA if available
        var localAs = metadataAsn ?? 65000;

        var bgp = new BgpConfig { LocalAs = localAs };

        foreach (var (neighborIp, neighborConfig) in neighborTable)
        {
            // Skip IPv6 neighbors for now
            if (neighborIp.Contains(':'))
            {
                continue;
            }

            // SONiC uses rmt_asn, but support asn for compatibility
            int remoteAs;
            if (neighborConfig.TryGetProperty("rmt_asn", out var rmtAsnElement))
            {
                if (!TryGetInt(rmtAsnElement, out remoteAs))
                {
                    continue;
                }
            }
            else if (neighborConfig.TryGetProperty("asn", out var asnElement))
            {
                if (!TryGetInt(asnElement, out remoteAs))
                {
                    continue;
                }
            }
            else
            {
                remoteAs = 0;
            }

            var description = GetStringProperty(neighborConfig, "name", "");

            // Update source (local_addr in SONiC)
            string? updateSource = null;
            if (neighborConfig.TryGetProperty("local_addr", out var localAddrElement))
            {
                updateSource = GetString(localAddrElement);
            }

            bgp.Neighbors.Add(new BgpNeighbor
            {
                IpAddress = neighborIp,
                RemoteAs = remoteAs,
                Description = description,
                UpdateSource = updateSource,
            });
        }

        return bgp.Neighbors.Count > 0 ? bgp : null;
    }

    private static List<StaticRoute> ParseStaticRoutes(JsonElement root)
    {
        var routes = new List<StaticRoute>();

        foreach (var (prefix, routeConfig) in ReadTable(root, "STATIC_ROUTE"))
        {
            var slash = prefix.IndexOf('/');
            if (slash < 0)
            {
                continue;
            }

            var destination = prefix[..slash];
            var mask = CidrToNetmask(int.Parse(prefix[(slash + 1)..]));

            var nextHop = routeConfig.TryGetProperty("nexthop", out var nextHopElement) ? GetString(nextHopElement) ?? "" : "";
            if (nextHop.Length == 0)
            {
                continue;
            }

            // Admin distance
            var adminDistance = 1;
            if (routeConfig.TryGetProperty("distance", out var distanceElement) && TryGetInt(distanceElement, out var parsedDistance))
            {
                adminDistance = parsedDistance;
            }

            routes.Add(new StaticRoute
            {
                Destination = destination,
                Mask = mask,
                NextHop = nextHop,
                AdminDistance = adminDistance,
            });
        }

        return routes;
    }

    private static List<Acl> ParseAcls(JsonElement root)
    {
        var acls = new List<Acl>();
        var aclTable = ReadTable(root, "ACL_TABLE");
        var aclRuleTable = ReadTable(root, "ACL_RULE");

        foreach (var (aclName, aclConfig) in aclTable)
        {
            var isExtended = GetStringProperty(aclConfig, "type", "L3") == "L3";
            var acl = new Acl { Name = aclName, IsExtended = isExtended };

            // Find rules for this ACL
            foreach (var (ruleKey, ruleConfig) in aclRuleTable)
            {
                if (!ruleKey.StartsWith(aclName + "|", StringComparison.Ordinal))
                {
                    continue;
                }

                // Extract sequence from rule name
                var seqMatch = RuleSequenceRegex.Match(ruleKey);
                var sequence = seqMatch.Success ? int.Parse(seqMatch.Groups[1].Value) : 10;

                // Determine action
                var packetAction = GetStringProperty(ruleConfig, "PACKET_ACTION", "DROP");
                var action = packetAction is "FORWARD" or "ACCEPT" ? AclAction.Permit : AclAction.Deny;

                // Determine protocol
                var ipProtocol = GetStringProperty(ruleConfig, "IP_PROTOCOL", "");
                var protocol = ipProtocol switch
                {
                    "6" => AclProtocol.Tcp,
                    "17" => AclProtocol.Udp,
                    "1" => AclProtocol.Icmp,
                    _ => AclProtocol.Ip,
                };

                // Source
                var srcIp = GetStringProperty(ruleConfig, "SRC_IP", "any");
                string source;
                string sourceWildcard;
                if (TrySplitCidr(srcIp, out var srcAddr, out var srcPrefix))
                {
                    source = srcAddr;
                    sourceWildcard = CidrToWildcard(srcPrefix);
                }
                else
                {
                    source = srcIp;
                    sourceWildcard = "0.0.0.0";
                }

                // Destination
                var dstIp = GetStringProperty(ruleConfig, "DST_IP", "any");
                string destination;
                string destinationWildcard;
                if (TrySplitCidr(dstIp, out var dstAddr, out var dstPrefix))
                {
                    destination = dstAddr;
                    destinationWildcard = CidrToWildcard(dstPrefix);
                }
                else
                {
                    destination = dstIp;
                    destinationWildcard = "0.0.0.0";
                }

                acl.Entries.Add(new AclEntry
                {
                    Sequence = sequence,
                    Action = action,
                    Protocol = protocol,
                    Source = source,
                    SourceWildcard = sourceWildcard,
                    Destination = destination,
                    DestinationWildcard = destinationWildcard,
                    SourcePort = ruleConfig.TryGetProperty("L4_SRC_PORT", out var srcPortElement) ? GetStringOrNumber(srcPortElement) : null,
                    DestinationPort = ruleConfig.TryGetProperty("L4_DST_PORT", out var dstPortElement) ? GetStringOrNumber(dstPortElement) : null,
                });
            }

            // Sort entries by sequence
            acl.Entries.Sort((a, b) => a.Sequence.CompareTo(b.Sequence));
            acls.Add(acl);
        }

        return acls;
    }

    /// <summary>
    /// Reads a table as a dictionary with last-value-wins semantics,
    /// matching Python's <c>json.loads</c> dict behavior on duplicate keys.
    /// </summary>
    private static Dictionary<string, JsonElement> ReadTable(JsonElement root, string tableName)
    {
        var result = new Dictionary<string, JsonElement>();
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(tableName, out var table)
            && table.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in table.EnumerateObject())
            {
                result[prop.Name] = prop.Value;
            }
        }

        return result;
    }

    private static List<string> ReadTableKeys(JsonElement root, string tableName)
    {
        var keys = new List<string>();
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(tableName, out var table)
            && table.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in table.EnumerateObject())
            {
                if (!keys.Contains(prop.Name))
                {
                    keys.Add(prop.Name);
                }
            }
        }

        return keys;
    }

    /// <summary>
    /// Rewrites JSON text, keeping the last value for duplicate object keys
    /// (matching Python's <c>json.loads</c> dict semantics) while preserving
    /// first-seen key order. Required because the C# SONiC generator emits a
    /// duplicate "Ethernet1" PORT entry (frozen by golden parity), which
    /// <see cref="JsonDocument"/> rejects with a JsonException.
    /// </summary>
    private static string RemoveDuplicateKeys(string json)
    {
        var pos = 0;
        return RewriteValue(json, ref pos);
    }

    private static string RewriteValue(string json, ref int pos)
    {
        SkipWhitespace(json, ref pos);
        if (pos >= json.Length)
        {
            return "";
        }

        return json[pos] switch
        {
            '{' => RewriteObject(json, ref pos),
            '[' => RewriteArray(json, ref pos),
            '"' => CopyStringToken(json, ref pos),
            _ => CopyScalarToken(json, ref pos),
        };
    }

    private static string RewriteObject(string json, ref int pos)
    {
        pos++; // consume '{'

        var values = new List<string>();
        var indexByKey = new Dictionary<string, int>(StringComparer.Ordinal);

        SkipWhitespace(json, ref pos);
        while (pos < json.Length)
        {
            SkipWhitespace(json, ref pos);
            if (pos >= json.Length || json[pos] == '}')
            {
                break; // also covers a trailing comma before the closing brace
            }

            var key = CopyStringToken(json, ref pos);
            SkipWhitespace(json, ref pos);
            if (pos < json.Length && json[pos] == ':')
            {
                pos++;
            }

            var value = RewriteValue(json, ref pos);

            if (indexByKey.TryGetValue(key, out var existingIndex))
            {
                // Duplicate key: keep the last value (Python dict semantics)
                values[existingIndex] = $"{key}:{value}";
            }
            else
            {
                indexByKey[key] = values.Count;
                values.Add($"{key}:{value}");
            }

            SkipWhitespace(json, ref pos);
            if (pos < json.Length && json[pos] == ',')
            {
                pos++;
            }
        }

        if (pos < json.Length)
        {
            pos++; // consume '}'
        }

        return "{" + string.Join(',', values) + "}";
    }

    private static string RewriteArray(string json, ref int pos)
    {
        pos++; // consume '['

        var values = new List<string>();

        SkipWhitespace(json, ref pos);
        while (pos < json.Length && json[pos] != ']')
        {
            values.Add(RewriteValue(json, ref pos));
            SkipWhitespace(json, ref pos);
            if (pos < json.Length && json[pos] == ',')
            {
                pos++;
            }
        }

        if (pos < json.Length)
        {
            pos++; // consume ']'
        }

        return "[" + string.Join(',', values) + "]";
    }

    private static void SkipWhitespace(string json, ref int pos)
    {
        while (pos < json.Length && char.IsWhiteSpace(json[pos]))
        {
            pos++;
        }
    }

    private static string CopyStringToken(string json, ref int pos)
    {
        var start = pos;
        pos++; // consume opening quote
        while (pos < json.Length)
        {
            if (json[pos] == '\\')
            {
                pos += 2;
                continue;
            }

            if (json[pos] == '"')
            {
                pos++;
                break;
            }

            pos++;
        }

        return json[start..pos];
    }

    private static string CopyScalarToken(string json, ref int pos)
    {
        var start = pos;
        while (pos < json.Length && json[pos] is not (',' or '}' or ']') && !char.IsWhiteSpace(json[pos]))
        {
            pos++;
        }

        return json[start..pos];
    }

    private static string GetString(JsonElement element) =>
        element.ValueKind == JsonValueKind.String ? element.GetString() ?? "" : "";

    private static string? GetStringOrNumber(JsonElement element) =>
        element.ValueKind == JsonValueKind.String ? element.GetString()
        : element.ValueKind == JsonValueKind.Number ? element.GetRawText()
        : null;

    private static string GetStringProperty(JsonElement obj, string propertyName, string defaultValue) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(propertyName, out var element)
            ? GetString(element)
            : defaultValue;

    private static bool TryGetInt(JsonElement element, out int value)
    {
        if (element.ValueKind == JsonValueKind.Number)
        {
            value = element.GetInt32();
            return true;
        }

        if (element.ValueKind == JsonValueKind.String && int.TryParse(element.GetString(), out value))
        {
            return true;
        }

        value = 0;
        return false;
    }

    private static bool TrySplitIpPrefix(string ipPrefix, out string ip, out string mask)
    {
        var slash = ipPrefix.IndexOf('/');
        if (slash < 0)
        {
            ip = "";
            mask = "";
            return false;
        }

        ip = ipPrefix[..slash];
        mask = CidrToNetmask(int.Parse(ipPrefix[(slash + 1)..]));
        return true;
    }

    private static bool TrySplitCidr(string cidr, out string address, out int prefix)
    {
        var slash = cidr.IndexOf('/');
        if (slash < 0)
        {
            address = "";
            prefix = 0;
            return false;
        }

        address = cidr[..slash];
        prefix = int.Parse(cidr[(slash + 1)..]);
        return true;
    }

    private static InterfaceType DetectInterfaceType(string name)
    {
        var nameLower = name.ToLowerInvariant();
        if (nameLower.StartsWith("ethernet", StringComparison.Ordinal))
        {
            return InterfaceType.Ethernet;
        }
        else if (nameLower.StartsWith("loopback", StringComparison.Ordinal))
        {
            return InterfaceType.Loopback;
        }
        else if (nameLower.StartsWith("portchannel", StringComparison.Ordinal))
        {
            return InterfaceType.PortChannel;
        }
        else if (nameLower.StartsWith("vlan", StringComparison.Ordinal))
        {
            return InterfaceType.Vlan;
        }
        else if (nameLower.StartsWith("eth", StringComparison.Ordinal) || nameLower.StartsWith("mgmt", StringComparison.Ordinal))
        {
            return InterfaceType.Management;
        }

        return InterfaceType.Ethernet;
    }
}
