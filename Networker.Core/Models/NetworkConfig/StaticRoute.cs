using System.ComponentModel.DataAnnotations;

namespace Networker.Core.Models.NetworkConfig;

/// <summary>
/// Static route configuration.
/// </summary>
public sealed record StaticRoute
{
    /// <summary>
    /// Destination network.
    /// </summary>
    [Required]
    public required string Destination { get; init; }

    /// <summary>
    /// Destination subnet mask.
    /// </summary>
    [Required]
    public required string Mask { get; init; }

    /// <summary>
    /// Next-hop IP address.
    /// </summary>
    [Required]
    public required string NextHop { get; init; }

    /// <summary>
    /// Administrative distance (default 1).
    /// </summary>
    [Range(1, 255)]
    public int AdminDistance { get; init; } = 1;

    /// <summary>
    /// Route name/description.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Whether route is permanent.
    /// </summary>
    public bool Permanent { get; init; }

    /// <summary>
    /// Validates the static route configuration.
    /// </summary>
    public void Validate()
    {
        if (AdminDistance < 1 || AdminDistance > 255)
            throw new ValidationException($"Admin distance must be 1-255, got {AdminDistance}");
    }
}