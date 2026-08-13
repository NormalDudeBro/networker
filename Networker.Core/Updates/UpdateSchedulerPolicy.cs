namespace Networker.Core.Updates;

/// <summary>
/// The pure scheduling policy behind the WinUI scheduler: success cadence,
/// periodic wake, exponential failure backoff, and rate-limit extension.
/// Kept in Core so it can be tested offline with injected times.
/// </summary>
public static class UpdateSchedulerPolicy
{
    /// <summary>Automatic checks run when the last successful check is at least this old.</summary>
    public static readonly TimeSpan SuccessInterval = TimeSpan.FromHours(24);

    /// <summary>Periodic wake cadence that lets long-running instances become due.</summary>
    public static readonly TimeSpan WakeInterval = TimeSpan.FromHours(6);

    /// <summary>Backoff for consecutive failed checks: 15 minutes, 1 hour, 6 hours, then 24 hours.</summary>
    public static TimeSpan Backoff(int failureCount) => failureCount switch
    {
        <= 0 => TimeSpan.Zero,
        1 => TimeSpan.FromMinutes(15),
        2 => TimeSpan.FromHours(1),
        3 => TimeSpan.FromHours(6),
        _ => TimeSpan.FromHours(24),
    };

    /// <summary>
    /// Computes the next automatic check time after an attempt. A success moves
    /// the next check one cadence out; a failure backs off exponentially; a
    /// rate-limit reset in the future extends the wait.
    /// </summary>
    public static DateTimeOffset ComputeNextCheck(
        DateTimeOffset now,
        bool succeeded,
        int failureCount,
        DateTimeOffset? retryAfterUtc)
    {
        DateTimeOffset next = succeeded
            ? now + SuccessInterval
            : now + Backoff(failureCount);

        if (retryAfterUtc is { } rateLimit && rateLimit > next)
        {
            next = rateLimit;
        }

        return next;
    }

    /// <summary>
    /// Whether an automatic check should run now, based on the persisted next
    /// check time. A missing value (first run) is immediately due.
    /// </summary>
    public static bool IsDue(DateTimeOffset now, DateTimeOffset? nextAutomaticCheckUtc)
        => nextAutomaticCheckUtc is null || now >= nextAutomaticCheckUtc.Value;
}
