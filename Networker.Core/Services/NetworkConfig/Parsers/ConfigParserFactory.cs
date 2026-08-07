using System.Collections.Generic;
using Networker.Core.Models.NetworkConfig;

namespace Networker.Core.Services.NetworkConfig.Parsers;

/// <summary>
/// Factory for creating vendor-specific parsers.
/// Ported from NetworkConfigPro <c>src/core/parsers/config_parser.py</c>
/// <c>ConfigParserFactory</c>.
/// </summary>
public sealed class ConfigParserFactory : IConfigParserFactory
{
    // SONiC first (JSON format is very specific), Junos second (more specific
    // patterns), Cisco last — mirrors the Python _parsers ordering.
    private static readonly IConfigParser[] Parsers =
    [
        new SonicParser(),
        new JuniperJunosParser(),
        new CiscoIosParser(),
    ];

    /// <inheritdoc />
    public IConfigParser? GetParser(string configText)
    {
        foreach (var parser in Parsers)
        {
            if (parser.DetectVendor(configText))
            {
                return parser;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public IConfigParser? GetParser(Vendor vendor) => vendor switch
    {
        // Cisco NXOS and Arista EOS use similar syntax to IOS for basic parsing
        Vendor.CiscoIos or Vendor.CiscoNxos or Vendor.AristaEos => new CiscoIosParser(),
        Vendor.JuniperJunos => new JuniperJunosParser(),
        Vendor.Sonic => new SonicParser(),
        _ => null,
    };

    /// <summary>
    /// Detects the vendor and parses the configuration text.
    /// Mirrors Python's <c>detect_and_parse</c>.
    /// </summary>
    public ParseResult DetectAndParse(string configText)
    {
        foreach (var parser in Parsers)
        {
            if (parser.DetectVendor(configText))
            {
                return parser.Parse(configText);
            }
        }

        return new ParseResult
        {
            Config = null,
            Vendor = null,
            Errors = new List<string> { "Could not detect configuration vendor/format" },
            Warnings = new List<string>(),
        };
    }

    /// <summary>
    /// Parses configuration text with a known vendor.
    /// Mirrors Python's <c>parse_with_vendor</c>.
    /// </summary>
    public ParseResult ParseWithVendor(string configText, Vendor vendor)
    {
        var parser = GetParser(vendor);
        if (parser is null)
        {
            return new ParseResult
            {
                Config = null,
                Vendor = vendor,
                Errors = new List<string> { $"No parser available for vendor: {vendor}" },
                Warnings = new List<string>(),
            };
        }

        var result = parser.Parse(configText);
        // Override vendor to the specified one
        if (result.Config is not null)
        {
            result = result with { Config = result.Config with { Vendor = vendor } };
        }

        return result;
    }
}
