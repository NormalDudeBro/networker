using Networker.Core.Models.NetworkConfig;

namespace Networker.Core.Services.NetworkConfig;

/// <summary>
/// Configuration generator service interface.
/// </summary>
public interface IConfigGenerator
{
    /// <summary>
    /// Generates a configuration from a device config object.
    /// </summary>
    /// <param name="config">The device configuration.</param>
    /// <returns>Generated configuration as a string.</returns>
    string Generate(NetworkDeviceConfig config);

    /// <summary>
    /// Generates a configuration from a dictionary.
    /// </summary>
    /// <param name="vendor">Target vendor.</param>
    /// <param name="configDict">Configuration as a dictionary.</param>
    /// <returns>Generated configuration as a string.</returns>
    string GenerateFromDict(Vendor vendor, IReadOnlyDictionary<string, object> configDict);

    /// <summary>
    /// Gets the list of supported vendors.
    /// </summary>
    /// <returns>List of supported vendors.</returns>
    IReadOnlyList<Vendor> GetSupportedVendors();
}