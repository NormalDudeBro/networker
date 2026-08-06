using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Networker.Core.Models.NetworkConfig;

/// <summary>
/// Access Control List.
/// </summary>
public sealed record Acl
{
    /// <summary>
    /// ACL name.
    /// </summary>
    [Required]
    [MinLength(1)]
    public required string Name { get; init; }

    /// <summary>
    /// ACL entries.
    /// </summary>
    public List<AclEntry> Entries { get; init; } = new();

    /// <summary>
    /// Whether this is an extended ACL (vs standard).
    /// </summary>
    public bool IsExtended { get; init; } = true;

    /// <summary>
    /// Adds an entry to the ACL and maintains sequence order.
    /// </summary>
    public void AddEntry(AclEntry entry)
    {
        Entries.Add(entry);
        Entries.Sort((a, b) => a.Sequence.CompareTo(b.Sequence));
    }
}