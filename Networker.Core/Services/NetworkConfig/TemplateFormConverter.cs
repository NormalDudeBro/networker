using System.Net;
using Networker.Core.Models.NetworkConfig;

namespace Networker.Core.Services.NetworkConfig;

/// <summary>
/// Converts form-shaped template data (<see cref="TemplateFormData"/>) into a
/// <see cref="NetworkDeviceConfig"/>, mirroring NetworkConfigPro's GUI logic:
/// <c>_generate_config</c> (VLAN/route/ACL/OSPF/BGP text parsing),
/// <c>_get_interface_name</c> + <c>INTERFACE_PREFIXES</c> (vendor interface
/// naming), and <c>INTERFACE_TYPE_MAP</c>.
/// </summary>
public static class TemplateFormConverter
{
    /// <summary>
    /// Ported <c>VENDOR_DISPLAY</c> table (vendor display name → enum).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, Vendor> VendorDisplay = new Dictionary<string, Vendor>
    {
        ["Cisco IOS/IOS-XE"] = Vendor.CiscoIos,
        ["Cisco NX-OS"] = Vendor.CiscoNxos,
        ["Arista EOS"] = Vendor.AristaEos,
        ["Juniper Junos"] = Vendor.JuniperJunos,
        ["SONiC"] = Vendor.Sonic,
        ["Fortinet FortiGate"] = Vendor.FortinetFortigate,
    };

    /// <summary>
    /// Ported <c>INTERFACE_TYPE_MAP</c> (form interface type → enum).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, InterfaceType> InterfaceTypeMap = new Dictionary<string, InterfaceType>
    {
        ["GigabitEthernet"] = InterfaceType.Gigabit,
        ["TenGigabitEthernet"] = InterfaceType.TenGigabit,
        ["FortyGigabitEthernet"] = InterfaceType.FortyGigabit,
        ["HundredGigabitEthernet"] = InterfaceType.HundredGigabit,
        ["Ethernet"] = InterfaceType.Ethernet,
        ["Loopback"] = InterfaceType.Loopback,
        ["VLAN"] = InterfaceType.Vlan,
        ["Port-Channel"] = InterfaceType.PortChannel,
        ["Management"] = InterfaceType.Management,
    };

    /// <summary>
    /// Ported <c>INTERFACE_PREFIXES</c> (vendor × form type → name prefix).
    /// </summary>
    private static readonly IReadOnlyDictionary<Vendor, IReadOnlyDictionary<string, string>> InterfacePrefixes =
        new Dictionary<Vendor, IReadOnlyDictionary<string, string>>
        {
            [Vendor.CiscoIos] = new Dictionary<string, string>
            {
                ["GigabitEthernet"] = "GigabitEthernet",
                ["TenGigabitEthernet"] = "TenGigabitEthernet",
                ["FortyGigabitEthernet"] = "FortyGigabitEthernet",
                ["HundredGigabitEthernet"] = "HundredGigE",
                ["Ethernet"] = "Ethernet",
                ["Loopback"] = "Loopback",
                ["VLAN"] = "Vlan",
                ["Port-Channel"] = "Port-channel",
                ["Management"] = "Management",
            },
            [Vendor.CiscoNxos] = new Dictionary<string, string>
            {
                ["GigabitEthernet"] = "Ethernet",
                ["TenGigabitEthernet"] = "Ethernet",
                ["FortyGigabitEthernet"] = "Ethernet",
                ["HundredGigabitEthernet"] = "Ethernet",
                ["Ethernet"] = "Ethernet",
                ["Loopback"] = "loopback",
                ["VLAN"] = "Vlan",
                ["Port-Channel"] = "port-channel",
                ["Management"] = "mgmt",
            },
            [Vendor.AristaEos] = new Dictionary<string, string>
            {
                ["GigabitEthernet"] = "Ethernet",
                ["TenGigabitEthernet"] = "Ethernet",
                ["FortyGigabitEthernet"] = "Ethernet",
                ["HundredGigabitEthernet"] = "Ethernet",
                ["Ethernet"] = "Ethernet",
                ["Loopback"] = "Loopback",
                ["VLAN"] = "Vlan",
                ["Port-Channel"] = "Port-Channel",
                ["Management"] = "Management",
            },
            [Vendor.JuniperJunos] = new Dictionary<string, string>
            {
                ["GigabitEthernet"] = "ge-",
                ["TenGigabitEthernet"] = "xe-",
                ["FortyGigabitEthernet"] = "et-",
                ["HundredGigabitEthernet"] = "et-",
                ["Ethernet"] = "et-",
                ["Loopback"] = "lo",
                ["VLAN"] = "irb.",
                ["Port-Channel"] = "ae",
                ["Management"] = "em",
            },
            [Vendor.Sonic] = new Dictionary<string, string>
            {
                ["GigabitEthernet"] = "Ethernet",
                ["TenGigabitEthernet"] = "Ethernet",
                ["FortyGigabitEthernet"] = "Ethernet",
                ["HundredGigabitEthernet"] = "Ethernet",
                ["Ethernet"] = "Ethernet",
                ["Loopback"] = "Loopback",
                ["VLAN"] = "Vlan",
                ["Port-Channel"] = "PortChannel",
                ["Management"] = "eth",
            },
            [Vendor.FortinetFortigate] = new Dictionary<string, string>
            {
                ["GigabitEthernet"] = "port",
                ["TenGigabitEthernet"] = "port",
                ["FortyGigabitEthernet"] = "port",
                ["HundredGigabitEthernet"] = "port",
                ["Ethernet"] = "port",
                ["Loopback"] = "loopback",
                ["VLAN"] = "vlan",
                ["Port-Channel"] = "agg",
                ["Management"] = "mgmt",
            },
        };

