using Networker.Core.Updates;

namespace Networker.Core.Tests.Updates;

public class UpdateCoordinatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "NetworkerTests", "coordinator-" + Guid.NewGuid().ToString("N"));
    private readonly FakeReleaseClient _client = new();
    private readonly FakeDownloader _downloader = new();
    private readonly FakeVerifier _verifier = new();
    private readonly FakeInstaller _installer = new();
    private readonly MemoryCacheStore _cache = new();
    private readonly MemoryStorage _storage;
    private readonly TestLog _log = new();
    private readonly FakeClock _clock = new();
    private readonly TestInstalledVersionProvider _installed;

    private UpdateCoordinator _coordinator = null!;

    public UpdateCoordinatorTests()
    {
        _storage = new MemoryStorage(_root);
        _installed = new TestInstalledVersionProvider(UpdateTestFakes.Packaged("v1.0.0", "1.0.0.65535"));
        _coordinator = new UpdateCoordinator(_installed, _client, _downloader, _verifier, _installer, _cache, _storage, _log, _clock);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private static ReleaseCheckResult CheckResult(string tag, string? etag = "etag-new")
    {
        var version = UpdateTestData.V(tag);
        var release = UpdateTestData.Release(tag,
            UpdateTestData.Asset(tag, NetworkerVersionPolicy.MsixAssetName(version)),
            UpdateTestData.Asset(tag, NetworkerVersionPolicy.ChecksumAssetName(version), size: 66));
        return new ReleaseCheckResult(release, etag, false);
    }

    private UpdateCoordinator DisabledCoordinator()
        => new(new TestInstalledVersionProvider(UpdateTestFakes.Dev()), _client, _downloader, _verifier, _installer, _cache, _storage, _log, _clock);

    [Fact]
    public void InitialSnapshot_IsIdle_WhenUpdatesInstallable()
    {
        UpdateSnapshot snapshot = _coordinator.Snapshot;
        Assert.Equal(UpdateStatus.Idle, snapshot.Status);
        Assert.Equal(UpdateChannel.Stable, snapshot.Channel);
        Assert.Equal("v1.0.0", snapshot.Installed.DisplayVersion);
    }

    [Fact]
    public void InitialSnapshot_IsDisabled_WhenUpdatesNotInstallable()
    {
        Assert.Equal(UpdateStatus.Disabled, DisabledCoordinator().Snapshot.Status);
    }

    [Fact]
    public async Task CheckAsync_Success_BecomesAvailableAndCaches()
    {
        _client.Results.Enqueue(CheckResult("v1.2.3", "etag-1"));

        UpdateCheckOutcome outcome = await _coordinator.CheckAsync(UpdateChannel.Stable, manual: true, CancellationToken.None);

        Assert.True(outcome.Succeeded);
        Assert.False(outcome.Cancelled);
        Assert.Equal(UpdateStatus.Available, outcome.Status);
        UpdateSnapshot snapshot = _coordinator.Snapshot;
        Assert.Equal(UpdateStatus.Available, snapshot.Status);
        Assert.Equal("v1.2.3", snapshot.AvailableRelease!.TagName);
        Assert.Equal("etag-1", _cache.GetETag(UpdateChannel.Stable));
        Assert.Equal("v1.2.3", _cache.GetCachedRelease(UpdateChannel.Stable)!.TagName);
    }

    [Fact]
    public async Task CheckAsync_NothingNewer_IsUpToDate()
    {
        _client.Results.Enqueue(CheckResult("v0.9.0"));

        UpdateCheckOutcome outcome = await _coordinator.CheckAsync(UpdateChannel.Stable, manual: true, CancellationToken.None);

        Assert.True(outcome.Succeeded);
        Assert.Equal(UpdateStatus.UpToDate, outcome.Status);
        Assert.Null(_coordinator.Snapshot.AvailableRelease);
        Assert.Equal(UpdateStatus.UpToDate, _coordinator.Snapshot.Status);
    }

    [Fact]
    public async Task CheckAsync_NoRelease_IsUpToDate()
    {
        _client.Results.Enqueue(new ReleaseCheckResult(null, null, false));

        UpdateCheckOutcome outcome = await _coordinator.CheckAsync(UpdateChannel.Stable, manual: true, CancellationToken.None);

        Assert.Equal(UpdateStatus.UpToDate, outcome.Status);
        Assert.Equal(UpdateStatus.UpToDate, _coordinator.Snapshot.Status);
    }

    [Fact]
    public async Task CheckAsync_304WithCache_BecomesAvailableFromCache()
    {
        _cache.SetETag(UpdateChannel.Stable, "etag-0");
        _cache.SetCachedRelease(UpdateChannel.Stable, CheckResult("v1.2.3", "etag-0").Release);
        _client.Results.Enqueue(new ReleaseCheckResult(null, "etag-1", true));

        UpdateCheckOutcome outcome = await _coordinator.CheckAsync(UpdateChannel.Stable, manual: true, CancellationToken.None);

        Assert.Equal(UpdateStatus.Available, outcome.Status);
        Assert.Equal("v1.2.3", _coordinator.Snapshot.AvailableRelease!.TagName);
        Assert.Equal("etag-0", _client.CalledEtags[0]);
        Assert.Equal("etag-1", _cache.GetETag(UpdateChannel.Stable));
    }

    [Fact]
    public async Task CheckAsync_304WithoutCache_RetriesOnceWithoutEtag()
    {
        _cache.SetETag(UpdateChannel.Stable, "etag-0");
        _client.Results.Enqueue(new ReleaseCheckResult(null, null, true));
        _client.Results.Enqueue(CheckResult("v1.2.3", "etag-2"));

        UpdateCheckOutcome outcome = await _coordinator.CheckAsync(UpdateChannel.Stable, manual: true, CancellationToken.None);

        Assert.Equal(2, _client.CallCount);
        Assert.Equal(new string?[] { "etag-0", null }, _client.CalledEtags);
        Assert.Equal(UpdateStatus.Available, outcome.Status);
        Assert.Equal("etag-2", _cache.GetETag(UpdateChannel.Stable));
    }

    [Fact]
    public async Task CheckAsync_NetworkFailure_Fails()
    {
        _client.Throw = new HttpRequestException("boom");

        UpdateCheckOutcome outcome = await _coordinator.CheckAsync(UpdateChannel.Stable, manual: true, CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.False(outcome.Cancelled);
        Assert.Equal(UpdateStatus.Failed, outcome.Status);
        Assert.Null(outcome.RetryAfterUtc);
        Assert.Equal(UpdateStatus.Failed, _coordinator.Snapshot.Status);
        Assert.Contains("Couldn't check", _coordinator.Snapshot.Error!.Message);
    }

    [Fact]
    public async Task CheckAsync_RateLimited_ReportsRetryAfter()
    {
        DateTimeOffset retryAfter = DateTimeOffset.UtcNow.AddMinutes(5);
        _client.Throw = new UpdateException("rate limited", retryAfterUtc: retryAfter);

        UpdateCheckOutcome outcome = await _coordinator.CheckAsync(UpdateChannel.Stable, manual: true, CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Equal(retryAfter, outcome.RetryAfterUtc);
        Assert.Equal(UpdateStatus.Failed, _coordinator.Snapshot.Status);
    }

    [Fact]
    public async Task CheckAsync_Cancelled_IsNotAFailure()
    {
        _client.Gate = ct => Task.Delay(Timeout.InfiniteTimeSpan, ct);
        using var cts = new CancellationTokenSource();

        Task<UpdateCheckOutcome> check = _coordinator.CheckAsync(UpdateChannel.Stable, manual: false, cts.Token);
        cts.Cancel();
        UpdateCheckOutcome outcome = await check;

        Assert.False(outcome.Succeeded);
        Assert.True(outcome.Cancelled);
        Assert.Equal(UpdateStatus.Cancelled, outcome.Status);
        Assert.Equal(UpdateStatus.Idle, _coordinator.Snapshot.Status);
    }

    [Fact]
    public async Task CheckAsync_ConcurrentChecks_Coalesce()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _client.Gate = _ => gate.Task;

        Task<UpdateCheckOutcome> first = _coordinator.CheckAsync(UpdateChannel.Stable, manual: true, CancellationToken.None);
        Task<UpdateCheckOutcome> second = _coordinator.CheckAsync(UpdateChannel.Stable, manual: true, CancellationToken.None);

        Assert.Same(first, second);

        gate.SetResult();
        await first;

        Assert.Equal(1, _client.CallCount);
    }

    [Fact]
    public async Task Dismiss_ThenAutoCheckQuiet_ManualSurfaces()
    {
        _client.Results.Enqueue(CheckResult("v1.2.3"));
        await _coordinator.CheckAsync(UpdateChannel.Stable, manual: true, CancellationToken.None);
        Assert.Equal(UpdateStatus.Available, _coordinator.Snapshot.Status);

        _coordinator.DismissUpdate();
        Assert.Equal(UpdateStatus.Idle, _coordinator.Snapshot.Status);
        Assert.Equal("v1.2.3", _cache.GetDismissedUpdateTag());

        // Automatic check stays quiet about the dismissed tag.
        _client.Results.Enqueue(CheckResult("v1.2.3"));
        UpdateCheckOutcome auto = await _coordinator.CheckAsync(UpdateChannel.Stable, manual: false, CancellationToken.None);
        Assert.Equal(UpdateStatus.UpToDate, auto.Status);
        Assert.Null(_coordinator.Snapshot.AvailableRelease);

        // A manual check still surfaces it.
        _client.Results.Enqueue(CheckResult("v1.2.3"));
        UpdateCheckOutcome manual = await _coordinator.CheckAsync(UpdateChannel.Stable, manual: true, CancellationToken.None);
        Assert.Equal(UpdateStatus.Available, manual.Status);
        Assert.Equal("v1.2.3", _coordinator.Snapshot.AvailableRelease!.TagName);
    }

    [Fact]
    public async Task NewerRelease_ClearsDismissal()
    {
        _client.Results.Enqueue(CheckResult("v1.2.3"));
        await _coordinator.CheckAsync(UpdateChannel.Stable, manual: true, CancellationToken.None);
        _coordinator.DismissUpdate();
        Assert.Equal("v1.2.3", _cache.GetDismissedUpdateTag());

        // Automatic check sees a newer tag: dismissal is superseded.
        _client.Results.Enqueue(CheckResult("v1.2.4"));
        UpdateCheckOutcome outcome = await _coordinator.CheckAsync(UpdateChannel.Stable, manual: false, CancellationToken.None);

        Assert.Equal(UpdateStatus.Available, outcome.Status);
        Assert.Equal("v1.2.4", _coordinator.Snapshot.AvailableRelease!.TagName);
        Assert.Null(_cache.GetDismissedUpdateTag());
    }

    [Fact]
    public async Task Install_HappyPath_StagesAndClearsDismissal()
    {
        _client.Results.Enqueue(CheckResult("v1.2.3"));
        await _coordinator.CheckAsync(UpdateChannel.Stable, manual: true, CancellationToken.None);

        var statuses = new List<UpdateStatus>();
        _coordinator.StateChanged += snapshot => statuses.Add(snapshot.Status);

        UpdateSnapshot snapshot = await _coordinator.InstallUpdateAsync(CancellationToken.None);

        Assert.Equal(UpdateStatus.RestartRequired, snapshot.Status);
        Assert.Equal(1.0, snapshot.Progress, 6);
        Assert.Equal(1, _downloader.CallCount);
        Assert.Equal(1, _installer.CallCount);
        Assert.Contains("v1.2.3", _storage.Preserved);
        Assert.Null(_cache.GetDismissedUpdateTag());
        Assert.Contains(UpdateStatus.Downloading, statuses);
        Assert.Contains(UpdateStatus.Verifying, statuses);
        Assert.Contains(UpdateStatus.Installing, statuses);
        Assert.Contains(UpdateStatus.RestartRequired, statuses);
    }

    [Fact]
    public async Task Install_CancelledDuringDownload_GoesCancelledAndCleans()
    {
        _client.Results.Enqueue(CheckResult("v1.2.3"));
        await _coordinator.CheckAsync(UpdateChannel.Stable, manual: true, CancellationToken.None);
        _downloader.Handler = async (_, _, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return null!;
        };

        using var cts = new CancellationTokenSource();
        Task<UpdateSnapshot> install = _coordinator.InstallUpdateAsync(cts.Token);
        cts.Cancel();
        UpdateSnapshot snapshot = await install;

        Assert.Equal(UpdateStatus.Cancelled, snapshot.Status);
        Assert.Equal(0.0, snapshot.Progress, 6);
        Assert.Contains("v1.2.3", _storage.Cleaned);
        Assert.Empty(_storage.Preserved);
        Assert.Equal("v1.2.3", snapshot.AvailableRelease!.TagName);
    }

    [Fact]
    public async Task Install_FailureDuringDownload_FailsAndCleans()
    {
        _client.Results.Enqueue(CheckResult("v1.2.3"));
        await _coordinator.CheckAsync(UpdateChannel.Stable, manual: true, CancellationToken.None);
        _downloader.Handler = (_, _, _) => throw new UpdateException("network down");

        UpdateSnapshot snapshot = await _coordinator.InstallUpdateAsync(CancellationToken.None);

        Assert.Equal(UpdateStatus.Failed, snapshot.Status);
        Assert.Contains("couldn't be downloaded", snapshot.Error!.Message);
        Assert.Contains("v1.2.3", _storage.Cleaned);
        Assert.Empty(_storage.Preserved);
    }

    [Fact]
    public async Task Install_FailedByInstaller_ReportsErrorAndDoesNotStage()
    {
        _client.Results.Enqueue(CheckResult("v1.2.3"));
        await _coordinator.CheckAsync(UpdateChannel.Stable, manual: true, CancellationToken.None);
        _installer.Handler = (_, _) => Task.FromResult(new UpdateInstallResult(false, "message", "0x80073D00", null));

        UpdateSnapshot snapshot = await _coordinator.InstallUpdateAsync(CancellationToken.None);

        Assert.Equal(UpdateStatus.Failed, snapshot.Status);
        Assert.Contains("couldn't be installed", snapshot.Error!.Message);
        Assert.Empty(_storage.Preserved);
        Assert.Contains(_log.Entries, entry => entry.Contains("0x80073D00"));
    }

    [Fact]
    public async Task Install_NoAvailableRelease_Fails()
    {
        UpdateSnapshot snapshot = await _coordinator.InstallUpdateAsync(CancellationToken.None);

        Assert.Equal(UpdateStatus.Failed, snapshot.Status);
        Assert.Contains("No update is available", snapshot.Error!.Message);
    }

    [Fact]
    public async Task Install_UninstallableBuild_Fails()
    {
        UpdateSnapshot snapshot = await DisabledCoordinator().InstallUpdateAsync(CancellationToken.None);

        Assert.Equal(UpdateStatus.Failed, snapshot.Status);
        Assert.Contains("aren't available", snapshot.Error!.Message);
    }

    [Fact]
    public async Task Install_DefensiveGuard_TreatsAsUpToDate()
    {
        _client.Results.Enqueue(CheckResult("v1.1.0"));
        await _coordinator.CheckAsync(UpdateChannel.Stable, manual: true, CancellationToken.None);
        Assert.Equal(UpdateStatus.Available, _coordinator.Snapshot.Status);

        // Installed version moved past the staged release before install.
        _installed.Value = UpdateTestFakes.Packaged("v1.2.0", "1.2.0.65535");
        UpdateSnapshot snapshot = await _coordinator.InstallUpdateAsync(CancellationToken.None);

        Assert.Equal(UpdateStatus.UpToDate, snapshot.Status);
        Assert.Null(snapshot.AvailableRelease);
        Assert.Equal(0, _downloader.CallCount);
    }

    [Fact]
    public void CleanupConfirmedStaged_RemovesOnlyConfirmedTags()
    {
        _storage.PreserveStaged("v1.0.0");
        _storage.PreserveStaged("v1.1.0");
        _storage.PreserveStaged("not-a-tag");

        _coordinator.CleanupConfirmedStaged();

        Assert.Equal(new[] { "v1.0.0" }, _storage.Removed);
    }

    [Fact]
    public async Task IsBusy_TracksActiveWork()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _client.Gate = _ => gate.Task;

        Task<UpdateCheckOutcome> check = _coordinator.CheckAsync(UpdateChannel.Stable, manual: true, CancellationToken.None);
        Assert.True(_coordinator.IsBusy);

        gate.SetResult();
        await check;
        Assert.False(_coordinator.IsBusy);
    }
}
