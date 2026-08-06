using System.ComponentModel.DataAnnotations;

namespace Networker.Core.Models.NetworkConfig;

/// <summary>
/// OSPF network statement.
/// </summary>
public sealed record OspfNetwork
{
    /// <summary>
    /// Network address.
    /// </summary>
    [Required]
    public required string Network { get; init; }

    /// <summary>
    /// Wildcard mask.
    /// </summary>
    [Required]
    public required string Wildcard { get; init; }

    /// <summary>
    /// OSPF area ID.
    /// </summary>
    [Required]
    [Range(0, int.MaxValue)]
    public required int Area { get; init; }
}