    /// <summary>
    /// Converts a form preset to a <see cref="NetworkDeviceConfig"/>.
    /// </summary>
    public static NetworkDeviceConfig Convert(TemplateFormData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var vendor = VendorFromDisplayName(data.Basic.Vendor);
        var acls = ConvertAcl(data.Acl);
        var ospf = ConvertOspf(data.Ospf);
        var bgp = ConvertBgp(data.Bgp);
        var eigrp = ConvertEigrp(data.Eigrp);
        var stp = ConvertStp(data.Stp);

        var interfaces = new List<Interface>();
        foreach (var entry in data.Interfaces)
        {
            // Python: "Only add if number is provided" — skip blank rows.
            if (!string.IsNullOrWhiteSpace(entry.Number))
            {
                interfaces.Add(ConvertInterface(entry, vendor));
            }
        }

        return new NetworkDeviceConfig
        {
            Hostname = string.IsNullOrWhiteSpace(data.Basic.Hostname) ? "router" : data.Basic.Hostname.Trim(),
            Vendor = vendor,
            Interfaces = interfaces,
            Vlans = ParseVlans(data.Vlans),
            Acls = acls,
            StaticRoutes = ParseStaticRoutes(data.StaticRoutes),
            Ospf = ospf,
            Bgp = bgp,
            Eigrp = eigrp,
            Stp = stp,
            EnableSecret = Optional(data.Basic.EnableSecret),
            DomainName = Optional(data.Basic.Domain),
            DnsServers = SplitCommaSeparated(data.Basic.DnsServers),
            NtpServers = SplitCommaSeparated(data.Basic.NtpServers),
        };
    }

    /// <summary>
    /// Resolves a vendor display name ("Cisco IOS/IOS-XE") to a <see cref="Vendor"/>.
    /// </summary>
    public static Vendor VendorFromDisplayName(string displayName)
    {
        if (VendorDisplay.TryGetValue(displayName, out var vendor))
        {
            return vendor;
        }

        throw new ArgumentOutOfRangeException(nameof(displayName), displayName, "Unknown vendor display name.");
    }

    private static Interface ConvertInterface(TemplateInterfaceEntry entry, Vendor vendor)
    {
        var type = entry.Type.Trim();
        var prefix = InterfacePrefixes.GetValueOrDefault(vendor, new Dictionary<string, string>())
            .GetValueOrDefault(type, type);

        return new Interface
        {
            Name = $"{prefix}{entry.Number.Trim()}",
            InterfaceType = InterfaceTypeMap.GetValueOrDefault(type, InterfaceType.Gigabit),
            Description = entry.Description.Trim(),
            IpAddress = TryParseIp(entry.Ip),
            SubnetMask = TryParseIp(entry.Mask),
        };
    }

