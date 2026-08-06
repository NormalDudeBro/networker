namespace Networker.Core.Models.NetworkConfig;

/// <summary>
/// Interface types.
/// </summary>
public enum InterfaceType
{
    Ethernet = 0,
    Gigabit = 1,
    TenGigabit = 2,
    FortyGigabit = 3,
    HundredGigabit = 4,
    Loopback = 5,
    Vlan = 6,
    PortChannel = 7,
    Management = 8,
}