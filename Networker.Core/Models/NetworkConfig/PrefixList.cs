using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Networker.Core.Models.NetworkConfig;

/// <summary>
/// IP prefix-list for route filtering.
/// </summary>
public sealed record PrefixList
{
    /// <summary>
    /// Prefix-list name.
    /// </summary>
    [Required]
    [MinLength(1)]
    public required string Name { get; init; }

    /// <summary>
    /// Prefix-list entries.
    /// </summary>
    public List<PrefixListEntry> Entries { get; init; } = new();
}