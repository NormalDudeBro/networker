using Networker.Update.Contracts.State;

namespace Networker.Update.Tests.State;

public sealed class LauncherStateStoreTests : IDisposable
{
    private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "networker-launcher-state-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void RoundTripsAndRecoversFromCorruption()
    {
        string path = System.IO.Path.Combine(_directory, "state.json");
        var store = new LauncherStateStore(path);
        store.Write(LauncherState.Default with { FailureCount = 3, ETag = "abc" });
        Assert.Equal(3, store.Read().FailureCount);

        File.WriteAllText(path, "not-json");
        Assert.Equal(0, store.Read().FailureCount);
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch { }
    }
}
