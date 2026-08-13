using Networker.Update.Contracts.State;

namespace Networker.Update.Tests.State;

public sealed class ActiveSlotStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "networker-slot-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void DefaultsToAAndAtomicallySwitchesToB()
    {
        var store = new ActiveSlotStore(_root);
        Assert.Equal("app-a", store.Read().ActiveSlot);
        store.Write(new ActiveSlotState { ActiveSlot = "app-b" });
        Assert.Equal("app-b", store.Read().ActiveSlot);
        Assert.Equal("app-b", File.ReadAllText(Path.Combine(_root, "active-slot.txt")).Trim());
    }

    [Fact]
    public void RejectsInvalidSlot()
    {
        var store = new ActiveSlotStore(_root);
        Assert.Throws<ArgumentException>(() => store.Write(new ActiveSlotState { ActiveSlot = "elsewhere" }));
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
