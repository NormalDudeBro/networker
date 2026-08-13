using Networker.Update.Contracts.Scheduling;

namespace Networker.Update.Tests.Scheduling;

public sealed class UpdateSchedulePolicyTests
{
    [Fact]
    public void UsesTwoSecondMetadataDeadline() => Assert.Equal(TimeSpan.FromSeconds(2), UpdateSchedulePolicy.MetadataDeadline);

    [Theory]
    [InlineData(1, 15)]
    [InlineData(2, 60)]
    [InlineData(3, 360)]
    [InlineData(4, 1440)]
    public void BacksOffFailures(int failures, int minutes)
        => Assert.Equal(TimeSpan.FromMinutes(minutes), UpdateSchedulePolicy.Backoff(failures));
}
