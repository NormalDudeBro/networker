using System.ComponentModel.DataAnnotations;

namespace Networker.Core.Models.NetworkConfig;

/// <summary>
/// BGP neighbor configuration.
/// </summary>
public sealed record BgpNeighbor
{
    /// <summary>
    /// Neighbor IP address.
    /// </summary>
    [Required]
    public required string IpAddress { get; init; }

    /// <summary>
    /// Remote AS number.
    /// </summary>
    [Required]
    [Range(1, 4294967295)]
    public required int RemoteAs { get; init; }

    /// <summary>
    /// Neighbor description.
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// MD5 password for authentication.
    /// </summary>
    public string? Password { get; init; }

    /// <summary>
    /// Update source interface.
    /// </summary>
    public string? UpdateSource { get; init; }

    /// <summary>
    /// eBGP multihop TTL (0 = disabled).
    /// </summary>
    [Range(0, 255)]
    public int EbgpMultihop { get; init; }

    /// <summary>
    /// Inbound route map name.
    /// </summary>
    public string? RouteMapIn { get; init; }

    /// <summary>
    /// Outbound route map name.
    /// </summary>
    public string? RouteMapOut { get; init; }
}