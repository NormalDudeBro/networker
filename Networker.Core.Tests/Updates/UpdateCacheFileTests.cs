using Networker.Core.Updates;

namespace Networker.Core.Tests.Updates;

public class UpdateCacheFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "NetworkerTests", "cache-" + Guid.NewGuid().ToString("N"));
    private readonly string _path;

    public UpdateCacheFileTests()
    {
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "cache.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void Read_WhenFileMissing_ReturnsEmpty()
    {
        CacheFileData data = new UpdateCacheFile(_path).Read();
        Assert.Equal(CacheFileData.Empty, data);
    }

    [Fact]
    public void WriteThenRead_RoundTrips()
    {
        UpdateRelease release = UpdateTestData.Release("v1.2.3",
            UpdateTestData.Asset("v1.2.3", "Networker-1.2.3-win-x64.msix"),
            UpdateTestData.Asset("v1.2.3", "Networker-1.2.3-win-x64.msix.sha256", size: 66));

        var file = new UpdateCacheFile(_path);
        file.Write(new CacheFileData(
            "stable-etag",
            "preview-etag",
            UpdateReleaseCache.FromRelease(release),
            null,
            "v1.2.3"));

        CacheFileData read = file.Read();

        Assert.Equal("stable-etag", read.StableEtag);
        Assert.Equal("preview-etag", read.PreviewEtag);
        Assert.Null(read.PreviewRelease);
        Assert.Equal("v1.2.3", read.DismissedTag);
        Assert.NotNull(read.StableRelease);
        Assert.Equal("v1.2.3", read.StableRelease!.TagName);
        Assert.Equal("Networker-1.2.3-win-x64.msix", read.StableRelease.Assets[0].Name);
        Assert.Equal("v1.2.3", read.StableRelease!.ToUpdateRelease().TagName);
    }

    [Fact]
    public void WriteThenRead_NullPayload_RoundTrips()
    {
        var file = new UpdateCacheFile(_path);
        file.Write(CacheFileData.Empty);

        CacheFileData read = file.Read();

        Assert.Equal(CacheFileData.Empty, read);
    }

    [Fact]
    public void Read_CorruptJson_ReturnsEmpty()
    {
        File.WriteAllText(_path, "{ not valid json ");

        Assert.Equal(CacheFileData.Empty, new UpdateCacheFile(_path).Read());
    }

    [Fact]
    public void Read_EmptyFile_ReturnsEmpty()
    {
        File.WriteAllText(_path, string.Empty);

        Assert.Equal(CacheFileData.Empty, new UpdateCacheFile(_path).Read());
    }

    [Fact]
    public void Write_CreatesMissingDirectory()
    {
        string nested = Path.Combine(_dir, "a", "b", "cache.json");
        new UpdateCacheFile(nested).Write(CacheFileData.Empty);

        Assert.True(File.Exists(nested));
    }

    [Fact]
    public void Write_LeavesNoTempFileBehind()
    {
        var file = new UpdateCacheFile(_path);
        file.Write(CacheFileData.Empty);
        file.Write(new CacheFileData("e", null, null, null, null));

        Assert.True(File.Exists(_path));
        Assert.False(File.Exists(_path + ".tmp"));
    }
}