    private static List<Vlan> ParseVlans(string text) => ParseLines(text, parts =>
    {
        if (parts.Length >= 2 && int.TryParse(parts[0], out var vlanId))
        {
            return new Vlan { VlanId = vlanId, Name = parts[1] };
        }

        return null;
    });

    private static List<StaticRoute> ParseStaticRoutes(string text) => ParseLines(text, parts =>
        parts.Length >= 3
            ? new StaticRoute { Destination = parts[0], Mask = parts[1], NextHop = parts[2] }
            : null);

    private static List<Acl> ConvertAcl(TemplateAcl acl)
    {
        var result = new List<Acl>();
        var name = acl.Name.Trim();

        // Python: only build the ACL if a name exists and entries were added.
        if (name.Length == 0 || acl.Entries.Count == 0)
        {
            return result;
        }

        var model = new Acl
        {
            Name = name,
            IsExtended = acl.Type.Trim().Equals("Extended", StringComparison.OrdinalIgnoreCase),
        };

        foreach (var entry in acl.Entries)
        {
            var source = entry.Source.Trim();
            if (int.TryParse(entry.Sequence.Trim(), out var sequence) && source.Length > 0)
            {
                model.Entries.Add(new AclEntry
                {
                    Sequence = sequence,
                    Action = entry.Action.Trim().Equals("deny", StringComparison.OrdinalIgnoreCase)
                        ? AclAction.Deny
                        : AclAction.Permit,
                    Protocol = entry.Protocol.Trim().ToLowerInvariant() switch
                    {
                        "tcp" => AclProtocol.Tcp,
                        "udp" => AclProtocol.Udp,
                        "icmp" => AclProtocol.Icmp,
                        _ => AclProtocol.Ip,
                    },
                    Source = source,
                    SourceWildcard = DefaultIfEmpty(entry.SourceWildcard, "0.0.0.0"),
                    Destination = DefaultIfEmpty(entry.Destination, "any"),
                    DestinationWildcard = DefaultIfEmpty(entry.DestinationWildcard, "0.0.0.0"),
                    DestinationPort = Optional(entry.DestinationPort),
                    Log = entry.Log.Trim().Equals("log", StringComparison.OrdinalIgnoreCase),
                });
            }
        }

        if (model.Entries.Count > 0)
        {
            result.Add(model);
        }

        return result;
    }

    private static OspfConfig? ConvertOspf(TemplateOspf ospf)
    {
        // Python: OSPF is only built when a process ID is present.
        if (!int.TryParse(ospf.ProcessId.Trim(), out var processId))
        {
            return null;
        }

        var referenceBandwidth = int.TryParse(ospf.ReferenceBandwidth.Trim(), out var refBw)
            ? refBw
            : 100; // Python OSPFConfig.reference_bandwidth default.

        var model = new OspfConfig
        {
            ProcessId = processId,
            RouterId = Optional(ospf.RouterId),
            ReferenceBandwidth = referenceBandwidth,
            PassiveInterfaces = SplitCommaSeparated(ospf.PassiveInterfaces),
        };

        foreach (var network in ParseLines(ospf.Networks, parts =>
            parts.Length >= 3 && int.TryParse(parts[2], out var area)
                ? new OspfNetwork { Network = parts[0], Wildcard = parts[1], Area = area }
                : null))
        {
            model.Networks.Add(network);
        }

        return model;
    }

    private static BgpConfig? ConvertBgp(TemplateBgp bgp)
    {
        // Python: BGP is only built when a local AS is present.
        if (!int.TryParse(bgp.LocalAs.Trim(), out var localAs))
        {
            return null;
        }

        var model = new BgpConfig
        {
            LocalAs = localAs,
            RouterId = Optional(bgp.RouterId),
            Networks = bgp.Networks
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .ToList(),
        };

        foreach (var neighbor in bgp.Neighbors)
        {
            var ip = neighbor.IpAddress.Trim();
            if (ip.Length > 0 && int.TryParse(neighbor.RemoteAs.Trim(), out var remoteAs))
            {
                model.Neighbors.Add(new BgpNeighbor
                {
                    IpAddress = ip,
                    RemoteAs = remoteAs,
                    Description = neighbor.Description.Trim(),
                    UpdateSource = Optional(neighbor.UpdateSource),
                    EbgpMultihop = int.TryParse(neighbor.EbgpMultihop.Trim(), out var multihop) ? multihop : 0,
                });
            }
        }

        return model;
    }

