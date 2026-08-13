using System;
using System.Threading;
using System.Threading.Tasks;
using Networker.Core.Updates;

namespace networker.Services.Updates
{
    /// <summary>
    /// Background automatic update checking. Starts only after the main window
    /// activates and returns immediately; every entry point catches and logs its
    /// own exceptions. Checks at the persisted cadence (24-hour success interval
    /// with a six-hour wake), backs off on failure, extends on rate-limit reset,
    /// and never contacts GitHub for developer/unpackaged builds or when
    /// automatic checks are disabled.
    /// </summary>
    public sealed class UpdateScheduler : IDisposable
    {
        private readonly UpdateCoordinator _coordinator;
        private readonly IInstalledVersionProvider _installedVersion;
        private readonly IUpdateClock _clock;
        private readonly IUpdateLog _log;
        private readonly object _gate = new();
        private Timer? _timer;
        private bool _started;
        private bool _checking;

        public UpdateScheduler(
            UpdateCoordinator coordinator,
            IInstalledVersionProvider installedVersion,
            IUpdateClock clock,
            IUpdateLog log)
        {
            _coordinator = coordinator;
            _installedVersion = installedVersion;
            _clock = clock;
            _log = log;
        }

        public void Start()
        {
            lock (_gate)
            {
                if (_started)
                {
                    return;
                }

                _started = true;
            }

            TryStartupCheck();
            _timer = new Timer(OnTick, null, UpdateSchedulerPolicy.WakeInterval, UpdateSchedulerPolicy.WakeInterval);
        }

        public void Stop()
        {
            lock (_gate)
            {
                _started = false;
                _timer?.Dispose();
                _timer = null;
            }
        }

        public void Dispose() => Stop();

        private void OnTick(object? state)
        {
            if (_started)
            {
                TryStartupCheck();
            }
        }

        /// <summary>
        /// Decides whether an automatic check should run now, then fires it
        /// without blocking. Never throws.
        /// </summary>
        private void TryStartupCheck()
        {
            try
            {
                if (_checking)
                {
                    return;
                }

                if (!AppSettings.AutomaticUpdateChecksEnabled)
                {
                    return;
                }

                InstalledVersion installed = _installedVersion.GetInstalledVersion();
                if (!installed.CanInstallUpdates)
                {
                    return;
                }

                UpdateChannel channel = AppSettings.IncludePrereleaseUpdates
                    ? UpdateChannel.Preview
                    : UpdateChannel.Stable;

                bool channelChanged = AppSettings.LastCheckedUpdateChannel != channel.ToString();
                if (!channelChanged && !UpdateSchedulerPolicy.IsDue(_clock.UtcNow, AppSettings.NextAutomaticUpdateCheckUtc))
                {
                    return;
                }

                _checking = true;
                _ = RunCheckAsync(channel, channelChanged);
            }
            catch (Exception ex)
            {
                _log.Warn($"Scheduler tick failed: {ex.GetType().Name}.");
                _checking = false;
            }
        }

        private async Task RunCheckAsync(UpdateChannel channel, bool channelChanged)
        {
            try
            {
                if (channelChanged)
                {
                    // A channel change is immediately due; never reuse the other
                    // channel's timestamp or next-check time.
                    _log.Info($"Update channel switched to {channel}; checking.");
                    AppSettings.LastCheckedUpdateChannel = channel.ToString();
                    AppSettings.NextAutomaticUpdateCheckUtc = null;
                }

                UpdateCheckOutcome outcome = await _coordinator
                    .CheckAsync(channel, manual: false, CancellationToken.None)
                    .ConfigureAwait(false);

                if (outcome.Cancelled)
                {
                    return;
                }

                DateTimeOffset now = _clock.UtcNow;
                AppSettings.LastCheckedUpdateChannel = channel.ToString();

                if (outcome.Succeeded)
                {
                    AppSettings.UpdateCheckFailureCount = 0;
                    AppSettings.LastSuccessfulUpdateCheckUtc = now;
                    AppSettings.NextAutomaticUpdateCheckUtc =
                        UpdateSchedulerPolicy.ComputeNextCheck(now, succeeded: true, 0, null);
                    _log.Debug($"Automatic check {channel} succeeded; next at {AppSettings.NextAutomaticUpdateCheckUtc:O}.");
                }
                else
                {
                    int failures = AppSettings.UpdateCheckFailureCount + 1;
                    AppSettings.UpdateCheckFailureCount = failures;
                    AppSettings.NextAutomaticUpdateCheckUtc =
                        UpdateSchedulerPolicy.ComputeNextCheck(now, succeeded: false, failures, outcome.RetryAfterUtc);
                    _log.Warn($"Automatic check {channel} failed ({failures} in a row); next at {AppSettings.NextAutomaticUpdateCheckUtc:O}.");
                }
            }
            catch (Exception ex)
            {
                _log.Warn($"Scheduler check failed: {ex.GetType().Name}.");
                AppSettings.UpdateCheckFailureCount = AppSettings.UpdateCheckFailureCount + 1;
                AppSettings.NextAutomaticUpdateCheckUtc = UpdateSchedulerPolicy.ComputeNextCheck(
                    _clock.UtcNow,
                    succeeded: false,
                    AppSettings.UpdateCheckFailureCount,
                    null);
            }
            finally
            {
                _checking = false;
            }
        }
    }
}
