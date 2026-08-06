using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Networker.Core.Models.NetworkConfig;

/// <summary>
/// OSPF configuration.
/// </summary>
public sealed record OspfConfig
{
    /// <summary>
    /// OSPF process ID.
    /// </summary>
    [Required]
    [Range(0, 65535)]
    public required int ProcessId { get; init; }

    /// <summary>
    /// OSPF router ID (optional).
    /// </summary>
    public string? RouterId { get; init; }

    /// <summary>
    /// OSPF network statements.
    /// </summary>
    public List<OspfNetwork> Networks { get; init; } = new();

    /// <summary>
    /// Passive interfaces.
    /// </summary>
    public List<string> PassiveInterfaces { get; init; } = new();

    /// <summary>
    /// Whether to originate default information.
    /// </summary>
    public bool DefaultInformationOriginate { get; init; }

    /// <summary>
    /// Reference bandwidth for cost calculation (default 100 Mbps).
    /// </summary>
    [Range(1, 4294967)]
    public int ReferenceBandwidth { get; init; } = 100;

    /// <summary>
    /// Validates the OSPF configuration.
    /// </summary>
    public void Validate()
    {
        if (ProcessId < 0 || ProcessId > 65535)
            throw new ValidationException($"OSPF process ID must be 0-65535, got {ProcessId}");

        if (ReferenceBandwidth < 1 || ReferenceBandwidth > 4294967)
            throw new ValidationException($"Reference bandwidth must be 1-4294967, got {ReferenceBandwidth}");
    }
}