using System.ComponentModel.DataAnnotations;

namespace Networker.Core.Models.NetworkConfig;

/// <summary>
/// VLAN configuration.
/// </summary>
public sealed record Vlan
{
    /// <summary>
    /// VLAN ID (1-4094).
    /// </summary>
    [Required]
    [Range(1, 4094)]
    public required int VlanId { get; init; }

    /// <summary>
    /// VLAN name.
    /// </summary>
    [Required]
    [MinLength(1)]
    public required string Name { get; init; }

    /// <summary>
    /// VLAN description.
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// VLAN state (active, suspended, etc.).
    /// </summary>
    public string State { get; init; } = "active";

    /// <summary>
    /// Validates the VLAN configuration.
    /// </summary>
    public void Validate()
    {
        if (VlanId < 1 || VlanId > 4094)
            throw new ValidationException($"VLAN ID must be between 1 and 4094, got {VlanId}");

        if (string.IsNullOrWhiteSpace(Name))
            throw new ValidationException("VLAN name cannot be empty");
    }
}