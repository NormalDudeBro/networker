using System.ComponentModel.DataAnnotations;

namespace Networker.Core.Models.NetworkConfig;

/// <summary>
/// Single prefix-list entry.
/// </summary>
public sealed record PrefixListEntry
{
    /// <summary>
    /// Sequence number.
    /// </summary>
    [Required]
    [Range(1, 4294967295)]
    public required int Sequence { get; init; }

    /// <summary>
    /// Action (permit or deny).
    /// </summary>
    [Required]
    public required string Action { get; init; }

    /// <summary>
    /// Prefix (e.g., "10.0.0.0/8").
    /// </summary>
    [Required]
    public required string Prefix { get; init; }

    /// <summary>
    /// Greater than or equal (ge) value.
    /// </summary>
    public int? Ge { get; init; }

    /// <summary>
    /// Less than or equal (le) value.
    /// </summary>
    public int? Le { get; init; }
}