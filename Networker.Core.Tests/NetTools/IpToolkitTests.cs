using System.Numerics;
using Networker.Core.NetTools.Ip;

namespace Networker.Core.Tests.NetTools;

public class IpToolkitTests
{
    [Fact]
    public void Calculate_Classful24_ReturnsCorrectDetails()
    {
        var info = IpToolkit.Calculate("192.168.10.0/24");

        Assert.Equal("192.168.10.0", info.NetworkAddress);
        Assert.Equal("255.255.255.0", info.Netmask);
        Assert.Equal("0.0.0.255", info.WildcardMask);
        Assert.Equal("192.168.10.1", info.FirstUsable);
        Assert.Equal("192.168.10.254", info.LastUsable);
        Assert.Equal("192.168.10.255", info.BroadcastAddress);
        Assert.Equal(256, info.TotalHosts);
        Assert.Equal(254, info.UsableHosts);
        Assert.Equal(4, info.IpVersion);
        Assert.True(info.IsPrivate);
        Assert.False(info.IsPointToPoint);
        Assert.False(info.IsSingleHost);
    }

    [Fact]
    public void Calculate_TruncatesHostBits()
    {
        var info = IpToolkit.Calculate("10.1.2.200/16");
        Assert.Equal("10.1.0.0", info.NetworkAddress);
        Assert.Equal("10.1.255.255", info.BroadcastAddress);
        Assert.Equal("10.1.0.1", info.FirstUsable);
        Assert.Equal("10.1.255.254", info.LastUsable);
    }

    [Fact]
    public void Calculate_Slash31_PointToPoint()
    {
        var info = IpToolkit.Calculate("10.0.0.0/31");
        Assert.True(info.IsPointToPoint);
        Assert.Equal(2, info.UsableHosts);
        Assert.Equal("10.0.0.0", info.FirstUsable);
        Assert.Equal("10.0.0.1", info.LastUsable);
    }

    [Fact]
    public void Calculate_Slash32_SingleHost()
    {
        var info = IpToolkit.Calculate("192.168.1.7/32");
        Assert.True(info.IsSingleHost);
        Assert.Equal(1, info.UsableHosts);
        Assert.Equal("192.168.1.7", info.FirstUsable);
        Assert.Equal("192.168.1.7", info.LastUsable);
    }

    [Fact]
    public void Calculate_PrefixZero_CoversEverything()
    {
        var info = IpToolkit.Calculate("0.0.0.0/0");
        Assert.Equal("0.0.0.0", info.NetworkAddress);
        Assert.Equal("255.255.255.255", info.BroadcastAddress);
        Assert.Equal(BigInteger.One << 32, info.TotalHosts);
    }

    [Fact]
    public void Calculate_PrivateDetection()
    {
        Assert.True(IpToolkit.Calculate("10.0.0.0/8").IsPrivate);
        Assert.True(IpToolkit.Calculate("172.16.0.0/12").IsPrivate);
        Assert.True(IpToolkit.Calculate("192.168.0.0/16").IsPrivate);
        Assert.False(IpToolkit.Calculate("8.8.8.0/24").IsPrivate);
        Assert.False(IpToolkit.Calculate("203.0.113.0/24").IsPrivate);
    }

    [Fact]
    public void Calculate_Ipv6_ReturnsCorrectDetails()
    {
        var info = IpToolkit.Calculate("2001:db8::/64");

        Assert.Equal(6, info.IpVersion);
        Assert.Equal("2001:db8::", info.NetworkAddress);
        Assert.Equal(BigInteger.One << 64, info.TotalHosts);
        Assert.Equal("2001:db8::1", info.FirstUsable);
        Assert.False(info.IsPointToPoint);
    }

    [Fact]
    public void Calculate_Ipv6_Slash128()
    {
        var info = IpToolkit.Calculate("2001:db8::1/128");
        Assert.True(info.IsSingleHost);
        Assert.Equal(1, info.UsableHosts);
        Assert.Equal("2001:db8::1", info.FirstUsable);
    }

    [Fact]
    public void Calculate_RejectsInvalidInput()
    {
        Assert.Throws<FormatException>(() => IpToolkit.Calculate("not-an-ip"));
        Assert.Throws<FormatException>(() => IpToolkit.Calculate("10.0.0.1/33"));
        Assert.Throws<FormatException>(() => IpToolkit.Calculate("2001:db8::1/129"));
        Assert.Throws<ArgumentException>(() => IpToolkit.Calculate(""));
    }

    [Fact]
    public void Contains_ChecksMembership()
    {
        Assert.True(IpToolkit.Contains("192.168.10.0/24", "192.168.10.200"));
        Assert.False(IpToolkit.Contains("192.168.10.0/24", "192.168.11.1"));
        Assert.True(IpToolkit.Contains("2001:db8::/64", "2001:db8::1"));
        Assert.False(IpToolkit.Contains("2001:db8::/64", "2001:db8:1::1"));
        Assert.False(IpToolkit.Contains("10.0.0.0/8", "not-an-ip"));
    }

    [Fact]
    public void Divide_SplitsBlockIntoSubnets()
    {
        var subnets = IpToolkit.Divide("10.0.0.0/24", 26);
        Assert.Equal(4, subnets.Count);
        Assert.Equal("10.0.0.0/26", subnets[0]);
        Assert.Equal("10.0.0.64/26", subnets[1]);
        Assert.Equal("10.0.0.128/26", subnets[2]);
        Assert.Equal("10.0.0.192/26", subnets[3]);
    }

    [Fact]
    public void Divide_Ipv6_SplitsBlock()
    {
        var subnets = IpToolkit.Divide("2001:db8::/64", 66);
        Assert.Equal(4, subnets.Count);
        Assert.Equal("2001:db8::/66", subnets[0]);
        Assert.Equal("2001:db8:0:0:4000::/66", subnets[1]);
    }

    [Fact]
    public void Summarize_FindsSmallestContainer()
    {
        var summary = IpToolkit.Summarize(new[] { "10.0.1.0/24", "10.0.2.0/24", "10.0.3.0/24" });
        Assert.Equal("10.0.0.0/22", summary);
    }

    [Fact]
    public void Summarize_SingleBlockReturnsItself()
    {
        Assert.Equal("192.168.5.0/24", IpToolkit.Summarize(new[] { "192.168.5.0/24" }));
    }
}

