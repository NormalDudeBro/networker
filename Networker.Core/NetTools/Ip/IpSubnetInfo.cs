using System.Numerics;

namespace Networker.Core.NetTools.Ip;

public sealed class IpSubnetInfo
{
    public required string Input { get; init; }
    public required int PrefixLength { get; init; }
    public required string NetworkAddress { get; init; }
    public required string Netmask { get; init; }
    public required string WildcardMask { get; init; }
    public required string FirstUsable { get; init; }
    public required string LastUsable { get; init; }
    public string? BroadcastAddress { get; init; }
    public required BigInteger TotalHosts { get; init; }
    public required BigInteger UsableHosts { get; init; }
    public required int IpVersion { get; init; }
    public required bool IsPrivate { get; init; }
    public string? Description { get; init; }
    public bool IsPointToPoint => PrefixLength == 31 || PrefixLength == 127;
    public bool IsSingleHost => PrefixLength == 32 || PrefixLength == 128;

    public string Summary()
    {
        var hostCount = IsPointToPoint || IsSingleHost ? $"hosts: {UsableHosts}" : $"usable: {UsableHosts}";
        return $"{NetworkAddress}/{PrefixLength} · netmask {Netmask} · wildcard {WildcardMask} · {hostCount}";
    }
}

