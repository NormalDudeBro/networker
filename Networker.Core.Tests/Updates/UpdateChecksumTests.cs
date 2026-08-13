using Networker.Core.Updates;

namespace Networker.Core.Tests.Updates;

public class UpdateChecksumTests
{
    [Fact]
    public void TryParseSidecar_Accepts_ValidSidecar()
    {
        string hex = new('a', 64);
        Assert.True(UpdateChecksum.TryParseSidecar($"{hex}  Networker-1.2.3-win-x64.msix", "Networker-1.2.3-win-x64.msix", out string parsed));
        Assert.Equal(hex, parsed);
    }

    [Fact]
    public void TryParseSidecar_Accepts_CrlfAndSurroundingBlankLines()
    {
        string hex = new('b', 64);
        string content = "\r\n\r\n" + hex + "  Networker-1.2.3-win-x64.msix\r\n\r\n";
        Assert.True(UpdateChecksum.TryParseSidecar(content, "Networker-1.2.3-win-x64.msix", out string parsed));
        Assert.Equal(hex, parsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("garbage")]
    [InlineData("  \t  ")]
    public void TryParseSidecar_Rejects_EmptyOrGarbage(string? content)
    {
        Assert.False(UpdateChecksum.TryParseSidecar(content!, "Networker-1.2.3-win-x64.msix", out _));
    }

    [Fact]
    public void TryParseSidecar_Rejects_MoreThanOneLine()
    {
        string hex = new('a', 64);
        string content = hex + "  Networker-1.2.3-win-x64.msix\n" + hex + "  Networker-1.2.3-win-x64.msix";
        Assert.False(UpdateChecksum.TryParseSidecar(content, "Networker-1.2.3-win-x64.msix", out _));
    }

    [Fact]
    public void TryParseSidecar_Rejects_WrongFileName()
    {
        string hex = new('a', 64);
        Assert.False(UpdateChecksum.TryParseSidecar(hex + "  Networker-1.2.3-win-x86.msix", "Networker-1.2.3-win-x64.msix", out _));
    }

    [Fact]
    public void TryParseSidecar_Rejects_UpperCaseDigest()
    {
        Assert.False(UpdateChecksum.TryParseSidecar(new string('A', 64) + "  Networker-1.2.3-win-x64.msix", "Networker-1.2.3-win-x64.msix", out _));
    }

    [Fact]
    public void TryParseSidecar_Rejects_InvalidHexCharacters()
    {
        string hex = new('g', 64);
        Assert.False(UpdateChecksum.TryParseSidecar(hex + "  Networker-1.2.3-win-x64.msix", "Networker-1.2.3-win-x64.msix", out _));
    }

    [Fact]
    public void TryParseSidecar_Rejects_TooShortDigest()
    {
        string hex = new('a', 63);
        Assert.False(UpdateChecksum.TryParseSidecar(hex + "  Networker-1.2.3-win-x64.msix", "Networker-1.2.3-win-x64.msix", out _));
    }

    [Theory]
    [InlineData("a")]      // single space
    [InlineData("   ")]    // three spaces (digest split differs)
    [InlineData("\t")]     // tab separator
    public void TryParseSidecar_Rejects_WrongSeparator(string separator)
    {
        string hex = new('a', 64);
        Assert.False(UpdateChecksum.TryParseSidecar(hex + separator + "Networker-1.2.3-win-x64.msix", "Networker-1.2.3-win-x64.msix", out _));
    }

    [Fact]
    public void DigestMatches_True_ForMatchingHash()
    {
        byte[] data = { 1, 2, 3, 4, 5 };
        string hex = UpdateTestData.Sha256Hex(data);
        Assert.True(UpdateChecksum.DigestMatches(hex, System.Security.Cryptography.SHA256.HashData(data)));
    }

    [Fact]
    public void DigestMatches_False_ForDifferentHash()
    {
        byte[] data = { 1, 2, 3, 4, 5 };
        string hex = UpdateTestData.Sha256Hex(data);
        byte[] other = System.Security.Cryptography.SHA256.HashData(new byte[] { 9, 9, 9 });
        Assert.False(UpdateChecksum.DigestMatches(hex, other));
    }

    [Fact]
    public void DigestMatches_False_ForWrongLengths()
    {
        Assert.False(UpdateChecksum.DigestMatches(new string('a', 63), new byte[32]));
        Assert.False(UpdateChecksum.DigestMatches(new string('a', 64), new byte[31]));
    }

    [Fact]
    public void DigestMatches_False_ForMalformedHex()
    {
        Assert.False(UpdateChecksum.DigestMatches(new string('z', 64), new byte[32]));
    }

    [Fact]
    public void FormatExpectedDigest_PrefixesSha256()
    {
        Assert.Equal("sha256:" + new string('a', 64), UpdateChecksum.FormatExpectedDigest(new string('a', 64)));
    }
}
