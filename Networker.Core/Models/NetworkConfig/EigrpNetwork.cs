using System.ComponentModel.DataAnnotations;

namespace Networker.Core.Models.NetworkConfig;

/// <summary>
/// EIGRP network statement.
/// </summary>
public sealed record EigrpNetwork
{
    /// <summary>
    /// Network address.
    /// </summary>
    [Required]
    public required string Network { get; init; }

    /// <summary>
    /// Wildcard mask (optional, if null uses classful).
    /// </summary>
    public string? Wildcard { get; init; }
}