using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Networker.Core.Models.NetworkConfig;

namespace Networker.Core.Services.NetworkConfig;

/// <summary>
/// Summary of validation results, grouped by severity and category.
/// </summary>
public sealed record ValidationSummary
{
    /// <summary>Total number of validation issues.</summary>
    public required int Total { get; init; }

    /// <summary>Issue counts by severity (only non-zero counts).</summary>
    public required IReadOnlyDictionary<ValidationSeverity, int> BySeverity { get; init; }

    /// <summary>Issue counts by category (only non-zero counts).</summary>
    public required IReadOnlyDictionary<ValidationCategory, int> ByCategory { get; init; }
}

/// <summary>
/// Validates network configurations and checks for best practices.
/// Ported from NetworkConfigPro <c>src/core/validators/config_validator.py</c>.
/// </summary>
public sealed class ConfigValidator : IConfigValidator
{
    /// <summary>Common weak passwords to check against.</summary>
    private static readonly HashSet<string> WeakPasswords = new(StringComparer.OrdinalIgnoreCase)
    {
        "cisco", "admin", "password", "123456", "cisco123",
        "default", "test", "changeme", "secret", "enable",
    };

    /// <summary>Reserved VLAN IDs.</summary>
    private static readonly HashSet<int> ReservedVlans = new() { 1, 1002, 1003, 1004, 1005, 4094 };

    /// <inheritdoc />
    public IReadOnlyList<ValidationIssue> Validate(NetworkDeviceConfig config)
    {
        var issues = new List<ValidationIssue>();

        // Run all validation checks
        ValidateHostname(config, issues);
        ValidateInterfaces(config, issues);
        ValidateVlans(config, issues);
        ValidateAcls(config, issues);
        ValidateStaticRoutes(config, issues);
        ValidateOspf(config, issues);
        ValidateBgp(config, issues);
        ValidateSecurity(config, issues);
        CheckBestPractices(config, issues);

        return issues;
    }

    /// <summary>
    /// Builds a summary of validation results (counts by severity and category).
    /// Mirrors Python <c>ConfigValidator.get_summary()</c>.
    /// </summary>
    public static ValidationSummary GetSummary(IReadOnlyList<ValidationIssue> issues)
    {
        var bySeverity = new Dictionary<ValidationSeverity, int>();
        foreach (var severity in Enum.GetValues<ValidationSeverity>())
        {
            var count = issues.Count(i => i.Severity == severity);
            if (count > 0)
                bySeverity[severity] = count;
        }

        var byCategory = new Dictionary<ValidationCategory, int>();
        foreach (var category in Enum.GetValues<ValidationCategory>())
        {
            var count = issues.Count(i => i.Category == category);
            if (count > 0)
                byCategory[category] = count;
        }

        return new ValidationSummary
        {
            Total = issues.Count,
            BySeverity = bySeverity,
            ByCategory = byCategory,
        };
    }

    private static void ValidateHostname(NetworkDeviceConfig config, List<ValidationIssue> issues)
    {
        if (string.IsNullOrEmpty(config.Hostname))
        {
            AddIssue(issues, ValidationSeverity.Error, ValidationCategory.Syntax,
                "Hostname is not configured", "Global",
                "Configure a hostname for the device");
            return;
        }

        // Check hostname format
        if (!Regex.IsMatch(config.Hostname, @"^[a-zA-Z][a-zA-Z0-9\-_]*$"))
        {
            AddIssue(issues, ValidationSeverity.Error, ValidationCategory.Syntax,
                $"Invalid hostname format: {config.Hostname}", "Global",
                "Hostname must start with a letter and contain only letters, numbers, hyphens, and underscores");
        }

        if (config.Hostname.Length > 63)
        {
            AddIssue(issues, ValidationSeverity.Error, ValidationCategory.Syntax,
                $"Hostname too long: {config.Hostname.Length} characters", "Global",
                "Hostname must be 63 characters or less");
        }
    }

