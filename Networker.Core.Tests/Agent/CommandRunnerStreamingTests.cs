using Networker.Core.Agent;

namespace Networker.Core.Tests.Agent;

/// <summary>Synchronous chunk collector so report arrival timestamps are measured at report time.</summary>
internal sealed class CollectingProgress<T> : IProgress<T>
{
    public readonly List<(T Item, DateTimeOffset ArrivedAt)> Items = new();
    public TaskCompletionSource<(T Item, DateTimeOffset ArrivedAt)> First { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Report(T value)
    {
        var item = (value, DateTimeOffset.UtcNow);
        Items.Add(item);
        First.TrySetResult(item);
    }
}

public sealed class CommandRunnerStreamingTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task RunAsync_ReportsIncrementalLineChunks_BeforeCompletion()
    {
        if (!OperatingSystem.IsWindows()) return;
        var runner = new CommandRunner();
        var progress = new CollectingProgress<CommandOutputChunk>();

        Task<AgentCommandResult> run = runner.RunAsync(new AgentCommand("python", new[]
        {
            "-u", "-c",
            "import sys,time; sys.stdout.reconfigure(newline='\\n'); print('one', flush=True); time.sleep(0.5); print('two', flush=True)",
        }, TimeoutSeconds: 30), progress);
        (CommandOutputChunk FirstItem, DateTimeOffset FirstArrivedAt) = await progress.First.Task.WaitAsync(TestTimeout);
        Assert.False(run.IsCompleted, "The first output chunk must arrive while the command is still running.");
        AgentCommandResult result = await run;

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("one\ntwo\n", result.StandardOutput);
        Assert.True(progress.Items.Count >= 2, $"expected at least 2 chunks, got {progress.Items.Count}");
        Assert.Equal("one\n", progress.Items[0].Item.Text);
        Assert.Equal("two\n", progress.Items[^1].Item.Text);
        Assert.Equal(CommandOutputChannel.StdOut, progress.Items[0].Item.Channel);

        // The first line must have streamed while the process was still running,
        // i.e. well before RunAsync returned, not at process exit.
        Assert.Equal("one\n", FirstItem.Text);
    }

    [Fact]
    public async Task RunAsync_ChannelsStandardErrorSeparately()
    {
        if (!OperatingSystem.IsWindows()) return;
        var runner = new CommandRunner();
        var progress = new CollectingProgress<CommandOutputChunk>();

        AgentCommandResult result = await runner.RunAsync(new AgentCommand("python", new[]
        {
            "-u", "-c",
            "import sys; sys.stderr.reconfigure(newline='\\n'); print('err1', file=sys.stderr, flush=True)",
        }, TimeoutSeconds: 30), progress);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.StandardOutput);
        Assert.Equal("err1\n", result.StandardError);
        Assert.Single(progress.Items);
        Assert.Equal(CommandOutputChannel.StdErr, progress.Items[0].Item.Channel);
        Assert.Equal("err1\n", progress.Items[0].Item.Text);
    }

    [Fact]
    public async Task RunAsync_TruncatesOutputAtCap()
    {
        if (!OperatingSystem.IsWindows()) return;
        var runner = new CommandRunner();
        var progress = new CollectingProgress<CommandOutputChunk>();

        AgentCommandResult result = await runner.RunAsync(new AgentCommand("python", new[]
        {
            "-u", "-c",
            "import sys; sys.stdout.reconfigure(newline='\\n'); print(('x' * 1000 + '\\n') * 300, end='')",
        }, TimeoutSeconds: 30), progress);

        Assert.Equal(0, result.ExitCode);
        Assert.True(result.OutputTruncated);
        Assert.EndsWith("[output truncated]", result.StandardOutput);
        Assert.Equal(131_072 + "\n[output truncated]".Length, result.StandardOutput.Length);
        Assert.True(progress.Items.Count > 100, "oversized output should still stream as many chunks");
    }

}
