using System.Net;
using Networker.Core.Models.NetworkConfig;
using Networker.Core.NetTools.Config;

namespace Networker.Core.Services.NetworkConfig;

/// <summary>
/// Generates vendor-specific configuration using the ported Jinja2 templates.
/// </summary>
public sealed class NetworkConfigGenerator : IConfigGenerator
{
    private static readonly Vendor[] SupportedVendors =
    {
        Vendor.CiscoIos,
        Vendor.CiscoNxos,
        Vendor.AristaEos,
        Vendor.JuniperJunos,
        Vendor.Sonic,
        Vendor.FortinetFortigate,
    };

    /// <inheritdoc />
    public string Generate(NetworkDeviceConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        return config.Vendor switch
        {
            Vendor.CiscoIos => CiscoIosConfigTemplate.Render(config),
            Vendor.CiscoNxos => CiscoNxosConfigTemplate.Render(config),
            Vendor.AristaEos => AristaEosConfigTemplate.Render(config),
            Vendor.JuniperJunos => JuniperJunosConfigTemplate.Render(config),
            Vendor.Sonic => SonicConfigTemplate.Render(config),
            Vendor.FortinetFortigate => FortinetFortiGateConfigTemplate.Render(config),
            _ => throw new ArgumentOutOfRangeException(nameof(config), config.Vendor, $"Unsupported vendor: {config.Vendor}"),
        };
    }

    /// <inheritdoc />
    public string GenerateFromDict(Vendor vendor, IReadOnlyDictionary<string, object> configDict)
    {
        ArgumentNullException.ThrowIfNull(configDict);

        return Generate(DictionaryToConfig(vendor, configDict));
    }

    /// <inheritdoc />
    public IReadOnlyList<Vendor> GetSupportedVendors() => SupportedVendors;

    private static NetworkDeviceConfig DictionaryToConfig(Vendor vendor, IReadOnlyDictionary<string, object> d)
    {
        return new NetworkDeviceConfig
        {
            Hostname = GetString(d, "hostname", "host"),
            Vendor = vendor,
            Interfaces = GetDictList(d, "interfaces").Select(ParseInterface).ToList(),
            Vlans = GetDictList(d, "vlans").Select(ParseVlan).ToList(),
            Acls = GetDictList(d, "acls").Select(ParseAcl).ToList(),
            StaticRoutes = GetDictList(d, "static_routes").Select(ParseStaticRoute).ToList(),
            Ospf = TryGetDict(d, "ospf", out var ospf) ? ParseOspf(ospf) : null,
            Eigrp = TryGetDict(d, "eigrp", out var eigrp) ? ParseEigrp(eigrp) : null,
            Bgp = TryGetDict(d, "bgp", out var bgp) ? ParseBgp(bgp) : null,
            Stp = TryGetDict(d, "stp", out var stp) ? ParseStp(stp) : null,
            PrefixLists = GetDictList(d, "prefix_lists").Select(ParsePrefixList).ToList(),
            RouteMaps = GetDictList(d, "route_maps").Select(ParseRouteMap).ToList(),
            EnableSecret = GetOptionalString(d, "enable_secret"),
            DomainName = GetOptionalString(d, "domain_name"),
            DnsServers = GetStringList(d, "dns_servers"),
            NtpServers = GetStringList(d, "ntp_servers"),
            BannerMotd = GetString(d, "banner_motd"),
        };
    }

    private static Interface ParseInterface(IReadOnlyDictionary<string, object> d)
    {
        var name = GetString(d, "name");
        return new Interface
        {
            Name = name,
            InterfaceType = GetInterfaceType(d, name),
            Description = GetString(d, "description"),
            IpAddress = TryParseIp(GetOptionalString(d, "ip_address")),
            SubnetMask = TryParseIp(GetOptionalString(d, "subnet_mask")),
            Enabled = GetBool(d, "enabled", true),
            Speed = GetOptionalString(d, "speed"),
            Duplex = GetOptionalString(d, "duplex"),
            Mtu = GetInt(d, "mtu", 1500),
            SwitchportMode = GetSwitchportMode(GetOptionalString(d, "switchport_mode")),
            AccessVlan = GetNullableInt(d, "access_vlan"),
            VoiceVlan = GetNullableInt(d, "voice_vlan"),
            TrunkAllowedVlans = GetOptionalString(d, "trunk_allowed_vlans"),
            TrunkNativeVlan = GetNullableInt(d, "trunk_native_vlan"),
            VlanId = GetNullableInt(d, "vlan_id"),
            IsTrunk = GetBool(d, "is_trunk", false),
            NativeVlan = GetNullableInt(d, "native_vlan"),
            ChannelGroup = GetNullableInt(d, "channel_group"),
            ChannelGroupMode = GetOptionalString(d, "channel_group_mode"),
        };
    }