    private static void ValidateInterfaces(NetworkDeviceConfig config, List<ValidationIssue> issues)
    {
        var usedIps = new Dictionary<string, string>();

        foreach (var iface in config.Interfaces)
        {
            var location = $"Interface {iface.Name}";

            // Check for duplicate IP addresses
            if (iface.IpAddress is not null)
            {
                var ip = iface.IpAddress.ToString();
                if (usedIps.TryGetValue(ip, out var otherName))
                {
                    AddIssue(issues, ValidationSeverity.Error, ValidationCategory.Redundancy,
                        $"Duplicate IP address: {ip} (also on {otherName})", location,
                        "Each interface must have a unique IP address");
                }
                else
                {
                    usedIps[ip] = iface.Name;
                }
            }

            // Validate subnet mask
            if (iface.SubnetMask is not null && !IsValidSubnetMask(iface.SubnetMask.ToString()!))
            {
                AddIssue(issues, ValidationSeverity.Error, ValidationCategory.Syntax,
                    $"Invalid subnet mask: {iface.SubnetMask}", location,
                    "Use a valid subnet mask (e.g., 255.255.255.0)");
            }

            // Check VLAN configuration
            var vlanId = iface.VlanId ?? iface.AccessVlan;
            if (vlanId is not null && ReservedVlans.Contains(vlanId.Value))
            {
                AddIssue(issues, ValidationSeverity.Warning, ValidationCategory.BestPractice,
                    $"Using reserved VLAN {vlanId}", location,
                    "Avoid using reserved VLANs (1, 1002-1005, 4094)");
            }

            // Check trunk configuration
            if (iface.IsTrunk && string.IsNullOrEmpty(iface.TrunkAllowedVlans))
            {
                AddIssue(issues, ValidationSeverity.Warning, ValidationCategory.Security,
                    "Trunk interface allows all VLANs", location,
                    "Explicitly configure allowed VLANs on trunk interfaces");
            }

            // MTU validation
            if (iface.Mtu < 576)
            {
                AddIssue(issues, ValidationSeverity.Error, ValidationCategory.Syntax,
                    $"MTU too small: {iface.Mtu}", location,
                    "MTU should be at least 576 bytes for IPv4");
            }

            // Check for description on important interfaces
            if (iface.IpAddress is not null && string.IsNullOrEmpty(iface.Description))
            {
                AddIssue(issues, ValidationSeverity.Info, ValidationCategory.BestPractice,
                    "Interface with IP has no description", location,
                    "Add descriptions to routed interfaces for documentation");
            }
        }
    }

    private static void ValidateVlans(NetworkDeviceConfig config, List<ValidationIssue> issues)
    {
        var seenVlans = new Dictionary<int, string>();

        foreach (var vlan in config.Vlans)
        {
            var location = $"VLAN {vlan.VlanId}";

            // Check for duplicates
            if (seenVlans.ContainsKey(vlan.VlanId))
            {
                AddIssue(issues, ValidationSeverity.Error, ValidationCategory.Redundancy,
                    $"Duplicate VLAN ID: {vlan.VlanId}", location,
                    "Remove duplicate VLAN definition");
            }
            else
            {
                seenVlans[vlan.VlanId] = vlan.Name;
            }

            // Check reserved VLANs
            if (ReservedVlans.Contains(vlan.VlanId))
            {
                AddIssue(issues, ValidationSeverity.Warning, ValidationCategory.BestPractice,
                    $"Configuring reserved VLAN {vlan.VlanId}", location,
                    "Reserved VLANs should generally not be modified");
            }

            // Check VLAN name
            if (string.IsNullOrWhiteSpace(vlan.Name) || vlan.Name.ToLowerInvariant() is "vlan" or "default")
            {
                AddIssue(issues, ValidationSeverity.Info, ValidationCategory.BestPractice,
                    "VLAN has generic or no name", location,
                    "Use descriptive VLAN names");
            }
        }
    }

    private static void ValidateAcls(NetworkDeviceConfig config, List<ValidationIssue> issues)
    {
        foreach (var acl in config.Acls)
        {
            var location = $"ACL {acl.Name}";
            var hasDenyAny = false;
            var hasPermit = false;
            var sequences = new HashSet<int>();

            foreach (var entry in acl.Entries)
            {
                var entryLocation = $"{location} seq {entry.Sequence}";

                // Check for duplicate sequences
                if (!sequences.Add(entry.Sequence))
                {
                    AddIssue(issues, ValidationSeverity.Error, ValidationCategory.Redundancy,
                        $"Duplicate sequence number: {entry.Sequence}", entryLocation,
                        "Use unique sequence numbers");
                }

                // Track permit/deny
                if (entry.Action == AclAction.Permit)
                    hasPermit = true;
                if (entry.Action == AclAction.Deny && entry.Source == "any" && entry.Destination == "any")
                    hasDenyAny = true;

                // Validate IP addresses in ACL
                if (entry.Source != "any" && !IsValidIpOrNetwork(entry.Source))
                {
                    AddIssue(issues, ValidationSeverity.Error, ValidationCategory.Syntax,
                        $"Invalid source in ACL: {entry.Source}", entryLocation,
                        "Use a valid IP address or 'any'");
                }
            }

            // Check for implicit deny warning
            if (hasPermit && !hasDenyAny)
            {
                AddIssue(issues, ValidationSeverity.Info, ValidationCategory.Security,
                    "ACL has implicit deny at end", location,
                    "Consider adding explicit deny any any with logging for visibility");
            }

            // Empty ACL warning
            if (acl.Entries.Count == 0)
            {
                AddIssue(issues, ValidationSeverity.Warning, ValidationCategory.Syntax,
                    "Empty ACL defined", location,
                    "Add entries or remove unused ACL");
            }
        }
    }

