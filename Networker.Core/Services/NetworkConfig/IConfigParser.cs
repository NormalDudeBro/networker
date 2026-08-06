using Networker.Core.Models.NetworkConfig;

namespace Networker.Core.Services.NetworkConfig;

/// <summary>
/// Result of parsing a configuration.
/// </summary>
public sealed record ParseResult
{
    /// <summary>
    /// Parsed device configuration (null if parsing failed).
    /// </summary>
    public NetworkDeviceConfig? Config { get; init; }

    /// <summary>
    /// Detected vendor.
    /// </summary>
    public Vendor? Vendor { get; init; }

    /// <summary>
    /// Parse errors.
    /// </summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Parse warnings.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Whether parsing was successful.
    /// </summary>
    public bool Success => Config is not null && Errors.Count == 0;
}

/// <summary>
/// Configuration parser service interface.
/// </summary>
public interface IConfigParser
{
    /// <summary>
    /// Parses configuration text and returns a DeviceConfig.
    /// </summary>
    /// <param name="configText">Configuration text to parse.</param>
    /// <returns>Parse result with config, vendor, errors, and warnings.</param>
    ParseResult Parse(string configText);

    /// <summary>
    /// Checks if this parser can handle the given config.
    /// </summary>
    /// <param name="configText">Configuration text to check.</param>
    /// <returns>True if this parser can handle the config.</returns>
    bool DetectVendor(string configText);
}

/// <summary>
/// Factory for creating vendor-specific parsers.
/// </summary>
public interface IConfigParserFactory
{
    /// <summary>
    /// Gets the appropriate parser for the given configuration text.
    /// </summary>
    /// <param name="configText">Configuration text to parse.</param>
    /// <returns>Parser for the detected vendor, or null if no parser matches.</returns>
    IConfigParser? GetParser(string configText);

    /// <summary>
    /// Gets a parser for a specific vendor.
    /// </summary>
    /// <param name="vendor">Target vendor.</param>
    /// <returns>Parser for the vendor, or null if unsupported.</returns>
    IConfigParser? GetParser(Vendor vendor);
}