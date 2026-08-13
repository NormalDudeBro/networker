using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Networker.Core.Models.NetworkConfig;

/// <summary>
/// BGP configuration.
/// </summary>
public sealed record BgpConfig
{
    /// <summary>
    /// Local AS number.
    /// </summary>
    [Required]
    [Range(1, int.MaxValue)]
    public required int LocalAs { get; init; }

    /// <summary>
    /// BGP router ID (optional).
    /// </summary>
    public string? RouterId { get; init; }

    /// <summary>
    /// BGP neighbors.
    /// </summary>
    public List<BgpNeighbor> Neighbors { get; init; } = new();

    /// <summary>
    /// Networks to advertise.
    /// </summary>
    public List<string> Networks { get; init; } = new();

    /// <summary>
    /// Log neighbor changes.
    /// </summary>
    public bool LogNeighborChanges { get; init; } = true;

    /// <summary>
    /// Protocols to redistribute (ospf, eigrp, static, connected).
    /// </summary>
    public List<string> Redistribute { get; init; } = new();

    /// <summary>
    /// Validates the BGP configuration.
    /// </summary>
    public void Validate()
    {
        if (LocalAs < 1)
            throw new ValidationException($"BGP AS must be between 1 and {int.MaxValue}, got {LocalAs}");
    }
}