    private static Vlan ParseVlan(IReadOnlyDictionary<string, object> d)
    {
        return new Vlan
        {
            VlanId = GetInt(d, "vlan_id", 1),
            Name = GetString(d, "name"),
            Description = GetString(d, "description"),
            State = GetString(d, "state", "active"),
        };
    }

    private static Acl ParseAcl(IReadOnlyDictionary<string, object> d)
    {
        return new Acl
        {
            Name = GetString(d, "name"),
            IsExtended = GetBool(d, "is_extended", true),
            Entries = GetDictList(d, "entries").Select(ParseAclEntry).ToList(),
        };
    }

    private static AclEntry ParseAclEntry(IReadOnlyDictionary<string, object> d)
    {
        return new AclEntry
        {
            Sequence = GetInt(d, "sequence", 10),
            Action = GetAclAction(GetString(d, "action", "permit")),
            Protocol = GetAclProtocol(GetString(d, "protocol", "ip")),
            Source = GetString(d, "source", "any"),
            SourceWildcard = GetString(d, "source_wildcard", "0.0.0.0"),
            Destination = GetString(d, "destination", "any"),
            DestinationWildcard = GetString(d, "destination_wildcard", "0.0.0.0"),
            SourcePort = GetOptionalString(d, "source_port"),
            DestinationPort = GetOptionalString(d, "destination_port"),
            Log = GetBool(d, "log", false),
            Remark = GetString(d, "remark"),
        };
    }

    private static StaticRoute ParseStaticRoute(IReadOnlyDictionary<string, object> d)
    {
        return new StaticRoute
        {
            Destination = GetString(d, "destination"),
            Mask = GetString(d, "mask"),
            NextHop = GetString(d, "next_hop"),
            Name = GetString(d, "name"),
            AdminDistance = GetInt(d, "admin_distance", 1),
            Permanent = GetBool(d, "permanent", false),
        };
    }

    private static OspfConfig ParseOspf(IReadOnlyDictionary<string, object> d)
    {
        return new OspfConfig
        {
            ProcessId = GetInt(d, "process_id", 1),
            RouterId = GetOptionalString(d, "router_id"),
            Networks = GetDictList(d, "networks").Select(n => new OspfNetwork
            {
                Network = GetString(n, "network"),
                Wildcard = GetString(n, "wildcard", "0.0.0.0"),
                Area = GetInt(n, "area", 0),
            }).ToList(),
            PassiveInterfaces = GetStringList(d, "passive_interfaces"),
            DefaultInformationOriginate = GetBool(d, "default_information_originate", false),
            ReferenceBandwidth = GetInt(d, "reference_bandwidth", 100),
        };
    }

    private static EigrpConfig ParseEigrp(IReadOnlyDictionary<string, object> d)
    {
        return new EigrpConfig
        {
            AsNumber = GetInt(d, "as_number", 100),
            RouterId = GetOptionalString(d, "router_id"),
            Networks = GetDictList(d, "networks").Select(n => new EigrpNetwork
            {
                Network = GetString(n, "network"),
                Wildcard = GetOptionalString(n, "wildcard"),
            }).ToList(),
            PassiveInterfaces = GetStringList(d, "passive_interfaces"),
            AutoSummary = GetBool(d, "auto_summary", false),
            Redistribute = GetStringList(d, "redistribute"),
            NamedMode = GetBool(d, "named_mode", false),
            Name = GetString(d, "name", "EIGRP_PROCESS"),
        };
    }

    private static BgpConfig ParseBgp(IReadOnlyDictionary<string, object> d)
    {
        return new BgpConfig
        {
            LocalAs = GetInt(d, "local_as", 65000),
            RouterId = GetOptionalString(d, "router_id"),
            Networks = GetStringList(d, "networks"),
            Neighbors = GetDictList(d, "neighbors").Select(ParseBgpNeighbor).ToList(),
            LogNeighborChanges = GetBool(d, "log_neighbor_changes", true),
            Redistribute = GetStringList(d, "redistribute"),
        };
    }

