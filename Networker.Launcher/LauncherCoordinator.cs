using System.Diagnostics;
using Networker.Launcher.Migration;
using Networker.Update.Contracts.Scheduling;
using Networker.Update.Contracts.Releases;
using Networker.Update.Contracts.State;
using Networker.Update.Contracts.Versioning;
using Networker.Update.Diagnostics;
using Networker.Update.Releases;
using Networker.Update.Security;
using NuGet.Versioning;

namespace Networker.Launcher;

internal sealed class LauncherCoordinator
{
    private readonly LauncherStateStore _state = new();
    private readonly UpdateLog _log = new();
    private readonly string _installRoot = Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))?.FullName
        ?? AppContext.BaseDirectory;

    public async Task RunAsync(string[] arguments)
    {
        CleanupTemporaryUpdateHosts();
        if (IsMainAppRunning()) return;
        try
        {
            LauncherState state = _state.Read();
            if (!state.FirstRunCompleted)
            {
                var migration = new MigrationCoordinator();
                using var firstRun = new FirstRunForm(migration.HasLegacyInstall);
                if (firstRun.ShowDialog() != DialogResult.OK) return;
                if (migration.HasLegacyInstall && !await migration.RunAsync(firstRun)) return;
                state = _state.Read() with { FirstRunCompleted = true };
                _state.Write(state);
            }

            RecoveryJournal? recovery = RecoveryJournal.Read();
            if (recovery is not null && recovery.Phase == RecoveryPhase.Applying)
            {
                string active = new ActiveSlotStore(_installRoot).Read().ActiveSlot;
                if (active == recovery.TargetSlot)
                    (recovery with { Phase = RecoveryPhase.AwaitingHealth }).Write();
                else
                    RecoveryJournal.Delete();
            }

            if (CanCheck(state) && await TryCheckAndStageAsync(state)) return;
        }
        catch (Exception ex) { _log.Error("Launcher update path failed; opening the installed application.", ex); }

        await LaunchAndMonitorHealthAsync(arguments);
    }

    private static void CleanupTemporaryUpdateHosts()
    {
        try
        {
            foreach (string path in Directory.EnumerateFiles(Path.GetTempPath(), "Networker.UpdateHost-*.exe"))
            {
                try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static bool IsMainAppRunning()
    {
        using var mutex = new Mutex(false, "Local\\Networker.MainApp");
        try
        {
            if (!mutex.WaitOne(0)) return true;
            mutex.ReleaseMutex();
            return false;
        }
        catch (AbandonedMutexException) { return false; }
    }

    private static bool CanCheck(LauncherState state) => state.AutomaticChecksEnabled
        && UpdateTrustKeys.PublicKeys.Count > 0
        && UpdateSchedulePolicy.IsDue(DateTimeOffset.UtcNow, state.ManualCheckRequested ? null : state.NextCheckUtc);

    private async Task<bool> TryCheckAndStageAsync(LauncherState state)
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = System.Net.DecompressionMethods.All };
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var client = new SignedGitHubReleaseClient(http, new ReleaseFeedVerifier(UpdateTrustKeys.PublicKeys));
        AvailableRelease? available;
        try
        {
            using var deadline = new CancellationTokenSource(UpdateSchedulePolicy.MetadataDeadline);
            available = await client.CheckAsync(state.Channel, deadline.Token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException or InvalidDataException)
        {
            int failures = state.FailureCount + 1;
            _state.Write(state with { FailureCount = failures, NextCheckUtc = UpdateSchedulePolicy.ComputeNextCheck(DateTimeOffset.UtcNow, false, failures), ManualCheckRequested = false });
            _log.Warn($"Update check failed ({ex.GetType().Name}).");
            return false;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        _state.Write(state with { FailureCount = 0, LastSuccessfulCheckUtc = now, NextCheckUtc = UpdateSchedulePolicy.ComputeNextCheck(now, true, 0), ManualCheckRequested = false });
        if (available is null) return false;
        AvailableRelease release = available;

        string currentText = File.Exists(Path.Combine(AppContext.BaseDirectory, "version.txt"))
            ? File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "version.txt")).Trim() : "0.0.0";
        if (!NuGetVersion.TryParse(currentText, out NuGetVersion? current)
            || !NuGetVersion.TryParse(release.Manifest.Version, out NuGetVersion? target)
            || current is null || target is null) return false;
        string releaseChannel = release.Manifest.Channel;
        string? highestText = releaseChannel == NetworkerVersionPolicy.PreviewChannel
            ? state.HighestAuthenticatedPreviewVersion : state.HighestAuthenticatedStableVersion;
        if (NuGetVersion.TryParse(highestText, out NuGetVersion? highest) && target < highest) return false;
        state = _state.Update(value => value with
        {
            HighestAuthenticatedStableVersion = releaseChannel == NetworkerVersionPolicy.StableChannel ? target.ToNormalizedString() : value.HighestAuthenticatedStableVersion,
            HighestAuthenticatedPreviewVersion = releaseChannel == NetworkerVersionPolicy.PreviewChannel ? target.ToNormalizedString() : value.HighestAuthenticatedPreviewVersion,
        });
        if (target <= current) return false;

        using var form = new UpdateProgressForm();
        bool handedOff = false;
        form.Shown += async (_, _) =>
        {
            try
            {
                string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Networker", "Updates", "Downloads");
                Directory.CreateDirectory(downloads);
                string package = Path.Combine(downloads, release.Manifest.FileName);
                await client.DownloadAsync(release, package, form.Progress, form.CancellationToken);
                string active = new ActiveSlotStore(_installRoot).Read().ActiveSlot;
                string targetSlot = active == "app-a" ? "app-b" : "app-a";
                new RecoveryJournal(RecoveryJournal.CurrentSchemaVersion, active, targetSlot, current.ToNormalizedString(), target.ToNormalizedString(), package, RecoveryPhase.Applying, 0, DateTimeOffset.UtcNow).Write();
                _state.Update(value => value with
                {
                    LastObservedTarget = target.ToNormalizedString(),
                    RecoveryJournalPath = RecoveryJournal.DefaultPath,
                });
                LaunchUpdateHost(package, targetSlot, release.Manifest.Sha256, target.ToNormalizedString());
                handedOff = true;
                form.Close();
            }
            catch (OperationCanceledException) { form.Close(); }
            catch (Exception ex) { _log.Error("Update staging failed.", ex); form.Close(); }
        };
        form.ShowDialog();
        return handedOff;
    }

    private void LaunchUpdateHost(string package, string targetSlot, string sha256, string version)
    {
        string source = Path.Combine(AppContext.BaseDirectory, "Networker.UpdateHost.exe");
        if (!File.Exists(source)) throw new FileNotFoundException("Update host is missing.", source);
        string temporary = Path.Combine(Path.GetTempPath(), "Networker.UpdateHost-" + Guid.NewGuid().ToString("N") + ".exe");
        File.Copy(source, temporary);
        var start = new ProcessStartInfo(temporary) { UseShellExecute = true, WorkingDirectory = Path.GetTempPath() };
        Add("root", _installRoot); Add("package", package); Add("target-slot", targetSlot); Add("sha256", sha256); Add("version", version); Add("wait-pid", Environment.ProcessId.ToString());
        Process.Start(start);
        void Add(string name, string value) { start.ArgumentList.Add("--" + name); start.ArgumentList.Add(value); }
    }

    private async Task LaunchAndMonitorHealthAsync(string[] arguments)
    {
        var (process, token) = MainAppProcess.Start(arguments.Where(x => x != "--networker-updated").ToArray());
        string marker = MainAppProcess.HealthMarkerPath(token);
        DateTimeOffset deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTimeOffset.UtcNow < deadline && !process.HasExited && !File.Exists(marker)) await Task.Delay(100);
        if (File.Exists(marker))
        {
            try { File.Delete(marker); } catch { }
            RecoveryJournal.Delete();
            _state.Update(value => value with { RecoveryJournalPath = null });
            await FinalizeLegacyRemovalAsync();
            if (arguments.Contains("--networker-updated", StringComparer.Ordinal))
            {
                string version = File.Exists(Path.Combine(AppContext.BaseDirectory, "version.txt"))
                    ? File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "version.txt")).Trim() : string.Empty;
                using var confirmation = new InstalledVersionForm(version);
                confirmation.ShowDialog();
            }
            return;
        }

        RecoveryJournal? journal = RecoveryJournal.Read();
        if (journal is null || journal.Phase != RecoveryPhase.AwaitingHealth) return;
        journal = journal with { FailedHealthAttempts = journal.FailedHealthAttempts + 1 };
        journal.Write();
        if (journal.FailedHealthAttempts < 2 || !process.HasExited) return;
        new ActiveSlotStore(_installRoot).Write(new ActiveSlotState { ActiveSlot = journal.PreviousSlot });
        _log.Warn("Updated application failed startup health twice; restored the previous slot.");
        RecoveryJournal.Delete();
        Process.Start(new ProcessStartInfo(Path.Combine(_installRoot, "Networker.exe")) { UseShellExecute = true, WorkingDirectory = _installRoot });
    }

    private async Task FinalizeLegacyRemovalAsync()
    {
        LauncherState state = _state.Read();
        if (string.IsNullOrWhiteSpace(state.PendingLegacyMsixRemoval)) return;
        if (await MigrationCoordinator.TryRemoveLegacyPackageAsync(state.PendingLegacyMsixRemoval))
            _state.Update(value => value with { PendingLegacyMsixRemoval = null });
    }
}
