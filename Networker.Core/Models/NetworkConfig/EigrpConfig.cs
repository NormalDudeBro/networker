using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Networker.Core.Models.NetworkConfig;

/// <summary>
/// EIGRP configuration.
/// </summary>
public sealed record EigrpConfig
{
    /// <summary>
    /// EIGRP AS number.
    /// </summary>
    [Required]
    [Range(1, 65535)]
    public required int AsNumber { get; init; }

    /// <summary>
    /// EIGRP router ID (optional).
    /// </summary>
    public string? RouterId { get; init; }

    /// <summary>
    /// EIGRP network statements.
    /// </summary>
    public List<EigrpNetwork> Networks { get; init; } = new();

    /// <summary>
    /// Passive interfaces.
    /// </summary>
    public List<string> PassiveInterfaces { get; init; } = new();

    /// <summary>
    /// Auto-summary (default false).
    /// </summary>
    public bool AutoSummary { get; init; } = false;

    /// <summary>
    /// Protocols to redistribute (ospf, bgp, static, connected).
    /// </summary>
    public List<string> Redistribute { get; init; } = new();

    /// <summary>
    /// Use named EIGRP mode.
    /// </summary>
    public bool NamedMode { get; init; } = false;

    /// <summary>
    /// Name for named mode.
    /// </summary>
    public string Name { get; init; } = "EIGRP_PROCESS";

    /// <summary>
    /// Validates the EIGRP configuration.
    /// </summary>
    public void Validate()
    {
        if (AsNumber < 1 || AsNumber > 65535)
            throw new ValidationException($"EIGRP AS must be 1-65535, got {AsNumber}");
    }
}