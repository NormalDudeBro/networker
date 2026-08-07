using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Networker.Core.Services.NetworkConfig;

/// <summary>
/// Form-shaped template data, ported from the NetworkConfigPro GUI
/// <c>TEMPLATES</c> dict (<c>src/gui/app.py</c>). Each predefined template is
/// stored as a form preset (vendor + basics + interface rows + text fields for
/// VLANs/routes/ACLs/OSPF/BGP) rather than as a <c>NetworkDeviceConfig</c>, so
/// the raw preset survives for form population. <see cref="TemplateFormConverter"/>
/// turns this into a <c>NetworkDeviceConfig</c> mirroring the Python GUI's
/// <c>_generate_config</c> logic.
/// </summary>
public sealed class TemplateFormData
{
    public TemplateBasic Basic { get; set; } = new();
    public List<TemplateInterfaceEntry> Interfaces { get; set; } = new();
    public string Vlans { get; set; } = string.Empty;
    public TemplateAcl Acl { get; set; } = new();
    public string StaticRoutes { get; set; } = string.Empty;
    public TemplateOspf Ospf { get; set; } = new();
    public TemplateBgp Bgp { get; set; } = new();
    public TemplateEigrp Eigrp { get; set; } = new();
    public TemplateStp Stp { get; set; } = new();
}

/// <summary>
/// Device basics from a template preset.
/// </summary>
public sealed class TemplateBasic
{
    public string Vendor { get; set; } = string.Empty;
    public string Hostname { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string EnableSecret { get; set; } = string.Empty;
    public string DnsServers { get; set; } = string.Empty;
    public string NtpServers { get; set; } = string.Empty;
}

/// <summary>
/// One interface row from a template preset (type + number, vendor-resolved on
/// conversion).
/// </summary>
public sealed class TemplateInterfaceEntry
{
    public string Type { get; set; } = string.Empty;
    public string Number { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Ip { get; set; } = string.Empty;
    public string Mask { get; set; } = string.Empty;
}

/// <summary>
/// ACL preset (name + type + entries).
/// </summary>
public sealed class TemplateAcl
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "Extended";
    public List<TemplateAclEntry> Entries { get; set; } = new();
}

    /// <summary>
    /// One ACL entry preset (all fields text, matching the Python form rows).
    /// </summary>
    public sealed class TemplateAclEntry
    {
        /// <summary>
        /// Entry sequence number. Stored as text; JSON key is <c>seq</c> to
        /// match the Python form field name.
        /// </summary>
        [JsonPropertyName("seq")]
        public string Sequence { get; set; } = string.Empty;
    public string Action { get; set; } = "permit";
    public string Protocol { get; set; } = "ip";
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// Source wildcard mask; JSON key <c>src_wildcard</c> matches the Python form.
    /// </summary>
    [JsonPropertyName("src_wildcard")]
    public string SourceWildcard { get; set; } = string.Empty;

    public string Destination { get; set; } = string.Empty;

    /// <summary>
    /// Destination wildcard mask; JSON key <c>dst_wildcard</c> matches the Python form.
    /// </summary>
    [JsonPropertyName("dst_wildcard")]
    public string DestinationWildcard { get; set; } = string.Empty;

    /// <summary>
    /// Destination port; JSON key <c>dst_port</c> matches the Python form.
    /// </summary>
    [JsonPropertyName("dst_port")]
    public string DestinationPort { get; set; } = string.Empty;
    public string Log { get; set; } = string.Empty;
}

/// <summary>
/// OSPF preset.
/// </summary>
public sealed class TemplateOspf
{
    public string ProcessId { get; set; } = string.Empty;
    public string RouterId { get; set; } = string.Empty;

    /// <summary>
    /// OSPF reference bandwidth; JSON key <c>ref_bandwidth</c> matches the
    /// Python form field name.
    /// </summary>
    [JsonPropertyName("ref_bandwidth")]
    public string ReferenceBandwidth { get; set; } = string.Empty;
    public string Networks { get; set; } = string.Empty;
    public string PassiveInterfaces { get; set; } = string.Empty;
}

/// <summary>
/// BGP preset.
/// </summary>
public sealed class TemplateBgp
{
    public string LocalAs { get; set; } = string.Empty;
    public string RouterId { get; set; } = string.Empty;
    public List<TemplateBgpNeighbor> Neighbors { get; set; } = new();
    public string Networks { get; set; } = string.Empty;
}

/// <summary>
/// One BGP neighbor preset.
/// </summary>
public sealed class TemplateBgpNeighbor
{
    public string IpAddress { get; set; } = string.Empty;
    public string RemoteAs { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string UpdateSource { get; set; } = string.Empty;
    public string EbgpMultihop { get; set; } = string.Empty;
}

/// <summary>
/// EIGRP preset.
/// </summary>
public sealed class TemplateEigrp
{
    public string AsNumber { get; set; } = string.Empty;
    public string RouterId { get; set; } = string.Empty;
    public bool NamedMode { get; set; }

    /// <summary>
    /// Named-mode process name (Python default <c>EIGRP_PROCESS</c>).
    /// </summary>
    public string Name { get; set; } = "EIGRP_PROCESS";

    /// <summary>
    /// One <c>network[,wildcard]</c> per line.
    /// </summary>
    public string Networks { get; set; } = string.Empty;
    public string PassiveInterfaces { get; set; } = string.Empty;
}

/// <summary>
/// STP preset.
/// </summary>
public sealed class TemplateStp
{
    public string Mode { get; set; } = "rapid-pvst";
    public string Priority { get; set; } = string.Empty;
    public string RootPrimaryVlans { get; set; } = string.Empty;
    public string RootSecondaryVlans { get; set; } = string.Empty;
    public bool PortfastDefault { get; set; }
    public bool BpduguardDefault { get; set; }
}
