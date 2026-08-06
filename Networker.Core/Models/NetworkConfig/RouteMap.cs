using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Networker.Core.Models.NetworkConfig;

/// <summary>
/// Route-map for policy-based routing and BGP policies.
/// </summary>
public sealed record RouteMap
{
    /// <summary>
    /// Route-map name.
    /// </summary>
    [Required]
    [MinLength(1)]
    public required string Name { get; init; }

    /// <summary>
    /// Route-map entries.
    /// </summary>
    public List<RouteMapEntry> Entries { get; init; } = new();
}