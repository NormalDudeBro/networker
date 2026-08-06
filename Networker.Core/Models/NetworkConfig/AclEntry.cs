using System.ComponentModel.DataAnnotations;

namespace Networker.Core.Models.NetworkConfig;

/// <summary>
/// Single ACL entry/rule.
/// </summary>
public sealed record AclEntry
{
    /// <summary>
    /// Sequence number for ordering.
    /// </summary>
    [Required]
    [Range(1, 4294967295)]
    public required int Sequence { get; init; }

    /// <summary>
    /// Action (permit or deny).
    /// </summary>
    [Required]
    public required AclAction Action { get; init; }

    /// <summary>
    /// Protocol (ip, tcp, udp, icmp).
    /// </summary>
    [Required]
    public required AclProtocol Protocol { get; init; }

    /// <summary>
    /// Source address (e.g., "192.168.1.0" or "any").
    /// </summary>
    [Required]
    public required string Source { get; init; }

    /// <summary>
    /// Source wildcard mask (default "0.0.0.0").
    /// </summary>
    public string SourceWildcard { get; init; } = "0.0.0.0";

    /// <summary>
    /// Destination address (e.g., "192.168.2.0" or "any").
    /// </summary>
    public string Destination { get; init; } = "any";

    /// <summary>
    /// Destination wildcard mask (default "0.0.0.0").
    /// </summary>
    public string DestinationWildcard { get; init; } = "0.0.0.0";

    /// <summary>
    /// Source port (optional).
    /// </summary>
    public string? SourcePort { get; init; }

    /// <summary>
    /// Destination port (optional).
    /// </summary>
    public string? DestinationPort { get; init; }

    /// <summary>
    /// Whether to log matches.
    /// </summary>
    public bool Log { get; init; }

    /// <summary>
    /// Remark/comment for this entry.
    /// </summary>
    public string Remark { get; init; } = string.Empty;
}