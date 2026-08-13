using Networker.Core.Updates;

namespace Networker.Core.Tests.Updates;

public class UpdateAssetSelectorTests
{
    private const string Tag = "v1.2.3";
    private static readonly string MsixName = "Networker-1.2.3-win-x64.msix";
    private static readonly string ChecksumName = MsixName + ".sha256";

    private static UpdateRelease ValidRelease(params ReleaseAsset[] extra)
    {
        var assets = new List<ReleaseAsset>
        {
            UpdateTestData.Asset(Tag, MsixName),
            UpdateTestData.Asset(Tag, ChecksumName, size: 66),
        };
        assets.AddRange(extra);
        return UpdateTestData.Release(Tag, assets.ToArray());
    }

    [Fact]
    public void Select_Returns_ContractAssets()
    {
        SelectedUpdateAssets selected = UpdateAssetSelector.Select(ValidRelease());
        Assert.Equal(MsixName, selected.MsixAsset.Name);
        Assert.Equal(ChecksumName, selected.ChecksumAsset.Name);
    }

    [Fact]
    public void Select_Ignores_UnrelatedAssets()
    {
        SelectedUpdateAssets selected = UpdateAssetSelector.Select(ValidRelease(
            UpdateTestData.Asset(Tag, "Networker-1.2.3-win-x64.zip"),
            UpdateTestData.Asset(Tag, "README.txt", size: 100)));
        Assert.Equal(MsixName, selected.MsixAsset.Name);
    }

    [Fact]
    public void Select_Throws_WhenMsixMissing()
    {
        UpdateRelease release = UpdateTestData.Release(Tag, UpdateTestData.Asset(Tag, ChecksumName, size: 66));
        var ex = Assert.Throws<UpdateException>(() => UpdateAssetSelector.Select(release));
        Assert.Contains("missing its x64 MSIX", ex.Message);
    }

    [Fact]
    public void Select_Throws_WhenChecksumMissing()
    {
        UpdateRelease release = UpdateTestData.Release(Tag, UpdateTestData.Asset(Tag, MsixName));
        var ex = Assert.Throws<UpdateException>(() => UpdateAssetSelector.Select(release));
        Assert.Contains("missing its checksum", ex.Message);
    }

    [Fact]
    public void Select_Throws_OnDuplicateMsix()
    {
        UpdateRelease release = UpdateTestData.Release(Tag,
            UpdateTestData.Asset(Tag, MsixName),
            UpdateTestData.Asset(Tag, MsixName),
            UpdateTestData.Asset(Tag, ChecksumName, size: 66));
        var ex = Assert.Throws<UpdateException>(() => UpdateAssetSelector.Select(release));
        Assert.Contains("duplicate MSIX", ex.Message);
    }

    [Fact]
    public void Select_Throws_OnDuplicateChecksum()
    {
        UpdateRelease release = UpdateTestData.Release(Tag,
            UpdateTestData.Asset(Tag, MsixName),
            UpdateTestData.Asset(Tag, ChecksumName, size: 66),
            UpdateTestData.Asset(Tag, ChecksumName, size: 66));
        var ex = Assert.Throws<UpdateException>(() => UpdateAssetSelector.Select(release));
        Assert.Contains("duplicate checksum", ex.Message);
    }

    [Fact]
    public void Select_Throws_WhenReleaseUrlInvalid()
    {
        var release = ValidRelease() with { HtmlUrl = "https://github.com/NormalDudeBro/networker/releases/tag/other" };
        var ex = Assert.Throws<UpdateException>(() => UpdateAssetSelector.Select(release));
        Assert.Contains("release URL", ex.Message);
    }

    [Fact]
    public void Select_Throws_WhenAssetUrlInvalid()
    {
        // A source archive URL (not under the release download path).
        var badAsset = new ReleaseAsset(MsixName, 5_000_000,
            "https://github.com/NormalDudeBro/networker/archive/refs/tags/v1.2.3.zip");
        UpdateRelease release = UpdateTestData.Release(Tag, badAsset, UpdateTestData.Asset(Tag, ChecksumName, size: 66));
        var ex = Assert.Throws<UpdateException>(() => UpdateAssetSelector.Select(release));
        Assert.Contains("URL failed validation", ex.Message);
    }

    [Fact]
    public void Select_Throws_WhenAssetUrlHttp()
    {
        var badAsset = new ReleaseAsset(MsixName, 5_000_000,
            $"http://github.com/NormalDudeBro/networker/releases/download/{Tag}/{MsixName}");
        UpdateRelease release = UpdateTestData.Release(Tag, badAsset, UpdateTestData.Asset(Tag, ChecksumName, size: 66));
        Assert.Throws<UpdateException>(() => UpdateAssetSelector.Select(release));
    }

    [Fact]
    public void Select_Throws_WhenMsixTooSmall()
    {
        UpdateRelease release = UpdateTestData.Release(Tag,
            UpdateTestData.Asset(Tag, MsixName, size: 999_999),
            UpdateTestData.Asset(Tag, ChecksumName, size: 66));
        var ex = Assert.Throws<UpdateException>(() => UpdateAssetSelector.Select(release));
        Assert.Contains("implausible", ex.Message);
    }

    [Fact]
    public void Select_Throws_WhenMsixTooLarge()
    {
        UpdateRelease release = UpdateTestData.Release(Tag,
            UpdateTestData.Asset(Tag, MsixName, size: 1024L * 1024 * 1024 + 1),
            UpdateTestData.Asset(Tag, ChecksumName, size: 66));
        var ex = Assert.Throws<UpdateException>(() => UpdateAssetSelector.Select(release));
        Assert.Contains("implausible", ex.Message);
    }

    [Fact]
    public void Select_Throws_WhenChecksumTooLarge()
    {
        UpdateRelease release = UpdateTestData.Release(Tag,
            UpdateTestData.Asset(Tag, MsixName),
            UpdateTestData.Asset(Tag, ChecksumName, size: 4097));
        var ex = Assert.Throws<UpdateException>(() => UpdateAssetSelector.Select(release));
        Assert.Contains("implausible", ex.Message);
    }
}