    private static EigrpConfig? ConvertEigrp(TemplateEigrp eigrp)
    {
        // Python: EIGRP is only built when an AS number is present.
        if (!int.TryParse(eigrp.AsNumber.Trim(), out var asNumber))
        {
            return null;
        }

        var model = new EigrpConfig
        {
            AsNumber = asNumber,
            RouterId = Optional(eigrp.RouterId),
            NamedMode = eigrp.NamedMode,
            Name = string.IsNullOrWhiteSpace(eigrp.Name) ? "EIGRP_PROCESS" : eigrp.Name.Trim(),
            PassiveInterfaces = SplitCommaSeparated(eigrp.PassiveInterfaces),
        };

        foreach (var network in ParseLines(eigrp.Networks, parts =>
            parts.Length >= 1 && parts[0].Length > 0
                ? new EigrpNetwork { Network = parts[0], Wildcard = parts.Length >= 2 ? parts[1] : null }
                : null))
        {
            model.Networks.Add(network);
        }

        return model;
    }

    private static StpConfig? ConvertStp(TemplateStp stp)
    {
        // Python: only add STP if something is configured.
        var hasAny = !string.IsNullOrWhiteSpace(stp.Priority)
            || !string.IsNullOrWhiteSpace(stp.RootPrimaryVlans)
            || !string.IsNullOrWhiteSpace(stp.RootSecondaryVlans)
            || stp.PortfastDefault
            || stp.BpduguardDefault;

        if (!hasAny)
        {
            return null;
        }

        var mode = stp.Mode.Trim().ToLowerInvariant() switch
        {
            "pvst" => StpMode.Pvst,
            "mst" => StpMode.Mst,
            _ => StpMode.RapidPvst,
        };

        var model = new StpConfig
        {
            Mode = mode,
            PortfastDefault = stp.PortfastDefault,
            BpduguardDefault = stp.BpduguardDefault,
        };

        if (int.TryParse(stp.Priority.Trim(), out var priority))
        {
            model = model with { Priority = priority };
        }

        var rootPrimary = TryParseIntList(stp.RootPrimaryVlans);
        if (rootPrimary is not null)
        {
            model = model with { RootPrimaryVlans = rootPrimary };
        }

        var rootSecondary = TryParseIntList(stp.RootSecondaryVlans);
        if (rootSecondary is not null)
        {
            model = model with { RootSecondaryVlans = rootSecondary };
        }

        return model;
    }

    /// <summary>
    /// Parses a comma-separated integer list. Returns null when empty or when
    /// any element fails to parse (mirrors Python's try/except around the
    /// whole list comprehension).
    /// </summary>
    private static List<int>? TryParseIntList(string text)
    {
        var parts = text.Split(',').Select(part => part.Trim()).Where(part => part.Length > 0).ToArray();
        if (parts.Length == 0)
        {
            return null;
        }

        var values = new List<int>();
        foreach (var part in parts)
        {
            if (!int.TryParse(part, out var value))
            {
                return null;
            }

            values.Add(value);
        }

        return values;
    }

    private static List<T> ParseLines<T>(string text, Func<string[], T?> parseLine)
        where T : class
    {
        var result = new List<T>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return result;
        }

        foreach (var line in text.Split('\n'))
        {
            var parts = line.Split(',').Select(part => part.Trim()).ToArray();
            var parsed = parseLine(parts);
            if (parsed is not null)
            {
                result.Add(parsed);
            }
        }

        return result;
    }

    private static List<string> SplitCommaSeparated(string text) =>
        text.Split(',').Select(part => part.Trim()).Where(part => part.Length > 0).ToList();

    private static IPAddress? TryParseIp(string value) =>
        IPAddress.TryParse(value.Trim(), out var ip) ? ip : null;

    private static string? Optional(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string DefaultIfEmpty(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
