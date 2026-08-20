using Networker.Core.Agent;

namespace Networker.Core.Tests.Agent;

public sealed class AgentFileSystemTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "networker-agent-files", Guid.NewGuid().ToString("N"));
    private readonly AgentFileSystem _files = new();

    public AgentFileSystemTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void ReadWriteListDelete_RoundTripsAbsolutePath()
    {
        string file = Path.Combine(_root, "src", "test.txt");
        _files.WriteText(file, "hello");

        Assert.Equal("hello", _files.ReadText(file));
        Assert.Contains(_files.List(Path.GetDirectoryName(file)!), entry => entry.Path == file);
        _files.DeleteFile(file);
        Assert.False(File.Exists(file));
    }

    [Fact]
    public void ResolvePath_UsesUserProfileForRelativePaths()
    {
        string resolved = AgentFileSystem.ResolvePath(Path.Combine("folder", "file.txt"));
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.StartsWith(Path.GetFullPath(profile) + Path.DirectorySeparatorChar, resolved, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WriteText_ReplacesAtomicallyAndEnforcesLimit()
    {
        string file = Path.Combine(_root, "replace.txt");
        _files.WriteText(file, "first");
        _files.WriteText(file, "second");
        Assert.Equal("second", File.ReadAllText(file));
        Assert.Throws<InvalidOperationException>(() => _files.WriteText(file, new string('x', 1_048_577)));
    }

    [Fact]
    public void DeleteFile_RejectsDirectories()
        => Assert.Throws<InvalidOperationException>(() => _files.DeleteFile(_root));

    [Fact]
    public async Task CommandRunner_AllowsShellsAndAbsoluteExecutablePaths()
    {
        if (!OperatingSystem.IsWindows()) return;
        var runner = new CommandRunner();
        string cmd = Path.Combine(Environment.SystemDirectory, "where.exe");
        AgentCommandResult result = await runner.RunAsync(new AgentCommand(cmd, new[] { "cmd.exe" }, 30));
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("cmd.exe", result.StandardOutput, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
