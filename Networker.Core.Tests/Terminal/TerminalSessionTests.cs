using System.Text;
using Networker.Core.Terminal;

namespace Networker.Core.Tests.Terminal;

public class TerminalSessionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task Start_EchoesCommandOutput_ThroughOutputReceived()
    {
        using var session = new TerminalSession();
        string marker = "hello-from-terminal-" + Guid.NewGuid().ToString("N")[..8];
        session.Start();
        string output = await RunToMarkerAsync(session, marker, () => session.WriteLine($"echo {marker}"));
        Assert.Contains(marker, output, StringComparison.Ordinal);
        session.WriteLine("exit");
    }

    [Fact]
    public async Task StartsInUserProfileDirectory()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        using var session = new TerminalSession();
        session.Start();
        string output = await RunToMarkerAsync(session, profile, () => session.WriteLine("cd"));
        Assert.Contains(profile, output, StringComparison.OrdinalIgnoreCase);
        session.WriteLine("exit");
    }

    [Fact]
    public async Task RaisesProcessExited_WhenShellExitsOnItsOwn()
    {
        using var session = new TerminalSession();
        var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.ProcessExited += () => exited.TrySetResult(true);
        session.Start();
        session.WriteLine("exit");
        await exited.Task.WaitAsync(Timeout);
        Assert.False(session.IsRunning);
    }

    [Fact]
    public void Stop_TerminatesAndMarksNotRunning_WritesAreIgnored()
    {
        using var session = new TerminalSession();
        session.Start();
        Assert.True(session.IsRunning);
        session.Stop();
        Assert.False(session.IsRunning);
        // Writes to a dead session must not throw.
        session.WriteLine("echo after-stop");
        session.Stop();
    }

    [Fact]
    public void Start_WhenAlreadyRunning_Throws()
    {
        using var session = new TerminalSession();
        session.Start();
        try
        {
            Assert.Throws<InvalidOperationException>(() => session.Start());
        }
        finally
        {
            session.Stop();
        }
    }

    private static async Task<string> RunToMarkerAsync(TerminalSession session, string marker, Action sendCommand)
    {
        var buffer = new StringBuilder();
        var ready = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(string chunk)
        {
            lock (buffer)
            {
                buffer.Append(chunk);
                if (buffer.ToString().Contains(marker, StringComparison.Ordinal))
                    ready.TrySetResult(buffer.ToString());
            }
        }

        session.OutputReceived += Handler;
        try
        {
            sendCommand();
            return await ready.Task.WaitAsync(Timeout);
        }
        finally
        {
            session.OutputReceived -= Handler;
        }
    }
}