    private static void ValidateStaticRoutes(NetworkDeviceConfig config, List<ValidationIssue> issues)
    {
        var seenRoutes = new HashSet<(string Destination, string Mask, string NextHop)>();

        foreach (var route in config.StaticRoutes)
        {
            var location = $"Static route to {route.Destination}/{route.Mask}";
            var routeKey = (route.Destination, route.Mask, route.NextHop);

            // Check for duplicates
            if (!seenRoutes.Add(routeKey))
            {
                AddIssue(issues, ValidationSeverity.Warning, ValidationCategory.Redundancy,
                    $"Duplicate static route: {route.Destination}", location,
                    "Remove duplicate route definition");
            }

            // Validate destination
            if (!IsValidIp(route.Destination))
            {
                AddIssue(issues, ValidationSeverity.Error, ValidationCategory.Syntax,
                    $"Invalid destination: {route.Destination}", location,
                    "Use a valid network address");
            }

            // Validate next-hop
            if (!IsValidIp(route.NextHop))
            {
                AddIssue(issues, ValidationSeverity.Error, ValidationCategory.Syntax,
                    $"Invalid next-hop: {route.NextHop}", location,
                    "Use a valid next-hop IP address");
            }

            // Check for default route
            if (route.Destination == "0.0.0.0" && route.Mask == "0.0.0.0")
            {
                AddIssue(issues, ValidationSeverity.Info, ValidationCategory.BestPractice,
                    "Default route configured via static routing", location,
                    "Consider using a dynamic routing protocol for redundancy");
            }
        }
    }

    private static void ValidateOspf(NetworkDeviceConfig config, List<ValidationIssue> issues)
    {
        var ospf = config.Ospf;
        if (ospf is null)
            return;

        var location = $"OSPF process {ospf.ProcessId}";

        // Check router ID
        if (string.IsNullOrEmpty(ospf.RouterId))
        {
            AddIssue(issues, ValidationSeverity.Warning, ValidationCategory.BestPractice,
                "OSPF router-id not explicitly configured", location,
                "Explicitly configure router-id for stability");
        }
        else if (!IsValidIp(ospf.RouterId))
        {
            AddIssue(issues, ValidationSeverity.Error, ValidationCategory.Syntax,
                $"Invalid OSPF router-id: {ospf.RouterId}", location,
                "Use a valid IP address for router-id");
        }

        // Check reference bandwidth
        if (ospf.ReferenceBandwidth < 1000)
        {
            AddIssue(issues, ValidationSeverity.Info, ValidationCategory.Performance,
                $"Low OSPF reference bandwidth: {ospf.ReferenceBandwidth}", location,
                "Consider increasing reference-bandwidth for high-speed links");
        }

        // Check for networks
        if (ospf.Networks.Count == 0)
        {
            AddIssue(issues, ValidationSeverity.Warning, ValidationCategory.Syntax,
                "OSPF configured but no networks advertised", location,
                "Add network statements to advertise routes");
        }
    }

    private static void ValidateBgp(NetworkDeviceConfig config, List<ValidationIssue> issues)
    {
        var bgp = config.Bgp;
        if (bgp is null)
            return;

        var location = $"BGP AS {bgp.LocalAs}";

        // Check router ID
        if (string.IsNullOrEmpty(bgp.RouterId))
        {
            AddIssue(issues, ValidationSeverity.Warning, ValidationCategory.BestPractice,
                "BGP router-id not explicitly configured", location,
                "Explicitly configure router-id for stability");
        }

        // Check neighbors
        if (bgp.Neighbors.Count == 0)
        {
            AddIssue(issues, ValidationSeverity.Warning, ValidationCategory.Syntax,
                "BGP configured but no neighbors defined", location,
                "Add BGP neighbor configurations");
        }

        foreach (var neighbor in bgp.Neighbors)
        {
            var neighborLocation = $"{location} neighbor {neighbor.IpAddress}";

            // Validate neighbor IP
            if (!IsValidIp(neighbor.IpAddress))
            {
                AddIssue(issues, ValidationSeverity.Error, ValidationCategory.Syntax,
                    $"Invalid neighbor IP: {neighbor.IpAddress}", neighborLocation,
                    "Use a valid IP address");
            }

            // Check for authentication
            if (string.IsNullOrEmpty(neighbor.Password))
            {
                AddIssue(issues, ValidationSeverity.Warning, ValidationCategory.Security,
                    "BGP neighbor has no MD5 authentication", neighborLocation,
                    "Configure MD5 authentication for BGP security");
            }

            // Check for description
            if (string.IsNullOrEmpty(neighbor.Description))
            {
                AddIssue(issues, ValidationSeverity.Info, ValidationCategory.BestPractice,
                    "BGP neighbor has no description", neighborLocation,
                    "Add description for documentation");
            }
        }
    }

