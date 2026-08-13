namespace Networker.Update.Contracts.Scheduling;

public static class UpdateSchedulePolicy
{
    public static readonly TimeSpan SuccessInterval = TimeSpan.FromHours(24);
    public static readonly TimeSpan MetadataDeadline = TimeSpan.FromSeconds(2);

    public static TimeSpan Backoff(int failureCount) => failureCount switch
    {
        <= 0 => TimeSpan.Zero,
        1 => TimeSpan.FromMinutes(15),
        2 => TimeSpan.FromHours(1),
        3 => TimeSpan.FromHours(6),
        _ => TimeSpan.FromHours(24),
    };

    public static DateTimeOffset ComputeNextCheck(
        DateTimeOffset now,
        bool succeeded,
        int failureCount,
        DateTimeOffset? retryAfterUtc = null)
    {
        DateTimeOffset next = now + (succeeded ? SuccessInterval : Backoff(failureCount));
        return retryAfterUtc is { } retry && retry > next ? retry : next;
    }

    public static bool IsDue(DateTimeOffset now, DateTimeOffset? nextCheckUtc)
        => nextCheckUtc is null || now >= nextCheckUtc;
}
