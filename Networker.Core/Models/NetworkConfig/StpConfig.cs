using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Networker.Core.Models.NetworkConfig;

/// <summary>
/// Spanning Tree Protocol configuration.
/// </summary>
public sealed record StpConfig
{
    /// <summary>
    /// STP mode.
    /// </summary>
    public StpMode Mode { get; init; } = StpMode.RapidPvst;

    /// <summary>
    /// Bridge priority (default 32768).
    /// </summary>
    [Range(0, 61440)]
    public int Priority { get; init; } = 32768;

    /// <summary>
    /// Root primary VLANs.
    /// </summary>
    public List<int> RootPrimaryVlans { get; init; } = new();

    /// <summary>
    /// Root secondary VLANs.
    /// </summary>
    public List<int> RootSecondaryVlans { get; init; } = new();

    /// <summary>
    /// Portfast default.
    /// </summary>
    public bool PortfastDefault { get; init; } = false;

    /// <summary>
    /// BPDU guard default.
    /// </summary>
    public bool BpduguardDefault { get; init; } = false;
}