using Networker.Core.Models.NetworkConfig;
using Networker.Core.Services.NetworkConfig;

namespace Networker.Core.Tests.Services.NetworkConfig;

/// <summary>
/// Direct tests for <see cref="TemplateFormConverter"/>, mirroring the Python
/// GUI's <c>_generate_config</c> build rules.
/// </summary>
public class TemplateFormConverterTests
{
    private static TemplateFormData Form(string vendor = "Cisco IOS/IOS-XE") => new()
    {
        Basic = new TemplateBasic
        {
            Vendor = vendor,
            Hostname = "edge",
        },
    };

    // ----- Interfaces -----

    [Fact]
    public void Convert_InterfaceWithoutNumber_IsSkipped()
    {
        var form = Form();
        form.Interfaces.Add(new TemplateInterfaceEntry { Type = "GigabitEthernet", Number = "" });
        form.Interfaces.Add(new TemplateInterfaceEntry { Type = "GigabitEthernet", Number = "0/1", Description = "Kept" });

        var config = TemplateFormConverter.Convert(form);

        var iface = Assert.Single(config.Interfaces);
        Assert.Equal("GigabitEthernet0/1", iface.Name);
    }

    // ----- EIGRP -----

    [Fact]
    public void Convert_EigrpConfigured_ProducesEigrp()
    {
        var form = Form();
        form.Eigrp.AsNumber = "100";
        form.Eigrp.RouterId = "1.1.1.1";
        form.Eigrp.NamedMode = true;
        form.Eigrp.Name = "CUSTOM_PROCESS";
        form.Eigrp.Networks = "10.0.0.0,0.0.0.255\n192.168.1.0";
        form.Eigrp.PassiveInterfaces = "GigabitEthernet0/1, GigabitEthernet0/2";

        var config = TemplateFormConverter.Convert(form);

        Assert.NotNull(config.Eigrp);
        var eigrp = config.Eigrp!;
        Assert.Equal(100, eigrp.AsNumber);
        Assert.Equal("1.1.1.1", eigrp.RouterId);
        Assert.True(eigrp.NamedMode);
        Assert.Equal("CUSTOM_PROCESS", eigrp.Name);
        Assert.Equal(2, eigrp.Networks.Count);
        Assert.Equal("10.0.0.0", eigrp.Networks[0].Network);
        Assert.Equal("0.0.0.255", eigrp.Networks[0].Wildcard);
        Assert.Null(eigrp.Networks[1].Wildcard);
        Assert.Equal(new[] { "GigabitEthernet0/1", "GigabitEthernet0/2" }, eigrp.PassiveInterfaces);
    }

    [Fact]
    public void Convert_EigrpWithoutAs_ReturnsNull()
    {
        var form = Form();
        form.Eigrp.Networks = "10.0.0.0";

        Assert.Null(TemplateFormConverter.Convert(form).Eigrp);
    }

    // ----- STP -----

    [Fact]
    public void Convert_StpConfigured_ProducesStp()
    {
        var form = Form();
        form.Stp.Mode = "mst";
        form.Stp.Priority = "4096";
        form.Stp.RootPrimaryVlans = "10,20";
        form.Stp.RootSecondaryVlans = "30";
        form.Stp.PortfastDefault = true;
        form.Stp.BpduguardDefault = true;

        var config = TemplateFormConverter.Convert(form);

        Assert.NotNull(config.Stp);
        var stp = config.Stp!;
        Assert.Equal(StpMode.Mst, stp.Mode);
        Assert.Equal(4096, stp.Priority);
        Assert.Equal(new[] { 10, 20 }, stp.RootPrimaryVlans);
        Assert.Equal(new[] { 30 }, stp.RootSecondaryVlans);
        Assert.True(stp.PortfastDefault);
        Assert.True(stp.BpduguardDefault);
    }

    [Fact]
    public void Convert_StpNothingConfigured_ReturnsNull()
    {
        var form = Form();

        Assert.Null(TemplateFormConverter.Convert(form).Stp);
    }

    [Fact]
    public void Convert_StpInvalidVlanList_FallsBackToEmpty()
    {
        // Python wraps the whole comprehension in try/except ValueError: a
        // single bad element drops the whole list.
        var form = Form();
        form.Stp.RootPrimaryVlans = "10,not-a-number";
        form.Stp.PortfastDefault = true;

        var converted = TemplateFormConverter.Convert(form);
        Assert.NotNull(converted.Stp);
        var stp = converted.Stp!;

        Assert.Empty(stp.RootPrimaryVlans);
        Assert.True(stp.PortfastDefault);
    }

    // ----- Default mode mapping -----

    [Fact]
    public void Convert_StpUnknownMode_FallsBackToRapidPvst()
    {
        var form = Form();
        form.Stp.Mode = "totally-bogus";
        form.Stp.Priority = "8192";

        Assert.Equal(StpMode.RapidPvst, TemplateFormConverter.Convert(form).Stp!.Mode);
    }
}
