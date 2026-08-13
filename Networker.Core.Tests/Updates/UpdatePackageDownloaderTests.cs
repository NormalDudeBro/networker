using System.Net;
using System.Text;
using Networker.Core.Updates;

namespace Networker.Core.Tests.Updates;

public class UpdatePackageDownloaderTests : IDisposable
{
    private const string Tag = "v1.2.3";
    private static readonly string MsixName = "Networker-1.2.3-win-x64.msix";
    private static readonly string ChecksumName = MsixName + ".sha256";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "NetworkerTests", "downloader-" + Guid.NewGuid().ToString("N"));
    private readonly TestLog _log = new();

    public UpdatePackageDownloaderTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private static (UpdateRelease Release, SelectedUpdateAssets Assets) Build(byte[] msixBytes, string sidecar, long msixSize)
    {
        string msixUrl = NetworkerVersionPolicy.AssetDownloadUrl(UpdateTestData.V(Tag), MsixName);
        string checksumUrl = NetworkerVersionPolicy.AssetDownloadUrl(UpdateTestData.V(Tag), ChecksumName);
        var release = UpdateTestData.Release(Tag,
            new ReleaseAsset(MsixName, msixSize, msixUrl),
            new ReleaseAsset(ChecksumName, sidecar.Length, checksumUrl));
        return (release, new SelectedUpdateAssets(release, release.Assets[0], release.Assets[1]));
    }

    private static string SidecarFor(byte[] msix) => UpdateTestData.SidecarLine(UpdateTestData.Sha256Hex(msix), MsixName);

    private static HttpClient Client(
        string sidecar,
        byte[] msix,
        Func<string, HttpResponseMessage>? msixResponder = null,
        string? checksumStatusUrl = null)
    {
        return new HttpClient(new AsyncStubHttpMessageHandler((request, _) =>
        {
            string url = request.RequestUri!.AbsoluteUri;
            if (url.EndsWith(ChecksumName, StringComparison.OrdinalIgnoreCase))
            {
                return checksumStatusUrl is null
                    ? Task.FromResult(AsyncStubHttpMessageHandler.Bytes(Encoding.UTF8.GetBytes(sidecar), url))
                    : Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(msixResponder?.Invoke(url) ?? AsyncStubHttpMessageHandler.Bytes(msix, url));
        }));
    }

    [Fact]
    public async Task DownloadAsync_Success_VerifiesAndFinalizesAtomically()
    {
        byte[] msix = UpdateTestData.BuildMsix();
        string sidecar = SidecarFor(msix);
        (UpdateRelease release, SelectedUpdateAssets assets) = Build(msix, sidecar, msix.Length);
        var reported = new List<double>();

        var downloader = new UpdatePackageDownloader(Client(sidecar, msix), _log);
        DownloadedPackage result = await downloader.DownloadAsync(release, assets, _dir, new Progress<double>(reported.Add), CancellationToken.None);

        Assert.Equal(Path.Combine(_dir, MsixName), result.PackagePath);
        Assert.Equal(UpdateTestData.Sha256Hex(msix), result.ExpectedSha256Hex);
        Assert.Equal(sidecar, result.SidecarContent);
        Assert.True(File.Exists(result.PackagePath));
        Assert.False(File.Exists(result.PackagePath + ".partial"));
        Assert.Equal(msix, File.ReadAllBytes(result.PackagePath));
        Assert.NotEmpty(reported);
        Assert.Equal(1.0, reported[^1], 6);
    }

    [Fact]
    public async Task DownloadAsync_Indeterminate_WhenNoLengthAvailable()
    {
        byte[] msix = UpdateTestData.BuildMsix();
        string sidecar = SidecarFor(msix);
        (UpdateRelease release, SelectedUpdateAssets assets) = Build(msix, sidecar, msixSize: 0);
        var reported = new List<double>();

        var client = new HttpClient(new AsyncStubHttpMessageHandler((request, _) =>
        {
            string url = request.RequestUri!.AbsoluteUri;
            return url.EndsWith(ChecksumName, StringComparison.OrdinalIgnoreCase)
                ? Task.FromResult(AsyncStubHttpMessageHandler.Bytes(Encoding.UTF8.GetBytes(sidecar), url))
                : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new NonSeekableReadStream(msix)) });
        }));

        var downloader = new UpdatePackageDownloader(client, _log);
        DownloadedPackage result = await downloader.DownloadAsync(release, assets, _dir, new Progress<double>(reported.Add), CancellationToken.None);

        Assert.True(File.Exists(result.PackagePath));
        Assert.All(reported, value => Assert.True(value < 0, "indeterminate progress must be negative"));
    }

    [Fact]
    public async Task DownloadAsync_InvalidSidecar_Throws()
    {
        byte[] msix = UpdateTestData.BuildMsix();
        (UpdateRelease release, SelectedUpdateAssets assets) = Build(msix, "garbage", msix.Length);

        var downloader = new UpdatePackageDownloader(Client("garbage", msix), _log);
        var ex = await Assert.ThrowsAsync<UpdateException>(
            () => downloader.DownloadAsync(release, assets, _dir, null, CancellationToken.None));

        Assert.Contains("checksum file is invalid", ex.Message);
    }

    [Fact]
    public async Task DownloadAsync_SidecarTooLarge_Throws()
    {
        byte[] msix = UpdateTestData.BuildMsix();
        string huge = new string('x', 5000);
        (UpdateRelease release, SelectedUpdateAssets assets) = Build(msix, huge, msix.Length);

        var downloader = new UpdatePackageDownloader(Client(huge, msix), _log);
        var ex = await Assert.ThrowsAsync<UpdateException>(
            () => downloader.DownloadAsync(release, assets, _dir, null, CancellationToken.None));

        Assert.Contains("too large", ex.Message);
    }

    [Fact]
    public async Task DownloadAsync_SidecarHttpError_Throws()
    {
        byte[] msix = UpdateTestData.BuildMsix();
        string sidecar = SidecarFor(msix);
        (UpdateRelease release, SelectedUpdateAssets assets) = Build(msix, sidecar, msix.Length);

        var downloader = new UpdatePackageDownloader(Client(sidecar, msix, checksumStatusUrl: "notfound"), _log);
        var ex = await Assert.ThrowsAsync<UpdateException>(
            () => downloader.DownloadAsync(release, assets, _dir, null, CancellationToken.None));

        Assert.Contains("checksum download returned HTTP 404", ex.Message);
    }

    [Fact]
    public async Task DownloadAsync_FollowsAllowedRedirect()
    {
        byte[] msix = UpdateTestData.BuildMsix();
        string sidecar = SidecarFor(msix);
        string msixUrl = NetworkerVersionPolicy.AssetDownloadUrl(UpdateTestData.V(Tag), MsixName);
        string redirectTarget = "https://release-assets.githubusercontent.com/user/repo/msix";
        (UpdateRelease release, SelectedUpdateAssets assets) = Build(msix, sidecar, msix.Length);

        var client = new HttpClient(new AsyncStubHttpMessageHandler((request, _) =>
        {
            string url = request.RequestUri!.AbsoluteUri;
            if (url.EndsWith(ChecksumName, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(AsyncStubHttpMessageHandler.Bytes(Encoding.UTF8.GetBytes(sidecar), url));
            }

            return Task.FromResult(url == msixUrl
                ? AsyncStubHttpMessageHandler.Redirect(redirectTarget)
                : AsyncStubHttpMessageHandler.Bytes(msix, url));
        }));

        var downloader = new UpdatePackageDownloader(client, _log);
        DownloadedPackage result = await downloader.DownloadAsync(release, assets, _dir, null, CancellationToken.None);

        Assert.True(File.Exists(result.PackagePath));
        Assert.Equal(msix, File.ReadAllBytes(result.PackagePath));
    }

    [Fact]
    public async Task DownloadAsync_RejectsDisallowedRedirectHost()
    {
        byte[] msix = UpdateTestData.BuildMsix();
        string sidecar = SidecarFor(msix);
        (UpdateRelease release, SelectedUpdateAssets assets) = Build(msix, sidecar, msix.Length);

        var client = new HttpClient(new AsyncStubHttpMessageHandler((request, _) =>
        {
            string url = request.RequestUri!.AbsoluteUri;
            return url.EndsWith(ChecksumName, StringComparison.OrdinalIgnoreCase)
                ? Task.FromResult(AsyncStubHttpMessageHandler.Bytes(Encoding.UTF8.GetBytes(sidecar), url))
                : Task.FromResult(AsyncStubHttpMessageHandler.Redirect("https://evil.example.com/msix"));
        }));

        var downloader = new UpdatePackageDownloader(client, _log);
        var ex = await Assert.ThrowsAsync<UpdateException>(
            () => downloader.DownloadAsync(release, assets, _dir, null, CancellationToken.None));

        Assert.Contains("redirect target is not allowed", ex.Message);
    }

    [Fact]
    public async Task DownloadAsync_RejectsHttpDowngrade()
    {
        byte[] msix = UpdateTestData.BuildMsix();
        string sidecar = SidecarFor(msix);
        (UpdateRelease release, SelectedUpdateAssets assets) = Build(msix, sidecar, msix.Length);

        var client = new HttpClient(new AsyncStubHttpMessageHandler((request, _) =>
        {
            string url = request.RequestUri!.AbsoluteUri;
            return url.EndsWith(ChecksumName, StringComparison.OrdinalIgnoreCase)
                ? Task.FromResult(AsyncStubHttpMessageHandler.Bytes(Encoding.UTF8.GetBytes(sidecar), url))
                : Task.FromResult(AsyncStubHttpMessageHandler.Redirect("http://github.com/msix"));
        }));

        var downloader = new UpdatePackageDownloader(client, _log);
        var ex = await Assert.ThrowsAsync<UpdateException>(
            () => downloader.DownloadAsync(release, assets, _dir, null, CancellationToken.None));

        Assert.Contains("redirect target is not allowed", ex.Message);
    }

    [Fact]
    public async Task DownloadAsync_RejectsRedirectLoop()
    {
        byte[] msix = UpdateTestData.BuildMsix();
        string sidecar = SidecarFor(msix);
        string msixUrl = NetworkerVersionPolicy.AssetDownloadUrl(UpdateTestData.V(Tag), MsixName);
        (UpdateRelease release, SelectedUpdateAssets assets) = Build(msix, sidecar, msix.Length);

        var client = new HttpClient(new AsyncStubHttpMessageHandler((request, _) =>
        {
            string url = request.RequestUri!.AbsoluteUri;
            return url.EndsWith(ChecksumName, StringComparison.OrdinalIgnoreCase)
                ? Task.FromResult(AsyncStubHttpMessageHandler.Bytes(Encoding.UTF8.GetBytes(sidecar), url))
                : Task.FromResult(AsyncStubHttpMessageHandler.Redirect(msixUrl));
        }));

        var downloader = new UpdatePackageDownloader(client, _log);
        var ex = await Assert.ThrowsAsync<UpdateException>(
            () => downloader.DownloadAsync(release, assets, _dir, null, CancellationToken.None));

        Assert.Contains("too long", ex.Message);
    }

    [Fact]
    public async Task DownloadAsync_MsixHttpError_Throws()
    {
        byte[] msix = UpdateTestData.BuildMsix();
        string sidecar = SidecarFor(msix);
        (UpdateRelease release, SelectedUpdateAssets assets) = Build(msix, sidecar, msix.Length);

        var downloader = new UpdatePackageDownloader(Client(sidecar, msix, msixResponder: _ => new HttpResponseMessage(HttpStatusCode.InternalServerError)), _log);
        var ex = await Assert.ThrowsAsync<UpdateException>(
            () => downloader.DownloadAsync(release, assets, _dir, null, CancellationToken.None));

        Assert.Contains("HTTP 500", ex.Message);
    }

    [Fact]
    public async Task DownloadAsync_SizeMismatch_ThrowsAndCleansPartial()
    {
        byte[] msix = UpdateTestData.BuildMsix();
        string sidecar = SidecarFor(msix);
        // Declared size is far larger than the actual payload.
        (UpdateRelease release, SelectedUpdateAssets assets) = Build(msix, sidecar, 5_000_000);

        var downloader = new UpdatePackageDownloader(Client(sidecar, msix), _log);
        var ex = await Assert.ThrowsAsync<UpdateException>(
            () => downloader.DownloadAsync(release, assets, _dir, null, CancellationToken.None));

        Assert.Contains("does not match", ex.Message);
        Assert.False(File.Exists(Path.Combine(_dir, MsixName)));
        Assert.False(File.Exists(Path.Combine(_dir, MsixName + ".partial")));
    }

    [Fact]
    public async Task DownloadAsync_ChecksumMismatch_Throws()
    {
        byte[] msix = UpdateTestData.BuildMsix();
        string wrongDigest = new('a', 64);
        string sidecar = UpdateTestData.SidecarLine(wrongDigest, MsixName);
        (UpdateRelease release, SelectedUpdateAssets assets) = Build(msix, sidecar, msix.Length);

        var downloader = new UpdatePackageDownloader(Client(sidecar, msix), _log);
        var ex = await Assert.ThrowsAsync<UpdateException>(
            () => downloader.DownloadAsync(release, assets, _dir, null, CancellationToken.None));

        Assert.Contains("checksum verification failed", ex.Message);
        Assert.False(File.Exists(Path.Combine(_dir, MsixName)));
        Assert.False(File.Exists(Path.Combine(_dir, MsixName + ".partial")));
    }

    [Fact]
    public async Task DownloadAsync_StallTimeout_ThrowsAndCleansPartial()
    {
        byte[] msix = UpdateTestData.BuildMsix();
        string sidecar = SidecarFor(msix);
        (UpdateRelease release, SelectedUpdateAssets assets) = Build(msix, sidecar, msixSize: 1_000_000);

        var client = new HttpClient(new AsyncStubHttpMessageHandler((request, _) =>
        {
            string url = request.RequestUri!.AbsoluteUri;
            return url.EndsWith(ChecksumName, StringComparison.OrdinalIgnoreCase)
                ? Task.FromResult(AsyncStubHttpMessageHandler.Bytes(Encoding.UTF8.GetBytes(sidecar), url))
                : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new SlowStream(msix)) });
        }));

        var downloader = new UpdatePackageDownloader(client, _log, stallTimeout: TimeSpan.FromMilliseconds(200));
        var ex = await Assert.ThrowsAsync<UpdateException>(
            () => downloader.DownloadAsync(release, assets, _dir, null, CancellationToken.None));

        Assert.Contains("timed out", ex.Message);
        Assert.False(File.Exists(Path.Combine(_dir, MsixName)));
        Assert.False(File.Exists(Path.Combine(_dir, MsixName + ".partial")));
    }

    [Fact]
    public async Task DownloadAsync_OverallTimeout_Throws()
    {
        byte[] msix = UpdateTestData.BuildMsix();
        string sidecar = SidecarFor(msix);
        (UpdateRelease release, SelectedUpdateAssets assets) = Build(msix, sidecar, msixSize: 1_000_000);

        var client = new HttpClient(new AsyncStubHttpMessageHandler((request, _) =>
        {
            string url = request.RequestUri!.AbsoluteUri;
            return url.EndsWith(ChecksumName, StringComparison.OrdinalIgnoreCase)
                ? Task.FromResult(AsyncStubHttpMessageHandler.Bytes(Encoding.UTF8.GetBytes(sidecar), url))
                : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new SlowStream(msix)) });
        }));

        var downloader = new UpdatePackageDownloader(client, _log, overallTimeout: TimeSpan.FromMilliseconds(300));
        var ex = await Assert.ThrowsAsync<UpdateException>(
            () => downloader.DownloadAsync(release, assets, _dir, null, CancellationToken.None));

        Assert.Contains("timed out", ex.Message);
    }
}
