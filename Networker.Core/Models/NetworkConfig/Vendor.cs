namespace Networker.Core.Models.NetworkConfig;

/// <summary>
/// Supported network vendors.
/// </summary>
public enum Vendor
{
    CiscoIos = 0,
    CiscoNxos = 1,
    AristaEos = 2,
    JuniperJunos = 3,
    Sonic = 4,
    FortinetFortigate = 5,
}