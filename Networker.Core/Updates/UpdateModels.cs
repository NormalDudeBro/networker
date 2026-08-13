using NuGet.Versioning;

namespace Networker.Core.Updates;

/// <summary>
/// The update channel a check runs against. Stable is the default; Preview is
/// an explicit opt-in that also considers stable releases and picks the highest
/// eligible semantic version.
/// </summary>
public enum UpdateChannel
{
    Stable = 0,
    Preview = 1,
}

/// <summary>
/// The states of the update state machine as surfaced by
/// <see cref="UpdateCoordinator.Snapshot"/>. Not every transition is legal;
/// the coordinator enforces the legal graph.
/// </summary>
public enum UpdateStatus
{
    Disabled = 0,
    Idle = 1,
    Checking = 2,
    UpToDate = 3,
    Available = 4,
    Downloading = 5,
    Verifying = 6,
    Installing = 7,
    RestartRequired = 8,
    Cancelled = 9,
    Failed = 10,
}

/// <summary>
/// The version identity of the currently running Networker install, provided by
/// the platform layer. <see cref="SemanticVersion"/> is null for non-release
/// developer builds; <see cref="CanInstallUpdates"/> is false whenever the
/// install does not carry a trustworthy packaged identity that Windows can
/// update in place.
/// </summary>
public sealed record InstalledVersion(
    NuGetVersion? SemanticVersion,
    string DisplayVersion,
    bool IsPackaged,
    string? PackageName,
    string? PackageFamilyName,
    string? PackageFullName,
    string? Publisher,
    string? PackageVersion,
    string? Architecture,
    bool CanInstallUpdates);

/// <summary>
/// A single downloadable asset attached to a GitHub release.
/// </summary>
public sealed record ReleaseAsset(
    string Name,
    long Size,
    string BrowserDownloadUrl);

/// <summary>
/// A validated, versioned GitHub release. Version fields are normalized with
/// <see cref="NetworkerVersionPolicy"/>. The asset list may contain unrelated
/// assets; selection happens in <see cref="UpdateAssetSelector"/>.
/// </summary>
public sealed record UpdateRelease(
    NuGetVersion Version,
    string TagName,
    string? Name,
    string? Body,
    DateTimeOffset PublishedAt,
    string HtmlUrl,
    bool IsPrerelease,
    IReadOnlyList<ReleaseAsset> Assets);

/// <summary>
/// The exact updater assets selected for a release after contract validation.
/// </summary>
public sealed record SelectedUpdateAssets(
    UpdateRelease Release,
    ReleaseAsset MsixAsset,
    ReleaseAsset ChecksumAsset);

/// <summary>
/// The result of a download: the finalized MSIX whose contents matched the
/// checksum sidecar, plus the sidecar text for later re-validation.
/// </summary>
public sealed record DownloadedPackage(
    string PackagePath,
    string ExpectedSha256Hex,
    string SidecarContent);

/// <summary>
/// A fully verified package ready for Windows deployment. <see cref="ExpectedDigest"/>
/// is in the "<c>sha256:&lt;hex&gt;</c>" form accepted by
/// <c>AddPackageOptions.ExpectedDigests</c>.
/// </summary>
public sealed record VerifiedPackage(
    string PackagePath,
    string ExpectedDigest);

/// <summary>
/// The outcome of a Windows package deployment call.
/// </summary>
public sealed record UpdateInstallResult(
    bool Succeeded,
    string? ErrorMessage,
    string? ErrorCode,
    Guid? ActivityId);

/// <summary>
/// A concise, non-technical error for the update UI.
/// </summary>
public sealed record UpdateErrorInfo(string Message);

/// <summary>
/// An immutable snapshot of the update state machine. UI layers bind to this
/// and receive updates through <see cref="UpdateCoordinator.StateChanged"/>.
/// </summary>
public sealed record UpdateSnapshot(
    UpdateStatus Status,
    UpdateChannel Channel,
    InstalledVersion Installed,
    UpdateRelease? AvailableRelease,
    double Progress,
    UpdateErrorInfo? Error);

/// <summary>
/// The outcome of <see cref="UpdateCoordinator.CheckAsync"/> for persistence and
/// scheduling. A cancelled check is not a failure and must not back off.
/// </summary>
public sealed record UpdateCheckOutcome(
    bool Succeeded,
    bool Cancelled,
    UpdateStatus Status,
    DateTimeOffset? RetryAfterUtc);

/// <summary>
/// The raw result of a single GitHub API call.
/// </summary>
public sealed record ReleaseCheckResult(
    UpdateRelease? Release,
    string? NextETag,
    bool NotModified);
