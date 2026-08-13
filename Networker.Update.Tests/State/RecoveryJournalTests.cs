using Networker.Update.Contracts.State;

namespace Networker.Update.Tests.State;

public sealed class RecoveryJournalTests
{
    [Fact]
    public void RecoveryPhasesPreservePriorSlot()
    {
        var journal = new RecoveryJournal(1, "app-a", "app-b", "1.0.0", "1.1.0", "update.zip", RecoveryPhase.Applying, 0, DateTimeOffset.UtcNow);
        Assert.Equal("app-a", journal.PreviousSlot);
        Assert.Equal("app-b", journal.TargetSlot);
        Assert.Equal(RecoveryPhase.AwaitingHealth, (journal with { Phase = RecoveryPhase.AwaitingHealth }).Phase);
    }
}
