using System.Text.RegularExpressions;
using Networker.Core.Models.NetworkConfig;

namespace Networker.Core.NetTools.Config;

/// <summary>
/// Value converters and name-mapping filters that reproduce the behavior of
/// the Python NetworkConfigPro Jinja2 filters so generated output matches the
/// reference byte-for-byte.
/// </summary>
public static class ConfigTemplateFilters
{
    public static string AclActionValue(AclAction action) =>
        action switch
        {
            AclAction.Permit => "permit",
            AclAction.Deny => "deny",
            _ => action.ToString().ToLowerInvariant(),
        };

    public static string AclProtocolValue(AclProtocol protocol) =>
        protocol switch
        {
            AclProtocol.Ip => "ip",
            AclProtocol.Tcp => "tcp",
            AclProtocol.Udp => "udp",
            AclProtocol.Icmp => "icmp",
            _ => protocol.ToString().ToLowerInvariant(),
        };

    public static string SwitchportModeValue(SwitchportMode mode) =>
        mode switch
        {
            SwitchportMode.Access => "access",
            SwitchportMode.Trunk => "trunk",
            SwitchportMode.DynamicAuto => "dynamic auto",
            SwitchportMode.DynamicDesirable => "dynamic desirable",
            _ => mode.ToString().ToLowerInvariant(),
        };

    public static string StpModeValue(StpMode mode) =>
        mode switch
        {
            StpMode.Pvst => "pvst",
            StpMode.RapidPvst => "rapid-pvst",
            StpMode.Mst => "mst",
            _ => mode.ToString().ToLowerInvariant(),
        };

    public static string InterfaceTypeValue(InterfaceType type) =>
        type switch
        {
            InterfaceType.Ethernet => "ethernet",
            InterfaceType.Gigabit => "gigabit",
            InterfaceType.TenGigabit => "tengigabit",
            InterfaceType.FortyGigabit => "fortygigabit",
            InterfaceType.HundredGigabit => "hundredgigabit",
            InterfaceType.Loopback => "loopback",
            InterfaceType.Vlan => "vlan",
            InterfaceType.PortChannel => "port_channel",
            InterfaceType.Management => "management",
            _ => type.ToString().ToLowerInvariant(),
        };

    /// <summary>
    /// Converts a subnet mask to a CIDR prefix length (the <c>cidr_prefix</c> filter).
    /// </summary>
    public static int SubnetToCidr(string? mask)
    {
        if (string.IsNullOrEmpty(mask))
        {
            return 32;
        }

        var octets = mask.Split('.');
        if (octets.Length == 4 && octets.All(o => int.TryParse(o, out _)))
        {
            int bits = 0;
            foreach (var octet in octets)
            {
                bits += CountOnes(int.Parse(octet));
            }

            return bits;
        }

        return int.TryParse(mask, out var numeric) ? numeric : 32;
    }

    /// <summary>
    /// Converts a wildcard mask to a CIDR prefix length (the <c>wildcard_to_cidr</c> filter).
    /// </summary>
    public static int WildcardToCidr(string? wildcard)
    {
        if (string.IsNullOrEmpty(wildcard) || wildcard == "0.0.0.0")
        {
            return 32;
        }

        var octets = wildcard.Split('.');
        if (octets.Length == 4 && octets.All(o => int.TryParse(o, out _)))
        {
            int bits = 0;
            foreach (var octet in octets)
            {
                bits += CountOnes(255 - int.Parse(octet));
            }

            return bits;
        }

        return 32;
    }

    /// <summary>
    /// Converts a wildcard mask to a subnet mask (the <c>wildcard_to_netmask</c> filter).
    /// </summary>
    public static string WildcardToNetmask(string? wildcard)
    {
        if (string.IsNullOrEmpty(wildcard) || wildcard == "0.0.0.0")
        {
            return "255.255.255.255";
        }

        var octets = wildcard.Split('.');
        if (octets.Length == 4 && octets.All(o => int.TryParse(o, out _)))
        {
            return string.Join(".", octets.Select(o => (255 - int.Parse(o)).ToString()));
        }

        return "255.255.255.255";
    }

    /// <summary>
    /// Converts an interface name to Junos format (the <c>junos_interface_name</c> filter).
    /// </summary>
    public static string JunosInterfaceName(string name)
    {
        var nameLower = name.ToLowerInvariant();
        var conversions = new[]
        {
            ("hundredgigabitethernet", "et-"),
            ("fortygigabitethernet", "et-"),
            ("tengigabitethernet", "xe-"),
            ("gigabitethernet", "ge-"),
            ("fastethernet", "fe-"),
            ("ethernet", "et-"),
            ("loopback", "lo"),
            ("port-channel", "ae"),
            ("vlan", "vlan."),
        };

        foreach (var (oldName, newPrefix) in conversions)
        {
            if (!nameLower.Contains(oldName, StringComparison.Ordinal))
            {
                continue;
            }

            var remainder = nameLower.Replace(oldName, string.Empty).Trim();
            if (remainder.Contains('/'))
            {
                var parts = remainder.Split('/');
                if (newPrefix is "ge-" or "xe-" or "fe-" or "et-")
                {
                    return newPrefix + string.Join('/', parts);
                }
            }

            return newPrefix + remainder;
        }

        return name;
    }

