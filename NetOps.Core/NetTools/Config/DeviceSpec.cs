namespace NetOps.Core.NetTools.Config;

public enum ConfigPlatform
{
    CiscoIosXe = 0,
    JuniperJunos = 1,
    AristaEos = 2,
    Vyos = 3,
}

public sealed class VlanSpec
{
    public required string Id { get; init; }
    public string? Name { get; init; }
    public string? InterfaceVlanIp { get; init; }
}

public sealed class InterfaceSpec
{
    public required string Name { get; init; }
    public string? Description { get; init; }

    /// <summary>"access" | "trunk" | "routed" | "l3"</summary>
    public string Mode { get; init; } = "access";

    public string? Vlan { get; init; }
    public string? AllowedVlans { get; init; }

    /// <summary>CIDR, e.g. "192.168.10.1/24".</summary>
    public string? Ip { get; init; }

    public bool Shutdown { get; init; }
    public string? Mtu { get; init; }
    public string? Speed { get; init; }
    public string? Duplex { get; init; }
}

public sealed record OspfAreaSpec(string Cidr, string Area);

public sealed record BgpNeighborSpec(string PeerIp, string RemoteAs, string? Description = null);

public sealed class AclEntrySpec
{
    public required string Name { get; init; }
    public required string Action { get; init; }
    public string? Protocol { get; init; }
    public string? Source { get; init; }
    public string? SourcePort { get; init; }
    public string? Destination { get; init; }
    public string? DestinationPort { get; init; }
    public bool Established { get; init; }
    public bool Log { get; init; }
}

public sealed class NatSpec
{
    public IReadOnlyList<string> Inside { get; init; } = Array.Empty<string>();
    public string? Outside { get; init; }
    public string? PoolStart { get; init; }
    public string? PoolEnd { get; init; }
    public string? PoolMask { get; init; }
    public string? AclName { get; init; }
}

public sealed class DeviceSpec
{
    public string Hostname { get; init; } = "switch";
    public string? DomainName { get; init; }
    public string? EnableSecret { get; init; }
    public string? Username { get; init; }
    public string? UsernameSecret { get; init; }
    public string? SnmpCommunity { get; init; }
    public string? LoggingHost { get; init; }
    public string? NtpServer { get; init; }

    public IReadOnlyList<VlanSpec> Vlans { get; init; } = Array.Empty<VlanSpec>();
    public IReadOnlyList<InterfaceSpec> Interfaces { get; init; } = Array.Empty<InterfaceSpec>();
    public IReadOnlyList<OspfAreaSpec> OspfAreas { get; init; } = Array.Empty<OspfAreaSpec>();
    public string? OspfProcessId { get; init; }
    public string? RouterId { get; init; }
    public IReadOnlyList<BgpNeighborSpec> BgpNeighbors { get; init; } = Array.Empty<BgpNeighborSpec>();
    public string? BgpAsn { get; init; }
    public IReadOnlyList<string> BgpNetworks { get; init; } = Array.Empty<string>();
    public bool BgpRedistributeConnected { get; init; }
    public IReadOnlyList<AclEntrySpec> Acls { get; init; } = Array.Empty<AclEntrySpec>();
    public NatSpec? Nat { get; init; }
}
