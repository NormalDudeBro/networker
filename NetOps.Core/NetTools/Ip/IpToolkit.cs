using System.Net;
using System.Numerics;

namespace NetOps.Core.NetTools.Ip;

public static class IpToolkit
{
    /// <summary>
    /// Calculates full subnet details for a CIDR block. Deterministic, pure math —
    /// no LLM involvement. Supports IPv4 and IPv6.
    /// </summary>
    public static IpSubnetInfo Calculate(string cidr)
    {
        if (string.IsNullOrWhiteSpace(cidr))
        {
            throw new ArgumentException("CIDR cannot be empty.", nameof(cidr));
        }

        var parts = cidr.Trim().Split('/');
        if (parts.Length != 2)
        {
            throw new FormatException($"'{cidr}' is not a valid CIDR. Expected 'address/prefix'.");
        }

        var addressText = parts[0].Trim();
        if (!int.TryParse(parts[1], out var prefix))
        {
            throw new FormatException($"'{parts[1]}' is not a valid prefix length.");
        }

        if (!IPAddress.TryParse(addressText, out var address) ||
            (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork &&
             address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6))
        {
            throw new FormatException($"'{addressText}' is not a valid IP address.");
        }

        var isV4 = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork;
        var maxPrefix = isV4 ? 32 : 128;
        if (prefix < 0 || prefix > maxPrefix)
        {
            throw new FormatException($"Prefix length {prefix} is out of range for {(isV4 ? "IPv4" : "IPv6")} (0-{maxPrefix}).");
        }

        return isV4
            ? CalculateV4(cidr.Trim(), address, prefix)
            : CalculateV6(cidr.Trim(), address, prefix);
    }

    /// <summary>
    /// Tests whether <paramref name="ip"/> falls inside the <paramref name="cidr"/> block.
    /// </summary>
    public static bool Contains(string cidr, string ip)
    {
        var subnet = Calculate(cidr);
        if (!IPAddress.TryParse(ip, out var address) ||
            address.AddressFamily != (subnet.IpVersion == 4 ? System.Net.Sockets.AddressFamily.InterNetwork : System.Net.Sockets.AddressFamily.InterNetworkV6))
        {
            return false;
        }

        return subnet.IpVersion switch
        {
            4 => ContainsV4(subnet, address),
            _ => ContainsV6(subnet, address),
        };
    }

    /// <summary>
    /// Splits a CIDR block into equal sub-blocks of the given prefix length.
    /// </summary>
    public static IReadOnlyList<string> Divide(string cidr, int newPrefix)
    {
        var subnet = Calculate(cidr);
        if (newPrefix < subnet.PrefixLength)
        {
            throw new ArgumentException($"New prefix ({newPrefix}) must be >= current prefix ({subnet.PrefixLength}).", nameof(newPrefix));
        }

        var results = new List<string>();
        if (subnet.IpVersion == 4)
        {
            var network = ToV4Uint(IPAddress.Parse(subnet.NetworkAddress));
            var step = 1u << (32 - newPrefix);
            var count = 1u << (newPrefix - subnet.PrefixLength);
            for (uint i = 0; i < count; i++)
            {
                results.Add($"{FromV4Uint(network + (i * step))}/{newPrefix}");
            }
        }
        else
        {
            var network = ToV6BigInteger(IPAddress.Parse(subnet.NetworkAddress));
            var step = BigInteger.One << (128 - newPrefix);
            var count = BigInteger.One << (newPrefix - subnet.PrefixLength);
            for (BigInteger i = 0; i < count; i++)
            {
                results.Add($"{FromV6BigInteger(network + (i * step))}/{newPrefix}");
            }
        }

        return results;
    }

    /// <summary>
    /// Finds the smallest summarizing CIDR that contains all listed blocks.
    /// </summary>
    public static string Summarize(IReadOnlyList<string> cidrs)
    {
        if (cidrs is null || cidrs.Count == 0)
        {
            throw new ArgumentException("At least one CIDR is required.", nameof(cidrs));
        }

        var subnets = cidrs.Select(Calculate).ToList();
        var v4 = subnets.Where(s => s.IpVersion == 4).ToList();
        var v6 = subnets.Where(s => s.IpVersion == 6).ToList();
        if (v4.Count > 0 && v6.Count > 0)
        {
            throw new ArgumentException("Cannot summarize mixed IPv4 and IPv6 blocks.");
        }

        return v4.Count > 0 ? SummarizeV4(v4) : SummarizeV6(v6);
    }

