using System.Net;

namespace Networker.Core.Updates;

/// <summary>
/// The serialized update state machine. Owns check coalescing, release
/// selection, download/verify/install orchestration, dismissal semantics, and
/// post-restart staged-package cleanup. Contains no WinUI or application-data
/// calls; every platform dependency arrives through its contract.
/// </summary>
public sealed class UpdateCoordinator
{
    private readonly IInstalledVersionProvider _installedVersionProvider;
    private readonly IGitHubReleaseClient _releaseClient;
    private readonly IUpdatePackageDownloader _downloader;
    private readonly IUpdatePackageVerifier _verifier;
    private readonly IUpdateInstaller _installer;
    private readonly IUpdateCacheStore _cacheStore;
    private readonly IUpdatePackageStorage _storage;
    private readonly IUpdateLog _log;
    private readonly IUpdateClock _clock;

    private readonly SemaphoreSlim _installGate = new(1, 1);
    private readonly object _sync = new();
    private readonly Dictionary<UpdateChannel, Task<UpdateCheckOutcome>> _activeChecks = new();
    private UpdateSnapshot _snapshot;

    /// <summary>Raised after every state transition, on the calling thread.</summary>
    public event Action<UpdateSnapshot>? StateChanged;

    /// <summary>Raised with a 0..1 (or negative, indeterminate) value during downloads.</summary>
    public event Action<double>? ProgressChanged;

    public UpdateCoordinator(
        IInstalledVersionProvider installedVersionProvider,
        IGitHubReleaseClient releaseClient,
        IUpdatePackageDownloader downloader,
        IUpdatePackageVerifier verifier,
        IUpdateInstaller installer,
        IUpdateCacheStore cacheStore,
        IUpdatePackageStorage storage,
        IUpdateLog log,
        IUpdateClock clock)
    {
        _installedVersionProvider = installedVersionProvider;
        _releaseClient = releaseClient;
        _downloader = downloader;
        _verifier = verifier;
        _installer = installer;
        _cacheStore = cacheStore;
        _storage = storage;
        _log = log;
        _clock = clock;

        var installed = installedVersionProvider.GetInstalledVersion();
        _snapshot = new UpdateSnapshot(
            installed.CanInstallUpdates ? UpdateStatus.Idle : UpdateStatus.Disabled,
            UpdateChannel.Stable,
            installed,
            null,
            0,
            null);
    }

