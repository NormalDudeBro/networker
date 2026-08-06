using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Networker.Core.Models.NetworkConfig;

/// <summary>
/// Complete device configuration.
/// </summary>
public sealed record NetworkDeviceConfig
{
    /// <summary>
    /// Device hostname.
    /// </summary>
    [Required]
    [MinLength(1)]
    [MaxLength(63)]
    [RegularExpression(@"^[a-zA-Z][a-zA-Z0-9\-_]*$")]
    public required string Hostname { get; init; }

    /// <summary>
    /// Target vendor.
    /// </summary>
    [Required]
    public required Vendor Vendor { get; init; }

    /// <summary>
    /// Network interfaces.
    /// </summary>
    public List<Interface> Interfaces { get; init; } = new();

    /// <summary>
    /// VLANs.
    /// </summary>
    public List<Vlan> Vlans { get; init; } = new();

    /// <summary>
    /// Access Control Lists.
    /// </summary>
    public List<Acl> Acls { get; init; } = new();

    /// <summary>
    /// Static routes.
    /// </summary>
    public List<StaticRoute> StaticRoutes { get; init; } = new();

    /// <summary>
    /// OSPF configuration (optional).
    /// </summary>
    public OspfConfig? Ospf { get; init; }

    /// <summary>
    /// EIGRP configuration (optional).
    /// </summary>
    public EigrpConfig? Eigrp { get; init; }

    /// <summary>
    /// BGP configuration (optional).
    /// </summary>
    public BgpConfig? Bgp { get; init; }

    /// <summary>
    /// STP configuration (optional).
    /// </summary>
    public StpConfig? Stp { get; init; }

    /// <summary>
    /// Prefix lists.
    /// </summary>
    public List<PrefixList> PrefixLists { get; init; } = new();

    /// <summary>
    /// Route maps.
    /// </summary>
    public List<RouteMap> RouteMaps { get; init; } = new();

    /// <summary>
    /// Enable secret (hashed).
    /// </summary>
    public string? EnableSecret { get; init; }

    /// <summary>
    /// Domain name.
    /// </summary>
    public string? DomainName { get; init; }

    /// <summary>
    /// DNS servers.
    /// </summary>
    public List<string> DnsServers { get; init; } = new();

    /// <summary>
    /// NTP servers.
    /// </summary>
    public List<string> NtpServers { get; init; } = new();

    /// <summary>
    /// Banner message of the day.
    /// </summary>
    public string BannerMotd { get; init; } = string.Empty;

    /// <summary>
    /// Validates the entire device configuration.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Hostname))
            throw new ValidationException("Hostname is not configured");

        if (!System.Text.RegularExpressions.Regex.IsMatch(Hostname, @"^[a-zA-Z][a-zA-Z0-9\-_]*$"))
            throw new ValidationException($"Invalid hostname format: {Hostname}");

        if (Hostname.Length > 63)
            throw new ValidationException($"Hostname too long: {Hostname.Length} characters (max 63)");

        foreach (var iface in Interfaces)
        {
            iface.Validate();
        }

        foreach (var vlan in Vlans)
        {
            vlan.Validate();
        }

        if (Ospf is not null)
            Ospf.Validate();

        if (Eigrp is not null)
            Eigrp.Validate();

        if (Bgp is not null)
            Bgp.Validate();

        foreach (var route in StaticRoutes)
        {
            route.Validate();
        }
    }
}