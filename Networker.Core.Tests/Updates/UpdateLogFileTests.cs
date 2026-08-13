using Networker.Core.Updates;

namespace Networker.Core.Tests.Updates;

public class UpdateLogFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "NetworkerTests", "log-" + Guid.NewGuid().ToString("N"));
    private readonly string _path;

    public UpdateLogFileTests()
    {
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "updates.log");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void AppendLine_CreatesFile()
    {
        var log = new UpdateLogFile(_path);
        log.AppendLine("first entry");

        Assert.True(File.Exists(_path));
        Assert.Contains("first entry", File.ReadAllText(_path));
    }

    [Fact]
    public void AppendLine_CreatesMissingDirectories()
    {
        string nested = Path.Combine(_dir, "a", "b", "updates.log");
        new UpdateLogFile(nested).AppendLine("hello");

        Assert.True(File.Exists(nested));
    }

    [Fact]
    public void Rotation_MovesOldContentToBackup()
    {
        var log = new UpdateLogFile(_path, maxBytes: 4096);
        string longLine = new string('x', 1500);

        for (int i = 0; i < 4; i++)
        {
            log.AppendLine(longLine + i);
        }

        // 3rd append crosses the 4096 limit, so the first two lines rotate to .1.
        Assert.True(File.Exists(_path));
        Assert.True(File.Exists(_path + ".1"));
        Assert.Contains("x2", File.ReadAllText(_path));
        Assert.DoesNotContain("x0", File.ReadAllText(_path));
        Assert.Contains("x0", File.ReadAllText(_path + ".1"));
    }

    [Fact]
    public void Rotation_CapsAtMostOneBackup()
    {
        var log = new UpdateLogFile(_path, maxBytes: 4096);
        for (int i = 0; i < 20; i++)
        {
            log.AppendLine(new string('y', 1500) + i);
        }

        // Only one backup file ever exists.
        Assert.False(File.Exists(_path + ".1.1"));
        Assert.True(new FileInfo(_path).Length <= 4096);
    }

    [Fact]
    public void DeleteAll_RemovesLogAndBackup()
    {
        var log = new UpdateLogFile(_path, maxBytes: 4096);
        for (int i = 0; i < 4; i++)
        {
            log.AppendLine(new string('z', 1500) + i);
        }

        Assert.True(File.Exists(_path));
        Assert.True(File.Exists(_path + ".1"));

        log.DeleteAll();

        Assert.False(File.Exists(_path));
        Assert.False(File.Exists(_path + ".1"));
    }

    [Fact]
    public void DeleteAll_WhenNothingExists_DoesNotThrow()
    {
        var log = new UpdateLogFile(_path);
        log.DeleteAll();
    }
}
