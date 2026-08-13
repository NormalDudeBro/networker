using Networker.Core.Agent;

namespace Networker.Core.Tests.Agent;

public sealed class WorkspaceServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "networker-agent-tests", Guid.NewGuid().ToString("N"));

    public WorkspaceServiceTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void ReadWriteListDelete_RoundTripsWithinWorkspace()
    {
        using var workspace = new WorkspaceService(_root);
        workspace.WriteText("src/test.txt", "hello");

        Assert.Equal("hello", workspace.ReadText("src/test.txt"));
        Assert.Contains(workspace.List("src"), entry => entry.Path == Path.Combine("src", "test.txt"));
        workspace.DeleteFile("src/test.txt");
        Assert.False(File.Exists(Path.Combine(_root, "src", "test.txt")));
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("C:\\outside.txt")]
    [InlineData("\\\\server\\share\\file.txt")]
    [InlineData("file.txt:secret")]
    [InlineData("C:drive-relative.txt")]
    [InlineData("\\?\\C:\\device.txt")]
    [InlineData("CON.txt")]
    [InlineData("folder.\\file.txt")]
    [InlineData("folder \\file.txt")]
    [InlineData("a\\..\\file.txt")]
    public void Resolve_RejectsEscapeAndAds(string path)
    {
        using var workspace = new WorkspaceService(_root);
        Assert.ThrowsAny<Exception>(() => workspace.Resolve(path, mustExist: false));
    }

    [Fact]
    public void Constructor_RejectsProtectedRootOverlap()
    {
        Assert.Throws<UnauthorizedAccessException>(() => new WorkspaceService(_root, new[] { Path.Combine(_root, "data") }));
        string child = Path.Combine(_root, "child");
        Directory.CreateDirectory(child);
        Assert.Throws<UnauthorizedAccessException>(() => new WorkspaceService(child, new[] { _root }));
    }

    [Fact]
    public void CommandPolicy_RejectsShellsAndExecutablePaths()
    {
        var policy = new CommandPolicy();
        Assert.Throws<UnauthorizedAccessException>(() => policy.Validate(new AgentCommand("cmd.exe", new[] { "/c", "echo hi" })));
        Assert.Throws<UnauthorizedAccessException>(() => policy.Validate(new AgentCommand("C:\\tools\\dotnet.exe", new[] { "test" })));
        Assert.Throws<UnauthorizedAccessException>(() => policy.Validate(new AgentCommand("powershell", new[] { "-Command", "Get-ChildItem" })));
        Assert.Throws<InvalidOperationException>(() => policy.Validate(new AgentCommand("dotnet", new[] { "test", "&&", "whoami" })));
        Assert.Throws<UnauthorizedAccessException>(() => policy.Validate(new AgentCommand("dotnet", new[] { "test", "C:\\outside.csproj" })));
        Assert.Throws<UnauthorizedAccessException>(() => policy.Validate(new AgentCommand("git", new[] { "config", "user.name", "bad" })));
        policy.Validate(new AgentCommand("dotnet", new[] { "test" }));
    }

    [Fact]
    public async Task CommandRunner_AssignsAndRunsApprovedExecutable()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var workspace = new WorkspaceService(_root);
        var runner = new CommandRunner(workspace);
        AgentCommandResult result = await runner.RunAsync(new AgentCommand("dotnet", new[] { "--version" }, TimeoutSeconds: 30));
        Assert.Equal(0, result.ExitCode);
        Assert.Matches(@"\d+\.\d+", result.StandardOutput);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