    private static BgpNeighbor ParseBgpNeighbor(IReadOnlyDictionary<string, object> d)
    {
        return new BgpNeighbor
        {
            IpAddress = GetString(d, "ip_address"),
            RemoteAs = GetInt(d, "remote_as", 65000),
            Description = GetString(d, "description"),
            Password = GetOptionalString(d, "password"),
            UpdateSource = GetOptionalString(d, "update_source"),
            EbgpMultihop = GetInt(d, "ebgp_multihop", 0),
            RouteMapIn = GetOptionalString(d, "route_map_in"),
            RouteMapOut = GetOptionalString(d, "route_map_out"),
        };
    }

    private static StpConfig ParseStp(IReadOnlyDictionary<string, object> d)
    {
        return new StpConfig
        {
            Mode = GetStpMode(GetString(d, "mode", "rapid_pvst")),
            Priority = GetInt(d, "priority", 32768),
            RootPrimaryVlans = GetIntList(d, "root_primary_vlans"),
            RootSecondaryVlans = GetIntList(d, "root_secondary_vlans"),
            PortfastDefault = GetBool(d, "portfast_default", false),
            BpduguardDefault = GetBool(d, "bpduguard_default", false),
        };
    }

    private static PrefixList ParsePrefixList(IReadOnlyDictionary<string, object> d)
    {
        return new PrefixList
        {
            Name = GetString(d, "name"),
            Entries = GetDictList(d, "entries").Select(e => new PrefixListEntry
            {
                Sequence = GetInt(e, "sequence", 10),
                Action = GetString(e, "action", "permit"),
                Prefix = GetString(e, "prefix"),
                Ge = GetNullableInt(e, "ge"),
                Le = GetNullableInt(e, "le"),
            }).ToList(),
        };
    }

    private static RouteMap ParseRouteMap(IReadOnlyDictionary<string, object> d)
    {
        return new RouteMap
        {
            Name = GetString(d, "name"),
            Entries = GetDictList(d, "entries").Select(e => new RouteMapEntry
            {
                Sequence = GetInt(e, "sequence", 10),
                Action = GetString(e, "action", "permit"),
                MatchPrefixList = GetOptionalString(e, "match_prefix_list"),
                MatchAsPath = GetOptionalString(e, "match_as_path"),
                MatchCommunity = GetOptionalString(e, "match_community"),
                SetLocalPref = GetNullableInt(e, "set_local_pref"),
                SetMed = GetNullableInt(e, "set_med"),
                SetAsPathPrepend = GetOptionalString(e, "set_as_path_prepend"),
                SetCommunity = GetOptionalString(e, "set_community"),
                SetNextHop = GetOptionalString(e, "set_next_hop"),
                SetWeight = GetNullableInt(e, "set_weight"),
            }).ToList(),
        };
    }

    private static InterfaceType GetInterfaceType(IReadOnlyDictionary<string, object> d, string name)
    {
        if (d.TryGetValue("interface_type", out var raw))
        {
            var text = raw switch
            {
                string s => s.Replace("_", string.Empty).ToLowerInvariant(),
                _ => raw.ToString()?.Replace("_", string.Empty).ToLowerInvariant() ?? string.Empty,
            };

            switch (text)
            {
                case "ethernet": return InterfaceType.Ethernet;
                case "gigabit": return InterfaceType.Gigabit;
                case "tengigabit": return InterfaceType.TenGigabit;
                case "fortygigabit": return InterfaceType.FortyGigabit;
                case "hundredgigabit": return InterfaceType.HundredGigabit;
                case "loopback": return InterfaceType.Loopback;
                case "vlan": return InterfaceType.Vlan;
                case "portchannel": return InterfaceType.PortChannel;
                case "management": return InterfaceType.Management;
            }
        }

        if (name.StartsWith("Vlan", StringComparison.OrdinalIgnoreCase)) return InterfaceType.Vlan;
        if (name.StartsWith("Loopback", StringComparison.OrdinalIgnoreCase)) return InterfaceType.Loopback;
        if (name.StartsWith("Port-channel", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("PortChannel", StringComparison.OrdinalIgnoreCase)) return InterfaceType.PortChannel;
        if (name.StartsWith("HundredGigabit", StringComparison.OrdinalIgnoreCase)) return InterfaceType.HundredGigabit;
        if (name.StartsWith("FortyGigabit", StringComparison.OrdinalIgnoreCase)) return InterfaceType.FortyGigabit;
        if (name.StartsWith("TenGigabit", StringComparison.OrdinalIgnoreCase)) return InterfaceType.TenGigabit;
        if (name.StartsWith("FastEthernet", StringComparison.OrdinalIgnoreCase)) return InterfaceType.Ethernet;
        if (name.StartsWith("Gigabit", StringComparison.OrdinalIgnoreCase)) return InterfaceType.Gigabit;
        if (name.StartsWith("Ethernet", StringComparison.OrdinalIgnoreCase)) return InterfaceType.Ethernet;
        if (name.StartsWith("mgmt", StringComparison.OrdinalIgnoreCase)) return InterfaceType.Management;
        return InterfaceType.Gigabit;
    }

    private static SwitchportMode? GetSwitchportMode(string? value)
    {
        return value?.Replace("_", " ").ToLowerInvariant() switch
        {
            "access" => SwitchportMode.Access,
            "trunk" => SwitchportMode.Trunk,
            "dynamic auto" => SwitchportMode.DynamicAuto,
            "dynamic desirable" => SwitchportMode.DynamicDesirable,
            _ => null,
        };
    }

    private static AclAction GetAclAction(string value)
    {
        return value.Equals("deny", StringComparison.OrdinalIgnoreCase) ? AclAction.Deny : AclAction.Permit;
    }

    private static AclProtocol GetAclProtocol(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "tcp" => AclProtocol.Tcp,
            "udp" => AclProtocol.Udp,
            "icmp" => AclProtocol.Icmp,
            _ => AclProtocol.Ip,
        };
    }

