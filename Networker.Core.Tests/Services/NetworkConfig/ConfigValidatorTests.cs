using System.Net;
using Networker.Core.Models.NetworkConfig;
using Networker.Core.Services.NetworkConfig;

namespace Networker.Core.Tests.Services.NetworkConfig;

/// <summary>
/// Tests for the ConfigValidator, ported from NetworkConfigPro
/// tests/unit/test_validator.py. The Python test "invalid IP address on an
/// interface" cannot be ported: the C# Interface model stores IPAddress?,
/// which cannot represent an invalid address.
/// </summary>
public class ConfigValidatorTests
{
    private static readonly ConfigValidator Validator = new();

    private static NetworkDeviceConfig BaseConfig => new()
    {
        Hostname = "router1",
        Vendor = Vendor.CiscoIos,
    };

    private static NetworkDeviceConfig ValidConfig => new()
    {
        Hostname = "valid-router",
        Vendor = Vendor.CiscoIos,
        DomainName = "example.com",
        EnableSecret = "StrongPassword123!",
        DnsServers = new List<string> { "8.8.8.8" },
        NtpServers = new List<string> { "pool.ntp.org" },
        BannerMotd = "Authorized users only",
        Interfaces = new List<Interface>
        {
            new()
            {
                Name = "Gi0/0",
                InterfaceType = InterfaceType.Gigabit,
                Description = "WAN Link",
                IpAddress = IPAddress.Parse("10.0.0.1"),
                SubnetMask = IPAddress.Parse("255.255.255.0"),
            },
        },
    };

    // Hostname validation

    [Fact]
    public void ValidHostname_PassesValidation()
    {
        var issues = Validator.Validate(BaseConfig);

        var hostnameErrors = issues
            .Where(i => i.Message.Contains("hostname", StringComparison.OrdinalIgnoreCase)
                && i.Severity == ValidationSeverity.Error)
            .ToList();
        Assert.Empty(hostnameErrors);
    }

    [Fact]
    public void EmptyHostname_ReportsError()
    {
        var config = BaseConfig with { Hostname = "" };

        var issues = Validator.Validate(config);

        Assert.Contains(issues, i =>
            i.Severity == ValidationSeverity.Error && i.Message == "Hostname is not configured");
    }

    [Fact]
    public void InvalidHostnameFormat_ReportsError()
    {
        var config = BaseConfig with { Hostname = "123-invalid" };

        var issues = Validator.Validate(config);

        Assert.Contains(issues, i =>
            i.Severity == ValidationSeverity.Error && i.Message.Contains("Invalid hostname format"));
    }

    [Fact]
    public void HostnameTooLong_ReportsError()
    {
        var config = BaseConfig with { Hostname = new string('a', 64) };

        var issues = Validator.Validate(config);

        Assert.Contains(issues, i =>
            i.Severity == ValidationSeverity.Error && i.Message.Contains("too long", StringComparison.OrdinalIgnoreCase));
    }

    // Interface validation

    [Fact]
    public void ValidInterface_PassesValidation()
    {
        var issues = Validator.Validate(ValidConfig);

        Assert.Empty(issues);
    }

    [Fact]
    public void DuplicateIpAddresses_Reported()
    {
        var config = BaseConfig with
        {
            Interfaces = new List<Interface>
            {
                new()
                {
                    Name = "Gi0/0",
                    InterfaceType = InterfaceType.Gigabit,
                    IpAddress = IPAddress.Parse("10.0.0.1"),
                    SubnetMask = IPAddress.Parse("255.255.255.0"),
                },
                new()
                {
                    Name = "Gi0/1",
                    InterfaceType = InterfaceType.Gigabit,
                    IpAddress = IPAddress.Parse("10.0.0.1"),
                    SubnetMask = IPAddress.Parse("255.255.255.0"),
                },
            },
        };

        var issues = Validator.Validate(config);

        Assert.Contains(issues, i =>
            i.Severity == ValidationSeverity.Error && i.Message.Contains("Duplicate IP address"));
    }

