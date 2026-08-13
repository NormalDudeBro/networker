using Networker.Core.Updates;

namespace Networker.Core.Tests.Updates;

public class UpdatePackageVerifierTests : IDisposable
{
    private static readonly InstalledVersion Installed = UpdateTestFakes.Packaged("v0.9.0", "0.9.0.65535");
    private static readonly string MsixName = "Networker-1.0.0-win-x64.msix";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "NetworkerTests", "verifier-" + Guid.NewGuid().ToString("N"));
    private readonly TestLog _log = new();

    public UpdatePackageVerifierTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private DownloadedPackage WritePackage(byte[] msix, string? expectedHash = null, string? sidecarOverride = null)
    {
        string path = Path.Combine(_dir, MsixName);
        File.WriteAllBytes(path, msix);
        string digest = expectedHash ?? UpdateTestData.Sha256Hex(msix);
        string sidecar = sidecarOverride ?? UpdateTestData.SidecarLine(digest, Path.GetFileName(path));
        return new DownloadedPackage(path, digest, sidecar);
    }

    private static UpdateRelease Release(string tag = "v1.0.0")
        => UpdateTestData.Release(tag, UpdateTestData.Asset(tag, "Networker-1.0.0-win-x64.msix"));

    [Fact]
    public async Task VerifyAsync_Success_ReturnsExpectedDigest()
    {
        byte[] msix = UpdateTestData.BuildMsix();
        DownloadedPackage downloaded = WritePackage(msix);
        var verifier = new UpdatePackageVerifier(_log);

        VerifiedPackage verified = await verifier.VerifyAsync(Release(), downloaded, Installed, CancellationToken.None);

        Assert.Equal(Path.Combine(_dir, MsixName), verified.PackagePath);
        Assert.Equal("sha256:" + UpdateTestData.Sha256Hex(msix), verified.ExpectedDigest);
    }

    [Fact]
    public async Task VerifyAsync_InvalidSidecar_Throws()
    {
        DownloadedPackage downloaded = WritePackage(UpdateTestData.BuildMsix(), sidecarOverride: "garbage");
        var verifier = new UpdatePackageVerifier(_log);

        var ex = await Assert.ThrowsAsync<UpdateException>(
            () => verifier.VerifyAsync(Release(), downloaded, Installed, CancellationToken.None));

        Assert.Contains("checksum file is invalid", ex.Message);
    }

    [Fact]
    public async Task VerifyAsync_InconsistentSidecar_Throws()
    {
        byte[] msix = UpdateTestData.BuildMsix();
        string otherDigest = new('b', 64);
        // Sidecar parses, but names a different digest than ExpectedSha256Hex.
        DownloadedPackage downloaded = WritePackage(msix, expectedHash: otherDigest, sidecarOverride: UpdateTestData.SidecarLine(new('c', 64), MsixName));
        var verifier = new UpdatePackageVerifier(_log);

        var ex = await Assert.ThrowsAsync<UpdateException>(
            () => verifier.VerifyAsync(Release(), downloaded, Installed, CancellationToken.None));

        Assert.Contains("inconsistent", ex.Message);
    }

    [Fact]
    public async Task VerifyAsync_FileHashMismatch_Throws()
    {
        byte[] msix = UpdateTestData.BuildMsix();
        string wrongDigest = new('d', 64);
        DownloadedPackage downloaded = WritePackage(msix, expectedHash: wrongDigest, sidecarOverride: UpdateTestData.SidecarLine(wrongDigest, MsixName));
        var verifier = new UpdatePackageVerifier(_log);

        var ex = await Assert.ThrowsAsync<UpdateException>(
            () => verifier.VerifyAsync(Release(), downloaded, Installed, CancellationToken.None));

        Assert.Contains("checksum verification failed", ex.Message);
    }

    [Fact]
    public async Task VerifyAsync_WrongArchitecture_Throws()
    {
        DownloadedPackage downloaded = WritePackage(UpdateTestData.BuildMsix(architecture: "x86"));
        var verifier = new UpdatePackageVerifier(_log);

        var ex = await Assert.ThrowsAsync<UpdateException>(
            () => verifier.VerifyAsync(Release(), downloaded, Installed, CancellationToken.None));

        Assert.Contains("not built for x64", ex.Message);
    }

    [Fact]
    public async Task VerifyAsync_WrongPackageName_Throws()
    {
        DownloadedPackage downloaded = WritePackage(UpdateTestData.BuildMsix(name: "OtherApp"));
        var verifier = new UpdatePackageVerifier(_log);

        var ex = await Assert.ThrowsAsync<UpdateException>(
            () => verifier.VerifyAsync(Release(), downloaded, Installed, CancellationToken.None));

        Assert.Contains("identity does not match", ex.Message);
    }

    [Fact]
    public async Task VerifyAsync_WrongPublisher_Throws()
    {
        DownloadedPackage downloaded = WritePackage(UpdateTestData.BuildMsix(publisher: "CN=SomeoneElse"));
        var verifier = new UpdatePackageVerifier(_log);

        var ex = await Assert.ThrowsAsync<UpdateException>(
            () => verifier.VerifyAsync(Release(), downloaded, Installed, CancellationToken.None));

        Assert.Contains("publisher does not match", ex.Message);
    }

    [Fact]
    public async Task VerifyAsync_WrongManifestVersion_Throws()
    {
        DownloadedPackage downloaded = WritePackage(UpdateTestData.BuildMsix(version: "1.0.0.1"));
        var verifier = new UpdatePackageVerifier(_log);

        var ex = await Assert.ThrowsAsync<UpdateException>(
            () => verifier.VerifyAsync(Release(), downloaded, Installed, CancellationToken.None));

        Assert.Contains("version does not match", ex.Message);
    }

    [Fact]
    public async Task VerifyAsync_NotNewerThanInstalled_Throws()
    {
        // Installed v1.2.3, release v1.0.0.
        DownloadedPackage downloaded = WritePackage(UpdateTestData.BuildMsix());
        var verifier = new UpdatePackageVerifier(_log);
        var installedNewer = UpdateTestFakes.Packaged("v1.2.3", "1.2.3.65535");

        var ex = await Assert.ThrowsAsync<UpdateException>(
            () => verifier.VerifyAsync(Release("v1.0.0"), downloaded, installedNewer, CancellationToken.None));

        Assert.Contains("not newer than the installed version", ex.Message);
    }

    [Fact]
    public async Task VerifyAsync_NotNewerThanInstalledPackage_Throws()
    {
        // SemVer installed is older, but the installed *package* version already matches the target.
        var installedPackageNewer = UpdateTestFakes.Packaged("v1.0.0", "1.1.0.65535");
        DownloadedPackage downloaded = WritePackage(UpdateTestData.BuildMsix(version: "1.1.0.65535"));
        var verifier = new UpdatePackageVerifier(_log);

        var ex = await Assert.ThrowsAsync<UpdateException>(
            () => verifier.VerifyAsync(Release("v1.1.0"), downloaded, installedPackageNewer, CancellationToken.None));

        Assert.Contains("not newer than the installed package", ex.Message);
    }

    [Fact]
    public async Task VerifyAsync_MissingManifest_Throws()
    {
        DownloadedPackage downloaded = WritePackage(UpdateTestData.BuildMsixWithEntry("Other.xml", "<x/>"));
        var verifier = new UpdatePackageVerifier(_log);

        var ex = await Assert.ThrowsAsync<UpdateException>(
            () => verifier.VerifyAsync(Release(), downloaded, Installed, CancellationToken.None));

        Assert.Contains("no root manifest", ex.Message);
    }

    [Fact]
    public async Task VerifyAsync_DuplicateManifest_Throws()
    {
        DownloadedPackage downloaded = WritePackage(UpdateTestData.BuildMsix(duplicateManifest: true));
        var verifier = new UpdatePackageVerifier(_log);

        var ex = await Assert.ThrowsAsync<UpdateException>(
            () => verifier.VerifyAsync(Release(), downloaded, Installed, CancellationToken.None));

        Assert.Contains("more than one root manifest", ex.Message);
    }

    [Fact]
    public async Task VerifyAsync_MalformedManifest_Throws()
    {
        DownloadedPackage downloaded = WritePackage(UpdateTestData.BuildMsix(manifestOverride: "<Package><Identity Name=\"Networker\""));
        var verifier = new UpdatePackageVerifier(_log);

        var ex = await Assert.ThrowsAsync<UpdateException>(
            () => verifier.VerifyAsync(Release(), downloaded, Installed, CancellationToken.None));

        Assert.Contains("malformed", ex.Message);
    }

    [Fact]
    public async Task VerifyAsync_MissingIdentity_Throws()
    {
        DownloadedPackage downloaded = WritePackage(UpdateTestData.BuildMsix(manifestOverride: "<Package></Package>"));
        var verifier = new UpdatePackageVerifier(_log);

        var ex = await Assert.ThrowsAsync<UpdateException>(
            () => verifier.VerifyAsync(Release(), downloaded, Installed, CancellationToken.None));

        Assert.Contains("missing identity information", ex.Message);
    }

    [Fact]
    public async Task VerifyAsync_MultipleIdentityElements_Throws()
    {
        string manifest = "<Package>"
            + "<Identity Name=\"Networker\" Publisher=\"CN=Kenny\" Version=\"1.0.0.65535\" ProcessorArchitecture=\"x64\"/>"
            + "<Identity Name=\"Networker\" Publisher=\"CN=Kenny\" Version=\"1.0.0.65535\" ProcessorArchitecture=\"x64\"/>"
            + "</Package>";
        DownloadedPackage downloaded = WritePackage(UpdateTestData.BuildMsix(manifestOverride: manifest));
        var verifier = new UpdatePackageVerifier(_log);

        var ex = await Assert.ThrowsAsync<UpdateException>(
            () => verifier.VerifyAsync(Release(), downloaded, Installed, CancellationToken.None));

        Assert.Contains("multiple identity elements", ex.Message);
    }

    [Fact]
    public async Task VerifyAsync_CancelledToken_Throws()
    {
        DownloadedPackage downloaded = WritePackage(UpdateTestData.BuildMsix());
        var verifier = new UpdatePackageVerifier(_log);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => verifier.VerifyAsync(Release(), downloaded, Installed, cts.Token));
    }
}