    private static IpSubnetInfo CalculateV4(string input, IPAddress address, int prefix)
    {
        uint value = ToV4Uint(address);
        uint mask = prefix == 0 ? 0u : 0xFFFFFFFFu << (32 - prefix);
        uint network = value & mask;
        uint wildcard = ~mask;
        uint broadcast = network | wildcard;

        uint firstUsable;
        uint lastUsable;
        BigInteger total = BigInteger.One << (32 - prefix);
        BigInteger usable;

        if (prefix >= 31)
        {
            firstUsable = network;
            lastUsable = broadcast;
            usable = prefix == 32 ? 1 : 2;
        }
        else
        {
            firstUsable = network + 1;
            lastUsable = broadcast - 1;
            usable = total - 2;
        }

        return new IpSubnetInfo
        {
            Input = input,
            PrefixLength = prefix,
            NetworkAddress = FromV4Uint(network),
            Netmask = FromV4Uint(mask),
            WildcardMask = FromV4Uint(wildcard),
            FirstUsable = FromV4Uint(firstUsable),
            LastUsable = FromV4Uint(lastUsable),
            BroadcastAddress = FromV4Uint(broadcast),
            TotalHosts = total,
            UsableHosts = usable,
            IpVersion = 4,
            IsPrivate = IsPrivateV4(network),
            Description = ClassV4(network),
        };
    }

    private static IpSubnetInfo CalculateV6(string input, IPAddress address, int prefix)
    {
        var value = ToV6BigInteger(address);
        var allOnes = (BigInteger.One << 128) - 1;
        BigInteger mask = prefix == 0 ? BigInteger.Zero : allOnes ^ ((BigInteger.One << (128 - prefix)) - 1);
        BigInteger network = value & mask;
        BigInteger last = network | ~mask & allOnes;

        BigInteger firstUsable;
        BigInteger lastUsable;
        BigInteger usable;

        if (prefix >= 127)
        {
            firstUsable = network;
            lastUsable = last;
            usable = prefix == 128 ? 1 : 2;
        }
        else
        {
            firstUsable = network + 1;
            lastUsable = last - 1;
            usable = (BigInteger.One << (128 - prefix)) - 2;
        }

        return new IpSubnetInfo
        {
            Input = input,
            PrefixLength = prefix,
            NetworkAddress = FromV6BigInteger(network),
            Netmask = FromV6BigInteger(mask),
            WildcardMask = FromV6BigInteger(mask ^ allOnes),
            FirstUsable = FromV6BigInteger(firstUsable),
            LastUsable = FromV6BigInteger(lastUsable),
            BroadcastAddress = FromV6BigInteger(last),
            TotalHosts = BigInteger.One << (128 - prefix),
            UsableHosts = usable,
            IpVersion = 6,
            IsPrivate = IsPrivateV6(network),
            Description = prefix >= 48 && prefix <= 64 ? "SLAAC / standard subnet" : null,
        };
    }

    private static bool ContainsV4(IpSubnetInfo subnet, IPAddress ip)
    {
        var value = ToV4Uint(ip);
        uint mask = 0xFFFFFFFFu << (32 - subnet.PrefixLength);
        return (value & mask) == ToV4Uint(IPAddress.Parse(subnet.NetworkAddress));
    }

    private static bool ContainsV6(IpSubnetInfo subnet, IPAddress ip)
    {
        var value = ToV6BigInteger(ip);
        var allOnes = (BigInteger.One << 128) - 1;
        BigInteger mask = subnet.PrefixLength == 0 ? BigInteger.Zero : allOnes ^ ((BigInteger.One << (128 - subnet.PrefixLength)) - 1);
        return (value & mask) == ToV6BigInteger(IPAddress.Parse(subnet.NetworkAddress));
    }

    private static string SummarizeV4(List<IpSubnetInfo> subnets)
    {
        var min = subnets.Min(s => ToV4Uint(IPAddress.Parse(s.NetworkAddress)));
        var max = subnets.Max(s => ToV4Uint(IPAddress.Parse(s.BroadcastAddress!)));

        for (var prefix = 32; prefix >= 0; prefix--)
        {
            uint mask = prefix == 0 ? 0u : 0xFFFFFFFFu << (32 - prefix);
            uint network = min & mask;
            uint broadcast = network | ~mask;
            if (network <= min && broadcast >= max)
            {
                return $"{FromV4Uint(network)}/{prefix}";
            }
        }

        throw new InvalidOperationException("Could not summarize blocks.");
    }

