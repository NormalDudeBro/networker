using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.NetworkInformation;

namespace Networker.Core.Models.NetworkConfig;

/// <summary>
/// Network interface configuration.
/// </summary>
public sealed record Interface
{
    /// <summary>
    /// Interface name (e.g., "GigabitEthernet0/0").
    /// </summary>
    [Required]
    [MinLength(1)]
    public required string Name { get; init; }

    /// <summary>
    /// Interface type.
    /// </summary>
    public required InterfaceType InterfaceType { get; init; }

    /// <summary>
    /// Interface description.
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// IPv4 address (optional).
    /// </summary>
    public IPAddress? IpAddress { get; init; }

    /// <summary>
    /// IPv4 subnet mask (optional).
    /// </summary>
    public IPAddress? SubnetMask { get; init; }

    /// <summary>
    /// Whether the interface is administratively enabled.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Interface speed (e.g., "1000", "10000").
    /// </summary>
    public string? Speed { get; init; }

    /// <summary>
    /// Duplex mode (e.g., "full", "half", "auto").
    /// </summary>
    public string? Duplex { get; init; }

    /// <summary>
    /// Maximum transmission unit.
    /// </summary>
    [Range(64, 9216)]
    public int Mtu { get; init; } = 1500;

    // L2 Switching
    /// <summary>
    /// Switchport mode (access, trunk, etc.).
    /// </summary>
    public SwitchportMode? SwitchportMode { get; init; }

    /// <summary>
    /// Access VLAN ID.
    /// </summary>
    [Range(1, 4094)]
    public int? AccessVlan { get; init; }

    /// <summary>
    /// Voice VLAN ID.
    /// </summary>
    [Range(1, 4094)]
    public int? VoiceVlan { get; init; }

    /// <summary>
    /// Comma-separated list of allowed VLANs on trunk.
    /// </summary>
    public string? TrunkAllowedVlans { get; init; }

    /// <summary>
    /// Native VLAN for trunk.
    /// </summary>
    [Range(1, 4094)]
    public int? TrunkNativeVlan { get; init; }

    // Legacy fields (kept for compatibility)
    /// <summary>
    /// Legacy VLAN ID (use AccessVlan instead).
    /// </summary>
    [Range(1, 4094)]
    public int? VlanId { get; init; }

    /// <summary>
    /// Legacy trunk flag (use SwitchportMode instead).
    /// </summary>
    public bool IsTrunk { get; init; }

    /// <summary>
    /// Legacy native VLAN (use TrunkNativeVlan instead).
    /// </summary>
    [Range(1, 4094)]
    public int? NativeVlan { get; init; }

    /// <summary>
    /// Channel group number for EtherChannel.
    /// </summary>
    public int? ChannelGroup { get; init; }

    /// <summary>
    /// Channel group mode (active, passive, on).
    /// </summary>
    public string? ChannelGroupMode { get; init; }

    /// <summary>
    /// Validates the interface configuration.
    /// </summary>
    public void Validate()
    {
        if (AccessVlan is not null && (AccessVlan < 1 || AccessVlan > 4094))
            throw new ValidationException($"Access VLAN must be between 1 and 4094, got {AccessVlan}");

        if (VoiceVlan is not null && (VoiceVlan < 1 || VoiceVlan > 4094))
            throw new ValidationException($"Voice VLAN must be between 1 and 4094, got {VoiceVlan}");

        if (Mtu < 64 || Mtu > 9216)
            throw new ValidationException($"MTU must be between 64 and 9216, got {Mtu}");

        if (TrunkNativeVlan is not null && (TrunkNativeVlan < 1 || TrunkNativeVlan > 4094))
            throw new ValidationException($"Trunk native VLAN must be between 1 and 4094, got {TrunkNativeVlan}");

        if (VlanId is not null && (VlanId < 1 || VlanId > 4094))
            throw new ValidationException($"VLAN ID must be between 1 and 4094, got {VlanId}");

        if (TrunkNativeVlan is not null && (TrunkNativeVlan < 1 || TrunkNativeVlan > 4094))
            throw new ValidationException($"Native VLAN must be between 1 and 4094, got {TrunkNativeVlan}");
    }
}