    [Fact]
    public void InvalidSubnetMask_ReportsError()
    {
        var config = BaseConfig with
        {
            Interfaces = new List<Interface>
            {
                new()
                {
                    Name = "Gi0/0",
                    InterfaceType = InterfaceType.Gigabit,
                    IpAddress = IPAddress.Parse("10.0.0.1"),
                    SubnetMask = IPAddress.Parse("255.0.255.0"), // non-contiguous
                },
            },
        };

        var issues = Validator.Validate(config);

        Assert.Contains(issues, i =>
            i.Severity == ValidationSeverity.Error && i.Message.Contains("Invalid subnet mask"));
    }

    [Fact]
    public void MtuTooSmall_ReportsError()
    {
        var config = BaseConfig with
        {
            Interfaces = new List<Interface>
            {
                new()
                {
                    Name = "Gi0/0",
                    InterfaceType = InterfaceType.Gigabit,
                    Mtu = 500,
                },
            },
        };

        var issues = Validator.Validate(config);

        Assert.Contains(issues, i =>
            i.Severity == ValidationSeverity.Error && i.Message.Contains("MTU too small"));
    }

    [Fact]
    public void ReservedVlan_Warning()
    {
        var config = BaseConfig with
        {
            Interfaces = new List<Interface>
            {
                new()
                {
                    Name = "Gi0/0",
                    InterfaceType = InterfaceType.Gigabit,
                    VlanId = 1, // Reserved VLAN
                },
            },
        };

        var issues = Validator.Validate(config);

        Assert.Contains(issues, i =>
            i.Severity == ValidationSeverity.Warning && i.Message.Contains("reserved VLAN", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TrunkAllowingAllVlans_Warning()
    {
        var config = BaseConfig with
        {
            Interfaces = new List<Interface>
            {
                new()
                {
                    Name = "Gi0/0",
                    InterfaceType = InterfaceType.Gigabit,
                    IsTrunk = true,
                    TrunkAllowedVlans = null, // All VLANs allowed
                },
            },
        };

        var issues = Validator.Validate(config);

        Assert.Contains(issues, i =>
            i.Severity == ValidationSeverity.Warning
            && i.Category == ValidationCategory.Security
            && i.Message.Contains("all VLANs"));
    }

    [Fact]
    public void InterfaceWithIpButNoDescription_ReportsInfo()
    {
        var config = BaseConfig with
        {
            Interfaces = new List<Interface>
            {
                new()
                {
                    Name = "Gi0/0",
                    InterfaceType = InterfaceType.Gigabit,
                    IpAddress = IPAddress.Parse("10.0.0.1"),
                    SubnetMask = IPAddress.Parse("255.255.255.0"),
                    Description = "", // No description
                },
            },
        };

        var issues = Validator.Validate(config);

        Assert.Contains(issues, i =>
            i.Severity == ValidationSeverity.Info
            && i.Message.Contains("no description", StringComparison.OrdinalIgnoreCase));
    }

    // VLAN validation

    [Fact]
    public void DuplicateVlans_Reported()
    {
        var config = BaseConfig with
        {
            Hostname = "switch1",
            Vlans = new List<Vlan>
            {
                new() { VlanId = 10, Name = "DATA1" },
                new() { VlanId = 10, Name = "DATA2" },
            },
        };

        var issues = Validator.Validate(config);

        Assert.Contains(issues, i =>
            i.Severity == ValidationSeverity.Error && i.Message.Contains("Duplicate VLAN ID"));
    }

    [Fact]
    public void ReservedVlanDefinition_Warning()
    {
        var config = BaseConfig with
        {
            Vlans = new List<Vlan> { new() { VlanId = 1002, Name = "fddi-default" } },
        };

        var issues = Validator.Validate(config);

        Assert.Contains(issues, i =>
            i.Severity == ValidationSeverity.Warning && i.Message.Contains("reserved VLAN"));
    }

    [Fact]
    public void GenericVlanName_ReportsInfo()
    {
        var config = BaseConfig with
        {
            Hostname = "switch1",
            Vlans = new List<Vlan> { new() { VlanId = 10, Name = "VLAN" } },
        };

        var issues = Validator.Validate(config);

        Assert.Contains(issues, i =>
            i.Severity == ValidationSeverity.Info
            && i.Message.Contains("generic", StringComparison.OrdinalIgnoreCase));
    }

    // ACL validation

    [Fact]
    public void EmptyAcl_Warning()
    {
        var config = BaseConfig with
        {
            Acls = new List<Acl> { new() { Name = "EMPTY-ACL", Entries = new List<AclEntry>() } },
        };

        var issues = Validator.Validate(config);

        Assert.Contains(issues, i =>
            i.Severity == ValidationSeverity.Warning && i.Message.Contains("Empty ACL"));
    }

    [Fact]
    public void DuplicateSequenceNumbers_Reported()
    {
        var config = BaseConfig with
        {
            Acls = new List<Acl>
            {
                new()
                {
                    Name = "TEST-ACL",
                    Entries = new List<AclEntry>
                    {
                        new()
                        {
                            Sequence = 10,
                            Action = AclAction.Permit,
                            Protocol = AclProtocol.Ip,
                            Source = "any",
                            Destination = "any",
                        },
                        new()
                        {
                            Sequence = 10,
                            Action = AclAction.Deny,
                            Protocol = AclProtocol.Ip,
                            Source = "any",
                            Destination = "any",
                        },
                    },
                },
            },
        };

        var issues = Validator.Validate(config);

        Assert.Contains(issues, i =>
            i.Severity == ValidationSeverity.Error && i.Message.Contains("Duplicate sequence"));
    }

    [Fact]
    public void InvalidAclSource_ReportsError()
    {
        var config = BaseConfig with
        {
            Acls = new List<Acl>
            {
                new()
                {
                    Name = "TEST-ACL",
                    Entries = new List<AclEntry>
                    {
                        new()
                        {
                            Sequence = 10,
                            Action = AclAction.Permit,
                            Protocol = AclProtocol.Ip,
                            Source = "999.999.999.999",
                        },
                    },
                },
            },
        };

        var issues = Validator.Validate(config);

        Assert.Contains(issues, i =>
            i.Severity == ValidationSeverity.Error && i.Message.Contains("Invalid source in ACL"));
    }

    [Fact]
    public void AclWithPermitButNoDenyAny_ReportsImplicitDenyInfo()
    {
        var config = BaseConfig with
        {
            Acls = new List<Acl>
            {
                new()
                {
                    Name = "TEST-ACL",
                    Entries = new List<AclEntry>
                    {
                        new()
                        {
                            Sequence = 10,
                            Action = AclAction.Permit,
                            Protocol = AclProtocol.Ip,
                            Source = "10.0.0.0/8",
                            Destination = "any",
                        },
                    },
                },
            },
        };

        var issues = Validator.Validate(config);

        Assert.Contains(issues, i =>
            i.Severity == ValidationSeverity.Info
            && i.Category == ValidationCategory.Security
            && i.Message.Contains("implicit deny"));
    }

    // Static route validation

    [Fact]
    public void InvalidStaticRouteDestination_ReportsError()
    {
        var config = BaseConfig with
        {
            StaticRoutes = new List<StaticRoute>
            {
                new() { Destination = "999.999.999.999", Mask = "255.255.255.0", NextHop = "10.0.0.1" },
            },
        };

        var issues = Validator.Validate(config);

        Assert.Contains(issues, i =>
            i.Severity == ValidationSeverity.Error && i.Message.Contains("Invalid destination"));
    }

    [Fact]
    public void InvalidStaticRouteNextHop_ReportsError()
    {
        var config = BaseConfig with
        {
            StaticRoutes = new List<StaticRoute>
            {
                new() { Destination = "10.1.0.0", Mask = "255.255.255.0", NextHop = "not-an-ip" },
            },
        };

        var issues = Validator.Validate(config);

        Assert.Contains(issues, i =>
            i.Severity == ValidationSeverity.Error && i.Message.Contains("Invalid next-hop"));
    }

    [Fact]
    public void DuplicateStaticRoute_Warning()
    {
        var config = BaseConfig with
        {
            StaticRoutes = new List<StaticRoute>
            {
                new() { Destination = "10.1.0.0", Mask = "255.255.255.0", NextHop = "10.0.0.1" },
                new() { Destination = "10.1.0.0", Mask = "255.255.255.0", NextHop = "10.0.0.1" },
            },
        };

        var issues = Validator.Validate(config);

        Assert.Contains(issues, i =>
            i.Severity == ValidationSeverity.Warning && i.Message.Contains("Duplicate static route"));
    }

    [Fact]
    public void DefaultStaticRoute_ReportsInfo()
    {
        var config = BaseConfig with
        {
            StaticRoutes = new List<StaticRoute>
            {
                new() { Destination = "0.0.0.0", Mask = "0.0.0.0", NextHop = "10.0.0.1" },
            },
        };

        var issues = Validator.Validate(config);

        Assert.Contains(issues, i =>
            i.Severity == ValidationSeverity.Info && i.Message.Contains("Default route configured"));
    }

    // OSPF validation

    [Fact]
    public void OspfNoRouterId_Warning()
    {
        var config = BaseConfig with
        {
            Ospf = new OspfConfig
            {
                ProcessId = 1,
                RouterId = null,
                Networks = new List<OspfNetwork>
                {
                    new() { Network = "10.0.0.0", Wildcard = "0.0.0.255", Area = 0 },
                },
            },
        };

        var issues = Validator.Validate(config);

        Assert.Contains(issues, i =>
            i.Severity == ValidationSeverity.Warning
            && i.Message.Contains("router-id not explicitly configured"));
    }

    [Fact]
    public void OspfInvalidRouterId_ReportsError()
    {
        var config = BaseConfig with
        {
            Ospf = new OspfConfig
            {
                ProcessId = 1,
                RouterId = "not-an-ip",
                Networks = new List<OspfNetwork>
                {
                    new() { Network = "10.0.0.0", Wildcard = "0.0.0.255", Area = 0 },
                },
            },
        };

        var issues = Validator.Validate(config);

        Assert.Contains(issues, i =>
            i.Severity == ValidationSeverity.Error && i.Message.Contains("Invalid OSPF router-id"));
    }

    [Fact]
    public void OspfLowReferenceBandwidth_ReportsInfo()
    {
        var config = BaseConfig with
        {
            Ospf = new OspfConfig
            {
                ProcessId = 1,
                RouterId = "1.1.1.1",
                ReferenceBandwidth = 100, // below 1000
            },
        };

        var issues = Validator.Validate(config);

        Assert.Contains(issues, i =>
            i.Severity == ValidationSeverity.Info
            && i.Category == ValidationCategory.Performance
            && i.Message.Contains("reference bandwidth"));
    }

    [Fact]
    public void OspfNoNetworks_Warning()
    {
        var config = BaseConfig with
        {
            Ospf = new OspfConfig { ProcessId = 1, RouterId = "1.1.1.1" },
        };

        var issues = Validator.Validate(config);

        Assert.Contains(issues, i =>
            i.Severity == ValidationSeverity.Warning && i.Message.Contains("no networks advertised"));
    }

    // BGP validation

    [Fact]
    public void BgpNoAuthentication_Warning()
    {
        var config = BaseConfig with
        {
            Bgp = new BgpConfig
            {
                LocalAs = 65000,
                Neighbors = new List<BgpNeighbor>
                {
                    new() { IpAddress = "10.0.0.2", RemoteAs = 65001, Password = null },
                },
            },
        };

        var issues = Validator.Validate(config);

        Assert.Contains(issues, i =>
            i.Severity == ValidationSeverity.Warning
            && i.Category == ValidationCategory.Security
            && i.Message.Contains("no MD5 authentication"));
    }

    [Fact]
    public void BgpNoRouterId_Warning()
    {
        var config = BaseConfig with
        {
            Bgp = new BgpConfig
            {
                LocalAs = 65000,
                Neighbors = new List<BgpNeighbor>
                {
                    new() { IpAddress = "10.0.0.2", RemoteAs = 65001, Password = "secret123" },
                },
            },
        };

        var issues = Validator.Validate(config);

        Assert.Contains(issues, i =>
            i.Severity == ValidationSeverity.Warning
            && i.Message.Contains("BGP router-id not explicitly configured"));
    }

    [Fact]
    public void BgpNoNeighbors_Warning()
    {
        var config = BaseConfig with
        {
            Bgp = new BgpConfig { LocalAs = 65000, RouterId = "1.1.1.1" },
        };

        var issues = Validator.Validate(config);

        Assert.Contains(issues, i =>
            i.Severity == ValidationSeverity.Warning && i.Message.Contains("no neighbors defined"));
    }

    [Fact]
    public void InvalidBgpNeighborIp_ReportsError()
    {
        var config = BaseConfig with
        {
            Bgp = new BgpConfig
            {
                LocalAs = 65000,
                Neighbors = new List<BgpNeighbor>
                {
                    new() { IpAddress = "not-an-ip", RemoteAs = 65001, Password = "secret123" },
                },
            },
        };

        var issues = Validator.Validate(config);

        Assert.Contains(issues, i =>
            i.Severity == ValidationSeverity.Error && i.Message.Contains("Invalid neighbor IP"));
    }

    // Security validation

    [Fact]
    public void NoEnableSecret_Warning()
    {
        var config = BaseConfig with { EnableSecret = null };

        var issues = Validator.Validate(config);

        Assert.Contains(issues, i =>
            i.Severity == ValidationSeverity.Warning
            && i.Category == ValidationCategory.Security
            && i.Message.Contains("enable secret", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WeakEnableSecret_Detected()
    {
        var config = BaseConfig with { EnableSecret = "cisco" }; // Weak password

        var issues = Validator.Validate(config);

        Assert.Contains(issues, i =>
            i.Severity == ValidationSeverity.Error
            && i.Category == ValidationCategory.Security
            && i.Message.Contains("Weak enable secret"));
    }

    [Fact]
    public void WeakBgpPassword_Detected()
    {
        var config = BaseConfig with
        {
            EnableSecret = "StrongPassword123!",
            Bgp = new BgpConfig
            {
                LocalAs = 65000,
                Neighbors = new List<BgpNeighbor>
                {
                    new() { IpAddress = "10.0.0.2", RemoteAs = 65001, Password = "admin" },
                },
            },
        };

        var issues = Validator.Validate(config);

        Assert.Contains(issues, i =>
            i.Severity == ValidationSeverity.Error
            && i.Category == ValidationCategory.Security
            && i.Message.Contains("Weak BGP password"));
    }

    // Best-practice validation

    [Fact]
    public void NoNtp_ReportsInfo()
    {
        var config = BaseConfig with { NtpServers = new List<string>() };

        var issues = Validator.Validate(config);

        Assert.Contains(issues, i =>
            i.Severity == ValidationSeverity.Info && i.Message.Contains("NTP"));
    }

    [Fact]
    public void NoDns_ReportsInfo()
    {
        var config = BaseConfig with { DnsServers = new List<string>() };

        var issues = Validator.Validate(config);

        Assert.Contains(issues, i =>
            i.Severity == ValidationSeverity.Info && i.Message.Contains("DNS"));
    }

    [Fact]
    public void NoDomainName_ReportsInfo()
    {
        var config = BaseConfig with { DomainName = null };

        var issues = Validator.Validate(config);

        Assert.Contains(issues, i =>
            i.Severity == ValidationSeverity.Info && i.Message.Contains("domain name"));
    }

    [Fact]
    public void NoBanner_ReportsInfo()
    {
        var config = BaseConfig with { BannerMotd = "" };

        var issues = Validator.Validate(config);

        Assert.Contains(issues, i =>
            i.Severity == ValidationSeverity.Info
            && i.Message.Contains("banner", StringComparison.OrdinalIgnoreCase));
    }

    // Summary

    [Fact]
    public void GetSummary_ReturnsCountsBySeverityAndCategory()
    {
        var config = BaseConfig with
        {
            EnableSecret = "cisco", // Weak - ERROR/Security
            NtpServers = new List<string>(), // Missing - INFO/BestPractice
        };

        var issues = Validator.Validate(config);
        var summary = ConfigValidator.GetSummary(issues);

        // Weak enable secret (Error/Security) + missing NTP, DNS, domain, banner (Info)
        Assert.Equal(5, summary.Total);
        Assert.Equal(1, summary.BySeverity[ValidationSeverity.Error]);
        Assert.Equal(4, summary.BySeverity[ValidationSeverity.Info]);
        Assert.Equal(2, summary.ByCategory[ValidationCategory.Security]);
        Assert.Equal(3, summary.ByCategory[ValidationCategory.BestPractice]);
    }
}