    private static string SummarizeV6(List<IpSubnetInfo> subnets)
    {
        BigInteger min = subnets.Min(s => ToV6BigInteger(IPAddress.Parse(s.NetworkAddress)));
        BigInteger max = subnets.Max(s => ToV6BigInteger(IPAddress.Parse(s.BroadcastAddress!)));
        var allOnes = (BigInteger.One << 128) - 1;

        for (var prefix = 128; prefix >= 0; prefix--)
        {
            BigInteger mask = prefix == 0 ? BigInteger.Zero : allOnes ^ ((BigInteger.One << (128 - prefix)) - 1);
            BigInteger network = min & mask;
            BigInteger broadcast = network | (~mask & allOnes);
            if (network <= min && broadcast >= max)
            {
                return $"{FromV6BigInteger(network)}/{prefix}";
            }
        }

        throw new InvalidOperationException("Could not summarize blocks.");
    }

    private static uint ToV4Uint(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
    }

    private static string FromV4Uint(uint value) => new IPAddress(ToV4Bytes(value)).ToString();

    private static byte[] ToV4Bytes(uint value) => new[]
    {
        (byte)(value >> 24),
        (byte)(value >> 16),
        (byte)(value >> 8),
        (byte)value,
    };

    private static BigInteger ToV6BigInteger(IPAddress address)
        => new(address.GetAddressBytes(), isUnsigned: true, isBigEndian: true);

    private static string FromV6BigInteger(BigInteger value)
    {
        var groups = new ushort[8];
        for (var i = 0; i < 8; i++)
        {
            groups[i] = (ushort)((value >> ((7 - i) * 16)) & 0xFFFF);
        }

        return FormatIpv6(groups);
    }

    /// <summary>
    /// Formats IPv6 groups using the canonical RFC 5952 rules: lowercase,
    /// leading zeros trimmed, and the longest run of zero groups (>= 2)
    /// compressed to "::" (leftmost run on ties).
    /// </summary>
    private static string FormatIpv6(ushort[] groups)
    {
        var bestStart = -1;
        var bestLength = 0;
        for (var i = 0; i < groups.Length;)
        {
            if (groups[i] != 0)
            {
                i++;
                continue;
            }

            var j = i;
            while (j < groups.Length && groups[j] == 0)
            {
                j++;
            }

            var length = j - i;
            if (length >= 2 && length > bestLength)
            {
                bestStart = i;
                bestLength = length;
            }

            i = j;
        }

        if (bestStart == 0 && bestLength == groups.Length)
        {
            return "::";
        }

        var parts = new List<string>(8);
        for (var i = 0; i < groups.Length; i++)
        {
            if (bestStart >= 0 && i == bestStart)
            {
                parts.Add(string.Empty);
                i += bestLength - 1;
            }
            else
            {
                parts.Add(groups[i].ToString("x"));
            }
        }

        var emptyIndex = parts.IndexOf(string.Empty);
        if (emptyIndex < 0)
        {
            return string.Join(':', parts);
        }

        var before = string.Join(':', parts.Take(emptyIndex));
        var after = string.Join(':', parts.Skip(emptyIndex + 1));
        return before + "::" + after;
    }

    private static bool IsPrivateV4(uint network)
    {
        return (network & 0xFF000000) == 0x0A000000    // 10.0.0.0/8
            || (network & 0xFFF00000) == 0xAC100000   // 172.16.0.0/12
            || (network & 0xFFFF0000) == 0xC0A80000   // 192.168.0.0/16
            || (network & 0xFF000000) == 0x7F000000   // 127.0.0.0/8 loopback
            || (network & 0xFFFF0000) == 0xA9FE0000;  // 169.254.0.0/16 link-local
    }

    private static bool IsPrivateV6(BigInteger network)
    {
        var first16 = (ushort)((network >> 112) & 0xFFFF);
        return (first16 & 0xFE00) == 0xFC00     // fc00::/7 unique local
            || first16 == 0xFE80                 // fe80::/10 link-local
            || first16 == 0x2001 && ((network >> 96) & 0xFFFF) == 0x0DB8; // documentation
    }

    private static string ClassV4(uint network)
    {
        var first = network >> 24;
        return first switch
        {
            <= 0x7F => "Class A",
            <= 0xBF => "Class B",
            <= 0xDF => "Class C",
            <= 0xEF => "Class D (multicast)",
            _ => "Class E (reserved)",
        };
    }

    public static string ToBinary(uint value) => Convert.ToString(value, 2).PadLeft(32, '0');
}
