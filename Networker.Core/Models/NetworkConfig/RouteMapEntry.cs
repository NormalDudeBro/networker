using System.ComponentModel.DataAnnotations;

namespace Networker.Core.Models.NetworkConfig;

/// <summary>
/// Single route-map entry.
/// </summary>
public sealed record RouteMapEntry
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
    /// Match prefix-list name.
    /// </summary>
    public string? MatchPrefixList { get; init; }

    /// <summary>
    /// Match AS-path.
    /// </summary>
    public string? MatchAsPath { get; init; }

    /// <summary>
    /// Match community.
    /// </summary>
    public string? MatchCommunity { get; init; }

    /// <summary>
    /// Set local preference.
    /// </summary>
    public int? SetLocalPref { get; init; }

    /// <summary>
    /// Set MED.
    /// </summary>
    public int? SetMed { get; init; }

    /// <summary>
    /// Set AS-path prepend.
    /// </summary>
    public string? SetAsPathPrepend { get; init; }

    /// <summary>
    /// Set community.
    /// </summary>
    public string? SetCommunity { get; init; }

    /// <summary>
    /// Set next hop.
    /// </summary>
    public string? SetNextHop { get; init; }

    /// <summary>
    /// Set weight.
    /// </summary>
    public int? SetWeight { get; init; }
}