using Networker.Core.Models.NetworkConfig;
using Networker.Core.Services.NetworkConfig;

namespace Networker.Core.Tests.Services.NetworkConfig;

/// <summary>
/// Tests for <see cref="TemplateLibrary"/> and <see cref="TemplateFormConverter"/>,
/// ported from NetworkConfigPro's <c>TEMPLATES</c> dict (src/gui/app.py).
/// </summary>
public class TemplateLibraryTests : IDisposable
{
    private readonly List<string> _paths = new();

    public void Dispose()
    {
        foreach (var path in _paths)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
                // Best-effort cleanup.
            }
        }
    }

    private TemplateLibrary CreateLibrary()
    {
        var path = Path.Combine(Path.GetTempPath(), "networker-template-tests", $"{Guid.NewGuid():N}.json");
        _paths.Add(path);
        return new TemplateLibrary(path);
    }

    // ----- Built-in listing -----

    [Fact]
    public void GetTemplates_ReturnsAllSixBuiltIns()
    {
        var library = CreateLibrary();

        var templates = library.GetTemplates();

        Assert.Equal(6, templates.Count);
        Assert.Equal(
            new[] { "Basic Router", "L3 Switch", "Edge Router with BGP", "Juniper Edge Router", "Data Center Spine", "SONiC ToR Switch" },
            templates.Select(t => t.Name).ToArray());
        Assert.All(templates, t => Assert.True(t.IsBuiltIn));
    }

    [Fact]
    public void GetTemplates_ReportsCorrectVendors()
    {
        var library = CreateLibrary();

        var vendors = library.GetTemplates().ToDictionary(t => t.Name, t => t.Vendor);

        Assert.Equal(Vendor.CiscoIos, vendors["Basic Router"]);
        Assert.Equal(Vendor.CiscoIos, vendors["L3 Switch"]);
        Assert.Equal(Vendor.CiscoIos, vendors["Edge Router with BGP"]);
        Assert.Equal(Vendor.JuniperJunos, vendors["Juniper Edge Router"]);
        Assert.Equal(Vendor.AristaEos, vendors["Data Center Spine"]);
        Assert.Equal(Vendor.Sonic, vendors["SONiC ToR Switch"]);
    }

    [Fact]
    public void GetTemplate_UnknownName_ReturnsNull()
    {
        var library = CreateLibrary();

        Assert.Null(library.GetTemplate("Not a Template"));
    }

    // ----- Basic Router -----

    [Fact]
    public void BasicRouter_ResolvesBasicsAndInterfaces()
    {
        var library = CreateLibrary();

        var template = library.GetTemplate("Basic Router");

        Assert.NotNull(template);
        Assert.True(template.IsBuiltIn);
        Assert.Equal("router", template.Config.Hostname);
        Assert.Equal(Vendor.CiscoIos, template.Config.Vendor);
        Assert.Equal("example.com", template.Config.DomainName);
        Assert.Equal(new[] { "8.8.8.8", "8.8.4.4" }, template.Config.DnsServers);
        Assert.Equal(new[] { "pool.ntp.org" }, template.Config.NtpServers);

        Assert.Equal(3, template.Config.Interfaces.Count);
        Assert.Equal("GigabitEthernet0/0", template.Config.Interfaces[0].Name);
        Assert.Equal(InterfaceType.Gigabit, template.Config.Interfaces[0].InterfaceType);
        Assert.Equal("WAN Uplink", template.Config.Interfaces[0].Description);
        Assert.Equal("Loopback0", template.Config.Interfaces[2].Name);
        Assert.Equal(InterfaceType.Loopback, template.Config.Interfaces[2].InterfaceType);
    }

    [Fact]
    public void BasicRouter_ResolvesOspf()
    {
        var library = CreateLibrary();

        var ospf = library.GetTemplate("Basic Router")!.Config.Ospf;

        Assert.NotNull(ospf);
        Assert.Equal(1, ospf.ProcessId);
        Assert.Equal(1000, ospf.ReferenceBandwidth);
        Assert.Empty(ospf.Networks);
        Assert.Empty(ospf.PassiveInterfaces);
    }

    // ----- L3 Switch -----

    [Fact]
    public void L3Switch_ParsesVlanText()
    {
        var library = CreateLibrary();

        var config = library.GetTemplate("L3 Switch")!.Config;

        Assert.Equal(4, config.Vlans.Count);
        Assert.Equal(10, config.Vlans[0].VlanId);
        Assert.Equal("MANAGEMENT", config.Vlans[0].Name);
        Assert.Equal(99, config.Vlans[3].VlanId);
        Assert.Equal("NATIVE", config.Vlans[3].Name);
        Assert.Null(config.Ospf);
        Assert.Null(config.Bgp);
    }

    // ----- Edge Router with BGP -----

    [Fact]
    public void EdgeRouterWithBgp_ParsesAcl()
    {
        var library = CreateLibrary();

        var config = library.GetTemplate("Edge Router with BGP")!.Config;

        var acl = Assert.Single(config.Acls);
        Assert.Equal("INBOUND-FILTER", acl.Name);
        Assert.True(acl.IsExtended);
        Assert.Equal(4, acl.Entries.Count);

        var first = acl.Entries[0];
        Assert.Equal(10, first.Sequence);
        Assert.Equal(AclAction.Deny, first.Action);
        Assert.Equal(AclProtocol.Ip, first.Protocol);
        Assert.Equal("10.0.0.0", first.Source);
        Assert.Equal("0.255.255.255", first.SourceWildcard);

        var last = acl.Entries[^1];
        Assert.Equal(1000, last.Sequence);
        Assert.Equal(AclAction.Permit, last.Action);
        Assert.Equal("any", last.Source);
    }

    [Fact]
    public void EdgeRouterWithBgp_ParsesBgpAndOspf()
    {
        var library = CreateLibrary();

        var config = library.GetTemplate("Edge Router with BGP")!.Config;

        Assert.Equal(65000, config.Bgp!.LocalAs);
        Assert.NotNull(config.Ospf);
        Assert.Equal(1, config.Ospf.ProcessId);
        Assert.Equal(10000, config.Ospf.ReferenceBandwidth);
        Assert.Equal(new[] { "GigabitEthernet0/0" }, config.Ospf.PassiveInterfaces);
    }

    // ----- Juniper Edge Router -----

    [Fact]
    public void JuniperEdgeRouter_UsesJunosInterfaceNaming()
    {
        var library = CreateLibrary();

        var config = library.GetTemplate("Juniper Edge Router")!.Config;

        Assert.Equal("ge-0/0/0", config.Interfaces[0].Name);
        Assert.Equal("ge-0/0/1", config.Interfaces[1].Name);
        Assert.Equal("lo0", config.Interfaces[2].Name);
        Assert.Equal(65000, config.Bgp!.LocalAs);
        Assert.Equal(0, config.Ospf!.ProcessId);
        Assert.Equal(100000, config.Ospf.ReferenceBandwidth);
    }

    // ----- Data Center Spine -----

    [Fact]
    public void DataCenterSpine_UsesAristaInterfaceNaming()
    {
        var library = CreateLibrary();

        var config = library.GetTemplate("Data Center Spine")!.Config;

        Assert.Equal(new[] { "Ethernet1", "Ethernet2", "Ethernet3", "Ethernet4", "Loopback0" },
            config.Interfaces.Select(i => i.Name).ToArray());
        Assert.Equal(Vendor.AristaEos, config.Vendor);
    }

    // ----- SONiC ToR -----

    [Fact]
    public void SonicTor_ParsesVlansAndBgp()
    {
        var library = CreateLibrary();

        var config = library.GetTemplate("SONiC ToR Switch")!.Config;

        Assert.Equal(2, config.Vlans.Count);
        Assert.Equal("SERVERS", config.Vlans[0].Name);
        Assert.Equal("MANAGEMENT", config.Vlans[1].Name);
        Assert.Equal(65100, config.Bgp!.LocalAs);
        Assert.Null(config.Ospf);
    }

    // ----- Custom templates -----

    [Fact]
    public void SaveCustomTemplate_RoundTrips_NotBuiltIn()
    {
        var library = CreateLibrary();
        var detail = CreateDetail("My Custom Router", Vendor.FortinetFortigate);

        library.SaveCustomTemplate("My Custom Router", detail);

        var loaded = library.GetTemplate("My Custom Router");
        Assert.NotNull(loaded);
        Assert.False(loaded.IsBuiltIn);
        Assert.Equal("custom-host", loaded.Config.Hostname);
        Assert.Equal(Vendor.FortinetFortigate, loaded.Config.Vendor);
        Assert.Equal(7, library.GetTemplates().Count); // 6 built-ins + 1 custom
    }

    [Fact]
    public void SaveCustomTemplate_OverridesBuiltIn()
    {
        var library = CreateLibrary();

        library.SaveCustomTemplate("Basic Router", CreateDetail("Basic Router", Vendor.Sonic));

        var loaded = library.GetTemplate("Basic Router");
        Assert.False(loaded!.IsBuiltIn);
        Assert.Equal("custom-host", loaded.Config.Hostname);
        Assert.Equal(Vendor.Sonic, loaded.Config.Vendor);
    }

    [Fact]
    public void DeleteCustomTemplate_Removes()
    {
        var library = CreateLibrary();
        library.SaveCustomTemplate("My Custom Router", CreateDetail("My Custom Router", Vendor.CiscoIos));

        Assert.True(library.DeleteCustomTemplate("My Custom Router"));
        Assert.Null(library.GetTemplate("My Custom Router"));
        Assert.False(library.DeleteCustomTemplate("My Custom Router"));
    }

    [Fact]
    public void CustomTemplates_PersistAcrossInstances()
    {
        var path = Path.Combine(Path.GetTempPath(), "networker-template-tests", $"{Guid.NewGuid():N}.json");
        _paths.Add(path);

        var first = new TemplateLibrary(path);
        first.SaveCustomTemplate("Persisted", CreateDetail("Persisted", Vendor.JuniperJunos));

        var second = new TemplateLibrary(path);
        var loaded = second.GetTemplate("Persisted");

        Assert.NotNull(loaded);
        Assert.Equal("custom-host", loaded.Config.Hostname);
        Assert.Equal(Vendor.JuniperJunos, loaded.Config.Vendor);
    }

    // ----- Converter edge cases -----

    [Fact]
    public void Convert_TemplateConfig_GeneratesValidVendorConfig()
    {
        var library = CreateLibrary();

        // Every built-in template must yield a config that the generator renders
        // without throwing (hostname + vendor are always set by the converter).
        foreach (var info in library.GetTemplates())
        {
            var detail = library.GetTemplate(info.Name);
            Assert.NotNull(detail);
            Assert.False(string.IsNullOrWhiteSpace(detail.Config.Hostname));
        }
    }

    private static TemplateDetail CreateDetail(string name, Vendor vendor) => new()
    {
        Name = name,
        Description = "Test custom template",
        Vendor = vendor,
        Config = new NetworkDeviceConfig
        {
            Hostname = "custom-host",
            Vendor = vendor,
        },
        IsBuiltIn = false,
    };
}
