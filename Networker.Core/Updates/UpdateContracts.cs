namespace Networker.Core.Updates;

/// <summary>
/// Contract for discovering the latest eligible GitHub release for a channel.
/// Implementations are pure HTTP and must never touch application data.
/// </summary>
public interface IGitHubReleaseClient
{
    /// <summary>
    /// Queries the GitHub Releases API for the channel. When <paramref name="etag"/>
    /// matches the server state the call may return a <see cref="ReleaseCheckResult"/>
    /// with <see cref="ReleaseCheckResult.NotModified"/> set and no release payload.
    /// </summary>
    Task<ReleaseCheckResult> CheckAsync(UpdateChannel channel, string? etag, CancellationToken cancellationToken);
}

/// <summary>
/// Contract for acquiring the checksum sidecar and the MSIX, verifying the
/// streamed digest, and finalizing an atomically renamed package file.
/// </summary>
public interface IUpdatePackageDownloader
{
    /// <summary>
    /// Downloads the checksum sidecar first, then the MSIX into
    /// <paramref name="destinationDirectory"/> as "<c>name.partial</c>" and
    /// atomically renames it to "<c>name</c>" only after the digest and byte
    /// count match. <paramref name="progress"/> receives 0..1 for determinate
    /// downloads or a negative value while indeterminate.
    /// </summary>
    Task<DownloadedPackage> DownloadAsync(
        UpdateRelease release,
        SelectedUpdateAssets assets,
        string destinationDirectory,
        IProgress<double>? progress,
        CancellationToken cancellationToken);
}

/// <summary>
/// Contract for package-level verification: checksum sidecar revalidation,
/// constant-time digest comparison, and bounded root <c>AppxManifest.xml</c>
/// inspection (identity, publisher, architecture, version).
/// </summary>
public interface IUpdatePackageVerifier
{
    Task<VerifiedPackage> VerifyAsync(
        UpdateRelease release,
        DownloadedPackage downloaded,
        InstalledVersion installed,
        CancellationToken cancellationToken);
}

/// <summary>
/// Contract for handing a verified package to Windows package deployment.
/// </summary>
public interface IUpdateInstaller
{
    Task<UpdateInstallResult> InstallAsync(VerifiedPackage package, CancellationToken cancellationToken);
}

/// <summary>
/// Contract for the platform's notion of the installed Networker version.
/// </summary>
public interface IInstalledVersionProvider
{
    InstalledVersion GetInstalledVersion();
}

/// <summary>
/// Contract for atomically persisted update metadata (ETags, sanitized release
/// cache, dismissed-tag marker). The platform layer owns the backing store.
/// </summary>
public interface IUpdateCacheStore
{
    string? GetETag(UpdateChannel channel);
    void SetETag(UpdateChannel channel, string? etag);
    UpdateRelease? GetCachedRelease(UpdateChannel channel);
    void SetCachedRelease(UpdateChannel channel, UpdateRelease? release);
    string? GetDismissedUpdateTag();
    void SetDismissedUpdateTag(string? tag);
}

/// <summary>
/// Contract for the package staging directory under the platform's temporary
/// data folder. All operations are confined below the update root; the platform
/// layer is responsible for enforcing that confinement.
/// </summary>
public interface IUpdatePackageStorage
{
    /// <summary>The directory a release's files download into and are staged from.</summary>
    string GetDownloadDirectoryPath(string tag);

    /// <summary>Keeps a successfully staged package until the next launch confirms the new version.</summary>
    void PreserveStaged(string tag);

    /// <summary>Tags that were staged and not yet confirmed removed.</summary>
    IReadOnlyList<string> GetStagedTags();

    /// <summary>Removes a staged package after the target version is confirmed running.</summary>
    void RemoveStaged(string tag);

    /// <summary>Best-effort cleanup of partial/invalid files for a tag.</summary>
    void Cleanup(string tag);

    /// <summary>Best-effort cleanup of all update staging data.</summary>
    void CleanupAll();
}

/// <summary>
/// Contract for bounded update diagnostics. Implementations must tolerate all
/// I/O errors and never log secrets, response bodies, or signed URLs.
/// </summary>
public interface IUpdateLog
{
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? exception = null);
    void Debug(string message);
}

/// <summary>
/// Time abstraction used by scheduler/coordinator tests.
/// </summary>
public interface IUpdateClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>Default clock backed by the system clock.</summary>
public sealed class SystemUpdateClock : IUpdateClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>
/// An update-domain failure with an optional rate-limit retry window.
/// </summary>
public sealed class UpdateException : Exception
{
    public DateTimeOffset? RetryAfterUtc { get; }

    public UpdateException(string message, DateTimeOffset? retryAfterUtc = null, Exception? innerException = null)
        : base(message, innerException)
    {
        RetryAfterUtc = retryAfterUtc;
    }
}