    private static void ValidateSecurity(NetworkDeviceConfig config, List<ValidationIssue> issues)
    {
        // Check enable secret
        if (string.IsNullOrEmpty(config.EnableSecret))
        {
            AddIssue(issues, ValidationSeverity.Warning, ValidationCategory.Security,
                "No enable secret configured", "Global",
                "Configure an enable secret for privileged access");
        }
        else if (WeakPasswords.Contains(config.EnableSecret))
        {
            AddIssue(issues, ValidationSeverity.Error, ValidationCategory.Security,
                "Weak enable secret detected", "Global",
                "Use a strong, unique password");
        }

        // Check for any BGP passwords
        if (config.Bgp is not null)
        {
            foreach (var neighbor in config.Bgp.Neighbors)
            {
                if (neighbor.Password is not null && WeakPasswords.Contains(neighbor.Password))
                {
                    AddIssue(issues, ValidationSeverity.Error, ValidationCategory.Security,
                        $"Weak BGP password for neighbor {neighbor.IpAddress}",
                        $"BGP neighbor {neighbor.IpAddress}",
                        "Use a strong, unique password");
                }
            }
        }
    }

    private static void CheckBestPractices(NetworkDeviceConfig config, List<ValidationIssue> issues)
    {
        // Check for NTP
        if (config.NtpServers.Count == 0)
        {
            AddIssue(issues, ValidationSeverity.Info, ValidationCategory.BestPractice,
                "No NTP servers configured", "Global",
                "Configure NTP for accurate time synchronization");
        }

        // Check for DNS
        if (config.DnsServers.Count == 0)
        {
            AddIssue(issues, ValidationSeverity.Info, ValidationCategory.BestPractice,
                "No DNS servers configured", "Global",
                "Configure DNS servers for name resolution");
        }

        // Check for domain name
        if (string.IsNullOrEmpty(config.DomainName))
        {
            AddIssue(issues, ValidationSeverity.Info, ValidationCategory.BestPractice,
                "No domain name configured", "Global",
                "Configure domain name for SSH key generation");
        }

        // Check for banner
        if (string.IsNullOrEmpty(config.BannerMotd))
        {
            AddIssue(issues, ValidationSeverity.Info, ValidationCategory.Security,
                "No login banner configured", "Global",
                "Configure a login banner for legal notice");
        }
    }

    private static void AddIssue(List<ValidationIssue> issues, ValidationSeverity severity,
        ValidationCategory category, string message, string location, string recommendation)
    {
        issues.Add(new ValidationIssue
        {
            Severity = severity,
            Category = category,
            Message = message,
            Location = location,
            Recommendation = recommendation,
        });
    }

    /// <summary>Check if string is a valid IPv4 address.</summary>
    private static bool IsValidIp(string ip) =>
        IPAddress.TryParse(ip, out var address) && address.AddressFamily == AddressFamily.InterNetwork;

    /// <summary>Check if string is a valid IP address or network.</summary>
    private static bool IsValidIpOrNetwork(string value)
    {
        if (IsValidIp(value))
            return true;

        // IPv4 network in CIDR form (e.g., "10.0.0.0/8"); host bits allowed (strict=False).
        var slash = value.IndexOf('/');
        if (slash <= 0 || slash == value.Length - 1)
            return false;

        var prefix = value[..slash];
        if (!IPAddress.TryParse(prefix, out var network) || network.AddressFamily != AddressFamily.InterNetwork)
            return false;

        return int.TryParse(value[(slash + 1)..], out var prefixLength) && prefixLength is >= 0 and <= 32;
    }

    /// <summary>Check if string is a valid subnet mask (contiguous 1s then 0s).</summary>
    private static bool IsValidSubnetMask(string mask)
    {
        var octets = mask.Split('.');
        if (octets.Length != 4)
            return false;

        var binary = new System.Text.StringBuilder();
        foreach (var octet in octets)
        {
            if (!int.TryParse(octet, out var value) || value < 0 || value > 255)
                return false;
            binary.Append(Convert.ToString(value, 2)!.PadLeft(8, '0'));
        }

        // Valid mask is all 1s followed by all 0s
        return !binary.ToString().Contains("01");
    }
}
