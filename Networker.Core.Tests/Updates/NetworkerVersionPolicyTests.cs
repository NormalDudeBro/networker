using Networker.Core.Updates;
using NuGet.Versioning;

namespace Networker.Core.Tests.Updates;

public class NetworkerVersionPolicyTests
{
    [Theory]
    [InlineData("v1.2.3")]
    [InlineData("v0.0.0")]
    [InlineData("v10.20.30")]
    [InlineData("v65535.65535.65535")]
    public void TryParseTag_Accepts_ValidStable(string tag)
    {
        Assert.True(NetworkerVersionPolicy.TryParseTag(tag, out NuGetVersion version));
        Assert.True(NetworkerVersionPolicy.IsStable(version));
        Assert.Equal(tag[1..], version.ToNormalizedString());
    }

    [Theory]
    [InlineData("v1.2.3-preview.1")]
    [InlineData("v1.2.3-preview.65534")]
    public void TryParseTag_Accepts_ValidPreview(string tag)
    {
        Assert.True(NetworkerVersionPolicy.TryParseTag(tag, out NuGetVersion version));
        Assert.True(NetworkerVersionPolicy.IsPreview(version));
        Assert.False(NetworkerVersionPolicy.IsStable(version));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1.2.3")]
    [InlineData("v")]
    [InlineData("v1")]
    [InlineData("v1.2")]
    [InlineData("v1.2.3.4")]
    [InlineData("v01.2.3")]
    [InlineData("v1.02.3")]
    [InlineData("v1.2.03")]
    [InlineData("v1.2.3-alpha.1")]
    [InlineData("v1.2.3-beta")]
    [InlineData("v1.2.3-preview")]
    [InlineData("v1.2.3-preview.0")]
    [InlineData("v1.2.3-preview.65535")]
    [InlineData("v1.2.3-preview.4.5")]
    [InlineData("v1.2.3-preview.04")]
    [InlineData("v1.2.3+meta")]
    [InlineData("v1.2.3-preview.4+build")]
    [InlineData("v65536.0.0")]
    [InlineData("v0.0.65536")]
    [InlineData("v999999999999999999.0.0")]
    public void TryParseTag_Rejects_Invalid(string? tag)
    {
        Assert.False(NetworkerVersionPolicy.TryParseTag(tag, out _));
    }

    [Fact]
    public void ParseTag_Throws_ForInvalid()
    {
        Assert.Throws<ArgumentException>(() => NetworkerVersionPolicy.ParseTag("v1.2"));
    }

    [Theory]
    [InlineData("1.2.3")]
    [InlineData("1.2.3-preview.4")]
    [InlineData("0.0.1")]
    public void TryParseInformationalVersion_Accepts_ReleaseLabels(string informational)
    {
        Assert.True(NetworkerVersionPolicy.TryParseInformationalVersion(informational, out NuGetVersion version));
        Assert.Equal(informational, version.ToNormalizedString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1.0.0-dev")]
    [InlineData("1.2.3-alpha.1")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("v1.2.3")]
    [InlineData("1.2.3+meta")]
    public void TryParseInformationalVersion_Rejects_DeveloperOrInvalid(string? informational)
    {
        Assert.False(NetworkerVersionPolicy.TryParseInformationalVersion(informational, out _));
    }

    [Fact]
    public void Ordering_Preview2_NewerThan_Preview1()
    {
        Assert.True(UpdateTestData.V("v1.2.3-preview.2") > UpdateTestData.V("v1.2.3-preview.1"));
    }

    [Fact]
    public void Ordering_Final_NewerThan_EveryPreview()
    {
        Assert.True(UpdateTestData.V("v1.2.3") > UpdateTestData.V("v1.2.3-preview.65534"));
        Assert.True(UpdateTestData.V("v1.2.3") > UpdateTestData.V("v1.2.3-preview.1"));
    }

    [Fact]
    public void Ordering_NextPatch_NewerThan_PriorFinal()
    {
        Assert.True(UpdateTestData.V("v1.2.4") > UpdateTestData.V("v1.2.3"));
        Assert.True(UpdateTestData.V("v1.3.0") > UpdateTestData.V("v1.2.65535"));
    }

    [Fact]
    public void Ordering_EqualVersions_NotNewer()
    {
        NuGetVersion a = UpdateTestData.V("v1.2.3");
        NuGetVersion b = UpdateTestData.V("v1.2.3");
        Assert.Equal(0, a.CompareTo(b));
    }

    [Theory]
    [InlineData("v1.2.3", "1.2.3.65535")]
    [InlineData("v1.2.3-preview.4", "1.2.3.4")]
    [InlineData("v1.2.3-preview.1", "1.2.3.1")]
    [InlineData("v0.0.1", "0.0.1.65535")]
    public void ToMsixVersion_Maps(string tag, string expected)
    {
        Version mapped = NetworkerVersionPolicy.ToMsixVersion(UpdateTestData.V(tag));
        Assert.Equal(expected, mapped.ToString(4));
    }

    [Fact]
    public void ToMsixVersion_Throws_WhenComponentOverflow()
    {
        var version = NuGetVersion.Parse("70000.0.0");
        Assert.Throws<ArgumentException>(() => NetworkerVersionPolicy.ToMsixVersion(version));
    }

    [Fact]
    public void AssetNames_FollowContract()
    {
        NuGetVersion version = UpdateTestData.V("v1.2.3-preview.4");
        Assert.Equal("Networker-1.2.3-preview.4-win-x64.msix", NetworkerVersionPolicy.MsixAssetName(version));
        Assert.Equal("Networker-1.2.3-preview.4-win-x64.msix.sha256", NetworkerVersionPolicy.ChecksumAssetName(version));
    }

    [Fact]
    public void AssetNames_StableDropsLabel()
    {
        NuGetVersion version = UpdateTestData.V("v1.2.3");
        Assert.Equal("Networker-1.2.3-win-x64.msix", NetworkerVersionPolicy.MsixAssetName(version));
    }

    [Theory]
    [InlineData("Networker-1.2.3-win-x64.msix", "v1.2.3", true)]
    [InlineData("Networker-1.2.3-win-x64.msix.sha256", "v1.2.3", true)]
    [InlineData("Networker-1.2.3-win-x86.msix", "v1.2.3", false)]
    [InlineData("Networker-1.2.3-win-x64.zip", "v1.2.3", false)]
    [InlineData("Networker-1.2.4-win-x64.msix", "v1.2.3", false)]
    public void IsContractAssetName_Matches(string name, string tag, bool expected)
    {
        Assert.Equal(expected, NetworkerVersionPolicy.IsContractAssetName(name, UpdateTestData.V(tag)));
    }

    [Fact]
    public void IsAcceptable_Rejects_UnknownLabels()
    {
        Assert.True(NetworkerVersionPolicy.IsAcceptable(UpdateTestData.V("v1.2.3")));
        Assert.True(NetworkerVersionPolicy.IsAcceptable(UpdateTestData.V("v1.2.3-preview.1")));
        Assert.False(NetworkerVersionPolicy.IsAcceptable(NuGetVersion.Parse("1.2.3-alpha.1")));
    }
}