    /// <summary>
    /// Converts an interface name to SONiC format (the <c>sonic_interface_name</c> filter).
    /// </summary>
    public static string SonicInterfaceName(string name)
    {
        var nameLower = name.ToLowerInvariant();
        var conversions = new[]
        {
            ("hundredgigabitethernet", "Ethernet"),
            ("fortygigabitethernet", "Ethernet"),
            ("tengigabitethernet", "Ethernet"),
            ("gigabitethernet", "Ethernet"),
            ("fastethernet", "Ethernet"),
            ("ethernet", "Ethernet"),
            ("loopback", "Loopback"),
            ("port-channel", "PortChannel"),
            ("portchannel", "PortChannel"),
            ("vlan", "Vlan"),
            ("management", "eth"),
            ("mgmt", "eth"),
        };

        foreach (var (oldName, newName) in conversions)
        {
            if (!nameLower.Contains(oldName, StringComparison.Ordinal))
            {
                continue;
            }

            var remainder = nameLower.Replace(oldName, string.Empty).Trim();
            if (remainder.Contains('/'))
            {
                var parts = remainder.Split('/');
                if (int.TryParse(parts[^1], out _))
                {
                    int flat;
                    if (parts.Length == 2)
                    {
                        flat = int.Parse(parts[0]) * 48 + int.Parse(parts[1]);
                    }
                    else if (parts.Length == 3)
                    {
                        flat = int.Parse(parts[0]) * 48 + int.Parse(parts[1]) * 4 + int.Parse(parts[2]);
                    }
                    else
                    {
                        flat = int.Parse(parts[^1]);
                    }

                    return newName == "PortChannel" ? newName + flat.ToString("D4") : newName + flat.ToString();
                }

                return newName + remainder.Replace("/", string.Empty);
            }

            if (int.TryParse(remainder, out var num))
            {
                return newName == "PortChannel" ? newName + num.ToString("D4") : newName + num.ToString();
            }

            return newName + remainder;
        }

        if (name.StartsWith("Ethernet", StringComparison.Ordinal) ||
            name.StartsWith("Loopback", StringComparison.Ordinal) ||
            name.StartsWith("PortChannel", StringComparison.Ordinal) ||
            name.StartsWith("Vlan", StringComparison.Ordinal))
        {
            return name;
        }

        return name;
    }

    /// <summary>
    /// Extracts the numeric VLAN ID from an interface name (the <c>sonic_vlan_id</c> filter).
    /// </summary>
    public static string SonicVlanId(string name)
    {
        var match = Regex.Match(name, @"(\d+)");
        return match.Success ? match.Groups[1].Value : name;
    }

    /// <summary>
    /// Strips IOS-style port qualifiers (the <c>sonic_port</c> filter).
    /// </summary>
    public static string SonicPort(string? port)
    {
        if (string.IsNullOrEmpty(port))
        {
            return port ?? string.Empty;
        }

        var value = port.Trim();
        var prefixes = new[] { "eq ", "neq ", "lt ", "gt ", "range " };
        foreach (var prefix in prefixes)
        {
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return value.Substring(prefix.Length).Trim();
            }
        }

        return value;
    }

    /// <summary>
    /// Converts an interface name to FortiGate format (the <c>fortinet_interface_name</c> filter).
    /// </summary>
    public static string FortinetInterfaceName(string name)
    {
        var nameLower = name.ToLowerInvariant();
        var conversions = new[]
        {
            ("hundredgigabitethernet", "port"),
            ("fortygigabitethernet", "port"),
            ("tengigabitethernet", "port"),
            ("gigabitethernet", "port"),
            ("fastethernet", "port"),
            ("ethernet", "port"),
            ("loopback", "loopback"),
            ("port-channel", "agg"),
            ("portchannel", "agg"),
            ("vlan", "vlan"),
            ("management", "mgmt"),
            ("mgmt", "mgmt"),
        };

        foreach (var (oldName, newName) in conversions)
        {
            if (!nameLower.Contains(oldName, StringComparison.Ordinal))
            {
                continue;
            }

            var remainder = nameLower.Replace(oldName, string.Empty).Trim();
            if (remainder.Contains('/'))
            {
                var parts = remainder.Split('/');
                if (int.TryParse(parts[^1], out var portNum))
                {
                    return newName + (portNum + 1).ToString();
                }

                return newName + remainder.Replace("/", string.Empty);
            }

            if (int.TryParse(remainder, out var num))
            {
                return newName == "port" ? newName + (num + 1).ToString() : newName + num.ToString();
            }

            return newName + remainder;
        }

        if (name.StartsWith("port", StringComparison.Ordinal) ||
            name.StartsWith("wan", StringComparison.Ordinal) ||
            name.StartsWith("internal", StringComparison.Ordinal) ||
            name.StartsWith("dmz", StringComparison.Ordinal))
        {
            return name;
        }

        return name;
    }

    /// <summary>
    /// Extracts the parent interface for a VLAN sub-interface (the <c>fortinet_parent_interface</c> filter).
    /// </summary>
    public static string FortinetParentInterface(string name)
    {
        if (name.Contains('.'))
        {
            var parent = name.Split('.')[0];
            return FortinetInterfaceName(parent);
        }

        return "internal";
    }

    /// <summary>
    /// Maps an OSPF network to a FortiGate interface (the <c>fortinet_ospf_interface</c> filter).
    /// </summary>
    public static string FortinetOspfInterface(string network) => "port1";

    /// <summary>
    /// Formats a channel-group number like SONiC's <c>%04d</c> formatting.
    /// </summary>
    public static string SonicChannelGroup(int channelGroup) => channelGroup.ToString("D4");

    private static int CountOnes(int octet)
    {
        int count = 0;
        for (int i = 0; i < 8; i++)
        {
            count += (octet >> i) & 1;
        }

        return count;
    }
}
