using Networker.Update.Contracts.Versioning;

namespace Networker.Update.Tests.Versioning;

public sealed class NetworkerVersionPolicyTests
{
    [Theory]
    [InlineData("v0.0.0", "0.0.0")]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("v1.2.3-preview.4", "1.2.3-preview.4")]
    public void ParsesStrictTags(string tag, string expected)
    {
        Assert.True(NetworkerVersionPolicy.TryParseTag(tag, out var version));
        Assert.Equal(expected, version.ToNormalizedString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1.2.3")]
    [InlineData("v1.2")]
    [InlineData("v01.2.3")]
    [InlineData("v1.2.3-alpha.1")]
    [InlineData("v1.2.3-preview.0")]
    [InlineData("v1.2.3+build")]
    public void RejectsInvalidTags(string? tag) => Assert.False(NetworkerVersionPolicy.TryParseTag(tag, out _));

    [Fact]
    public void StableOrdersAfterPreview()
    {
        Assert.True(NetworkerVersionPolicy.ParseTag("v1.2.3") > NetworkerVersionPolicy.ParseTag("v1.2.3-preview.99"));
    }
}