    private static StpMode GetStpMode(string value)
    {
        return value.Replace("-", string.Empty).Replace("_", string.Empty).ToLowerInvariant() switch
        {
            "pvst" => StpMode.Pvst,
            "mst" => StpMode.Mst,
            _ => StpMode.RapidPvst,
        };
    }

    private static IPAddress? TryParseIp(string? value)
    {
        return IPAddress.TryParse(value, out var ip) ? ip : null;
    }

    private static bool TryGetDict(IReadOnlyDictionary<string, object> d, string key, out Dictionary<string, object> dict)
    {
        if (d.TryGetValue(key, out var raw) && raw is Dictionary<string, object> inner)
        {
            dict = inner;
            return true;
        }

        dict = new Dictionary<string, object>();
        return false;
    }

    private static List<Dictionary<string, object>> GetDictList(IReadOnlyDictionary<string, object> d, string key)
    {
        if (!d.TryGetValue(key, out var raw) || raw is not IEnumerable<object> list)
        {
            return new List<Dictionary<string, object>>();
        }

        return list.OfType<Dictionary<string, object>>().ToList();
    }

    private static string GetString(IReadOnlyDictionary<string, object> d, string key, string? def = null)
    {
        if (d.TryGetValue(key, out var raw) && raw is not null)
        {
            return raw is string s ? s : raw.ToString() ?? def ?? string.Empty;
        }

        return def ?? string.Empty;
    }

    private static string? GetOptionalString(IReadOnlyDictionary<string, object> d, string key)
    {
        var value = GetString(d, key);
        return value.Length == 0 ? null : value;
    }

    private static List<string> GetStringList(IReadOnlyDictionary<string, object> d, string key)
    {
        if (!d.TryGetValue(key, out var raw) || raw is not IEnumerable<object> list)
        {
            return new List<string>();
        }

        return list.Select(item => item is string s ? s : item.ToString() ?? string.Empty).ToList();
    }

    private static List<int> GetIntList(IReadOnlyDictionary<string, object> d, string key)
    {
        if (!d.TryGetValue(key, out var raw) || raw is not IEnumerable<object> list)
        {
            return new List<int>();
        }

        return list.Select(item => item switch
        {
            int i => i,
            long l => (int)l,
            _ => int.TryParse(item.ToString(), out var v) ? v : 0,
        }).ToList();
    }

    private static int GetInt(IReadOnlyDictionary<string, object> d, string key, int def)
    {
        return d.TryGetValue(key, out var raw) && raw is not null
            ? raw switch
            {
                int i => i,
                long l => (int)l,
                _ => int.TryParse(raw.ToString(), out var v) ? v : def,
            }
            : def;
    }

    private static int? GetNullableInt(IReadOnlyDictionary<string, object> d, string key)
    {
        return d.TryGetValue(key, out var raw) && raw is not null
            ? raw switch
            {
                int i => i,
                long l => (int)l,
                _ => int.TryParse(raw.ToString(), out var v) ? v : (int?)null,
            }
            : null;
    }

    private static bool GetBool(IReadOnlyDictionary<string, object> d, string key, bool def)
    {
        return d.TryGetValue(key, out var raw) && raw is not null
            ? raw switch
            {
                bool b => b,
                int i => i != 0,
                long l => l != 0,
                _ => bool.TryParse(raw.ToString(), out var v) ? v : def,
            }
            : def;
    }
}