    /// <summary>The latest published snapshot (thread-safe).</summary>
    public UpdateSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                return _snapshot;
            }
        }
    }

    /// <summary>True while a check, download, verification, or install is active.</summary>
    public bool IsBusy
    {
        get
        {
            lock (_sync)
            {
                return _snapshot.Status is UpdateStatus.Checking
                    or UpdateStatus.Downloading
                    or UpdateStatus.Verifying
                    or UpdateStatus.Installing;
            }
        }
    }

    /// <summary>
    /// Runs a check for the channel. Concurrent checks for the same channel
    /// coalesce onto the in-flight request. A successful valid <c>200</c> or
    /// <c>304</c> yields <see cref="UpdateCheckOutcome.Succeeded"/>; user
    /// cancellation yields <see cref="UpdateCheckOutcome.Cancelled"/> without
    /// counting as a failure.
    /// </summary>
    public Task<UpdateCheckOutcome> CheckAsync(UpdateChannel channel, bool manual, CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (_activeChecks.TryGetValue(channel, out Task<UpdateCheckOutcome>? existing))
            {
                return existing;
            }

            Task<UpdateCheckOutcome> task = RunCheckAsync(channel, manual, cancellationToken);
            _activeChecks[channel] = task;
            _ = task.ContinueWith(
                _ =>
                {
                    lock (_sync)
                    {
                        _activeChecks.Remove(channel);
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return task;
        }
    }

    /// <summary>
    /// Downloads, verifies, and stages the available release. Cancellation is
    /// honored during download/verify; once Windows deployment begins it is
    /// ignored because cancelling deployment can leave pending state.
    /// </summary>
    public async Task<UpdateSnapshot> InstallUpdateAsync(CancellationToken cancellationToken)
    {
        await _installGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            UpdateSnapshot snapshot = Snapshot;
            InstalledVersion installed = _installedVersionProvider.GetInstalledVersion();
            UpdateRelease? release = snapshot.AvailableRelease;

            if (!installed.CanInstallUpdates)
            {
                SetStatus(snapshot with
                {
                    Status = UpdateStatus.Failed,
                    Error = new UpdateErrorInfo("Automatic updates aren't available for this build."),
                });
                return _snapshot;
            }

            if (release is null)
            {
                SetStatus(snapshot with
                {
                    Status = UpdateStatus.Failed,
                    Error = new UpdateErrorInfo("No update is available to install."),
                });
                return _snapshot;
            }

            if (installed.SemanticVersion is not null && release.Version <= installed.SemanticVersion)
            {
                SetStatus(snapshot with { Status = UpdateStatus.UpToDate, AvailableRelease = null, Error = null });
                return _snapshot;
            }

            try
            {
                SelectedUpdateAssets selected = UpdateAssetSelector.Select(release);
                string directory = _storage.GetDownloadDirectoryPath(release.TagName);

                SetStatus(snapshot with { Status = UpdateStatus.Downloading, Progress = 0, Error = null });
                var progress = new Progress<double>(ReportProgress);
                DownloadedPackage downloaded = await _downloader
                    .DownloadAsync(release, selected, directory, progress, cancellationToken)
                    .ConfigureAwait(false);

                SetStatus(_snapshot with { Status = UpdateStatus.Verifying, Progress = 1 });
                VerifiedPackage verified = await _verifier
                    .VerifyAsync(release, downloaded, installed, cancellationToken)
                    .ConfigureAwait(false);

                SetStatus(Snapshot with { Status = UpdateStatus.Installing, Progress = 1 });
                UpdateInstallResult installResult = await _installer
                    .InstallAsync(verified, cancellationToken)
                    .ConfigureAwait(false);

                if (!installResult.Succeeded)
                {
                    _log.Error(
                        $"Package deployment failed: code={installResult.ErrorCode ?? "unknown"} activity={installResult.ActivityId}");
                    SetStatus(Snapshot with
                    {
                        Status = UpdateStatus.Failed,
                        Progress = 0,
                        Error = new UpdateErrorInfo("The update couldn't be installed."),
                    });
                    return _snapshot;
                }

                _storage.PreserveStaged(release.TagName);
                _cacheStore.SetDismissedUpdateTag(null);
                _log.Info($"Staged {release.TagName}; restart required.");
                SetStatus(Snapshot with { Status = UpdateStatus.RestartRequired, Progress = 1 });
                return _snapshot;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _storage.Cleanup(release.TagName);
                _log.Info($"Update installation cancelled for {release.TagName}.");
                SetStatus(Snapshot with
                {
                    Status = UpdateStatus.Cancelled,
                    Progress = 0,
                    AvailableRelease = release,
                    Error = null,
                });
                return _snapshot;
            }
            catch (Exception ex) when (ex is UpdateException or HttpRequestException or IOException or UnauthorizedAccessException)
            {
                _log.Error($"Update installation failed for {release.TagName}: {ex.GetType().Name}.");
                _storage.Cleanup(release.TagName);
                SetStatus(Snapshot with
                {
                    Status = UpdateStatus.Failed,
                    Progress = 0,
                    AvailableRelease = release,
                    Error = new UpdateErrorInfo("The update couldn't be downloaded."),
                });
                return _snapshot;
            }
        }
        finally
        {
            _installGate.Release();
        }
    }

    /// <summary>
    /// Defers the available update: the tag is persisted so automatic checks
    /// stay quiet about it, while manual checks still surface it.
    /// </summary>
    public void DismissUpdate()
    {
        UpdateSnapshot snapshot = Snapshot;
        if (snapshot.AvailableRelease is { } release)
        {
            _cacheStore.SetDismissedUpdateTag(release.TagName);
            _log.Info($"Update {release.TagName} dismissed by the user.");
        }

        SetStatus(snapshot with { Status = UpdateStatus.Idle, AvailableRelease = null, Error = null });
    }

    /// <summary>
    /// Removes staged packages whose tag is already confirmed installed (called
    /// at startup). Never touches anything outside the update staging root.
    /// </summary>
    public void CleanupConfirmedStaged()
    {
        try
        {
            InstalledVersion installed = _installedVersionProvider.GetInstalledVersion();
            foreach (string tag in _storage.GetStagedTags())
            {
                if (NetworkerVersionPolicy.TryParseTag(tag, out var tagVersion)
                    && installed.SemanticVersion is not null
                    && tagVersion <= installed.SemanticVersion)
                {
                    _storage.RemoveStaged(tag);
                    _log.Info($"Removed confirmed staged update {tag}.");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warn($"Staged-package cleanup failed: {ex.GetType().Name}.");
        }
    }

    private async Task<UpdateCheckOutcome> RunCheckAsync(UpdateChannel channel, bool manual, CancellationToken cancellationToken)
    {
        UpdateSnapshot snapshot = Snapshot;
        SetStatus(snapshot with
        {
            Status = UpdateStatus.Checking,
            Channel = channel,
            AvailableRelease = null,
            Error = null,
        });

        try
        {
            InstalledVersion installed = _installedVersionProvider.GetInstalledVersion();
            string? etag = _cacheStore.GetETag(channel);
            ReleaseCheckResult result = await _releaseClient.CheckAsync(channel, etag, cancellationToken).ConfigureAwait(false);

            UpdateRelease? release = result.Release;
            if (result.NotModified)
            {
                release = _cacheStore.GetCachedRelease(channel);
                if (release is null)
                {
                    // Stale ETag with no cache: retry once without it.
                    _log.Info($"Release check {channel}: stale ETag without cache; retrying.");
                    result = await _releaseClient.CheckAsync(channel, null, cancellationToken).ConfigureAwait(false);
                    release = result.Release;
                }
            }

            if (release is not null)
            {
                _cacheStore.SetETag(channel, result.NextETag);
                _cacheStore.SetCachedRelease(channel, release);
            }

            bool nothingNewer = installed.SemanticVersion is not null && release is not null && release.Version <= installed.SemanticVersion;
            if (release is null || nothingNewer)
            {
                SetStatus(Snapshot with { Status = UpdateStatus.UpToDate, AvailableRelease = null, Error = null });
                return new UpdateCheckOutcome(true, false, UpdateStatus.UpToDate, null);
            }

            string? dismissed = _cacheStore.GetDismissedUpdateTag();
            if (!manual && string.Equals(dismissed, release.TagName, StringComparison.Ordinal))
            {
                // The user dismissed this tag; automatic checks stay quiet.
                _log.Info($"Release check {channel}: {release.TagName} dismissed; staying quiet.");
                SetStatus(Snapshot with { Status = UpdateStatus.UpToDate, AvailableRelease = null, Error = null });
                return new UpdateCheckOutcome(true, false, UpdateStatus.UpToDate, null);
            }

            _cacheStore.SetDismissedUpdateTag(null); // a new version supersedes an old dismissal
            SetStatus(Snapshot with { Status = UpdateStatus.Available, AvailableRelease = release, Error = null });
            return new UpdateCheckOutcome(true, false, UpdateStatus.Available, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetStatus(Snapshot with { Status = UpdateStatus.Idle, Error = null });
            return new UpdateCheckOutcome(false, true, UpdateStatus.Cancelled, null);
        }
        catch (UpdateException ex) when (ex.RetryAfterUtc is not null)
        {
            _log.Warn($"Release check {channel} rate limited until {ex.RetryAfterUtc:O}.");
            SetStatus(Snapshot with
            {
                Status = UpdateStatus.Failed,
                Error = new UpdateErrorInfo("The update service is busy. Try again later."),
            });
            return new UpdateCheckOutcome(false, false, UpdateStatus.Failed, ex.RetryAfterUtc);
        }
        catch (Exception ex) when (ex is UpdateException or HttpRequestException or TaskCanceledException)
        {
            _log.Warn($"Release check {channel} failed: {ex.GetType().Name}.");
            SetStatus(Snapshot with
            {
                Status = UpdateStatus.Failed,
                Error = new UpdateErrorInfo("Couldn't check for updates right now."),
            });
            return new UpdateCheckOutcome(false, false, UpdateStatus.Failed, null);
        }
    }

    private void ReportProgress(double value)
    {
        lock (_sync)
        {
            _snapshot = _snapshot with { Progress = value };
        }

        try
        {
            ProgressChanged?.Invoke(value);
        }
        catch (Exception ex)
        {
            _log.Warn($"ProgressChanged handler failed: {ex.GetType().Name}.");
        }
    }

    private void SetStatus(UpdateSnapshot snapshot)
    {
        lock (_sync)
        {
            _snapshot = snapshot;
        }

        try
        {
            StateChanged?.Invoke(snapshot);
        }
        catch (Exception ex)
        {
            _log.Warn($"StateChanged handler failed: {ex.GetType().Name}.");
        }
    }
}
