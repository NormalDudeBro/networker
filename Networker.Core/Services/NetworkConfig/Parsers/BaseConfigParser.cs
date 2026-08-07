using System.Net;
using Networker.Core.Models.NetworkConfig;

namespace Networker.Core.Services.NetworkConfig.Parsers;

/// <summary>
/// Base class for configuration parsers.
/// Ported from NetworkConfigPro <c>src/core/parsers/config_parser.py</c>
/// <c>BaseConfigParser</c> (an ABC with <c>parse</c>/<c>detect_vendor</c>).
/// </summary>
public abstract class BaseConfigParser : IConfigParser
{
    /// <inheritdoc />
    public abstract ParseResult Parse(string configText);

    /// <inheritdoc />
    public abstract bool DetectVendor(string configText);

    /// <summary>
    /// Normalizes CRLF line endings so regex behavior matches the Python
    /// parser on Linux (where pasted configs carry only <c>\n</c>).
    /// </summary>
    protected static string NormalizeNewlines(string text) => text.Replace("\r\n", "\n");

    /// <summary>
    /// Checks if a string is a valid IPv4 address (mirrors Python's
    /// <c>_is_valid_ip</c>: four octets, each 0-255).
    /// </summary>
    protected static bool IsValidIp(string ip)
    {
        var parts = ip.Split('.');
        if (parts.Length != 4)
        {
            return false;
        }

        foreach (var part in parts)
        {
            if (!int.TryParse(part, out var octet) || octet < 0 || octet > 255)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Parses a string as an IPv4 address, returning null for invalid input.
    /// Call sites guard with IPv4-shaped regexes/dotted splits, so this is
    /// equivalent to Python's <c>ipaddress.ip_address</c> usage.
    /// </summary>
    protected static IPAddress? TryParseIp(string value) =>
        IPAddress.TryParse(value, out var ip) ? ip : null;

    /// <summary>
    /// Converts a CIDR prefix to a dotted-decimal netmask (mirrors Python's
    /// <c>_cidr_to_netmask</c>; out-of-range prefixes yield
    /// <c>255.255.255.255</c>).
    /// </summary>
    protected static string CidrToNetmask(int prefix)
    {
        if (prefix < 0 || prefix > 32)
        {
            return "255.255.255.255";
        }

        // C# masks shift counts mod 32, so a /0 (shift by 32) must be handled
        // explicitly to match Python's arbitrary-precision shift.
        var mask = prefix == 0 ? 0u : 0xFFFFFFFFu << (32 - prefix);
        return FormatIpv4(mask);
    }

    /// <summary>
    /// Converts a CIDR prefix to a wildcard mask (mirrors Python's
    /// <c>_cidr_to_wildcard</c>; out-of-range prefixes yield <c>0.0.0.0</c>).
    /// </summary>
    protected static string CidrToWildcard(int prefix)
    {
        if (prefix < 0 || prefix > 32)
        {
            return "0.0.0.0";
        }

        var wildcard = prefix >= 32 ? 0u : 0xFFFFFFFFu >> prefix;
        return FormatIpv4(wildcard);
    }

    private static string FormatIpv4(uint value) =>
        $"{(value >> 24) & 0xFF}.{(value >> 16) & 0xFF}.{(value >> 8) & 0xFF}.{value & 0xFF}";
}
