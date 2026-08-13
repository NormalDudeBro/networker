using Networker.Core.Updates;

namespace Networker.Core.Tests.Updates;

public class UpdateSchedulerPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 15)]
    [InlineData(2, 60)]
    [InlineData(3, 360)]
    [InlineData(4, 1440)]
    [InlineData(10, 1440)]
    public void Backoff_FollowsExponentialLadder(int failureCount, int expectedMinutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), UpdateSchedulerPolicy.Backoff(failureCount));
    }

    [Fact]
    public void Backoff_ClampsNegativeToZero()
    {
        Assert.Equal(TimeSpan.Zero, UpdateSchedulerPolicy.Backoff(-5));
    }

    [Fact]
    public void ComputeNextCheck_Success_IsOneCadenceOut()
    {
        Assert.Equal(Now + TimeSpan.FromHours(24), UpdateSchedulerPolicy.ComputeNextCheck(Now, succeeded: true, failureCount: 0, retryAfterUtc: null));
    }

    [Fact]
    public void ComputeNextCheck_Failure_UsesBackoff()
    {
        Assert.Equal(Now + TimeSpan.FromMinutes(15), UpdateSchedulerPolicy.ComputeNextCheck(Now, succeeded: false, failureCount: 1, retryAfterUtc: null));
        Assert.Equal(Now + TimeSpan.FromHours(24), UpdateSchedulerPolicy.ComputeNextCheck(Now, succeeded: false, failureCount: 4, retryAfterUtc: null));
    }

    [Fact]
    public void ComputeNextCheck_Success_IgnoresFailureCount()
    {
        // A success resets the ladder regardless of prior failures.
        Assert.Equal(Now + TimeSpan.FromHours(24), UpdateSchedulerPolicy.ComputeNextCheck(Now, succeeded: true, failureCount: 5, retryAfterUtc: null));
    }

    [Fact]
    public void ComputeNextCheck_FutureRateLimitExtendsWait()
    {
        DateTimeOffset rateLimit = Now + TimeSpan.FromHours(48);
        DateTimeOffset next = UpdateSchedulerPolicy.ComputeNextCheck(Now, succeeded: true, failureCount: 0, retryAfterUtc: rateLimit);
        Assert.Equal(rateLimit, next);
    }

    [Fact]
    public void ComputeNextCheck_PastRateLimit_DoesNotShortenWait()
    {
        DateTimeOffset rateLimit = Now + TimeSpan.FromMinutes(1);
        DateTimeOffset next = UpdateSchedulerPolicy.ComputeNextCheck(Now, succeeded: false, failureCount: 4, retryAfterUtc: rateLimit);
        Assert.Equal(Now + TimeSpan.FromHours(24), next);
    }

    [Fact]
    public void ComputeNextCheck_RateLimitBeforeBackoff_KeepsBackoff()
    {
        DateTimeOffset rateLimit = Now + TimeSpan.FromMinutes(5);
        DateTimeOffset next = UpdateSchedulerPolicy.ComputeNextCheck(Now, succeeded: false, failureCount: 3, retryAfterUtc: rateLimit);
        Assert.Equal(Now + TimeSpan.FromHours(6), next);
    }

    [Fact]
    public void IsDue_True_WhenNeverChecked()
    {
        Assert.True(UpdateSchedulerPolicy.IsDue(Now, null));
    }

    [Fact]
    public void IsDue_True_WhenAtOrAfterNextCheck()
    {
        Assert.True(UpdateSchedulerPolicy.IsDue(Now, Now));
        Assert.True(UpdateSchedulerPolicy.IsDue(Now + TimeSpan.FromSeconds(1), Now));
    }

    [Fact]
    public void IsDue_False_BeforeNextCheck()
    {
        Assert.False(UpdateSchedulerPolicy.IsDue(Now, Now + TimeSpan.FromSeconds(1)));
    }
}
