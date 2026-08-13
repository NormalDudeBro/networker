using System.Text;

namespace Networker.Core.Updates;

/// <summary>
/// Bounded, best-effort file logging with rotation: the primary log grows to
/// <c>maxBytes</c>, then rotates to a single backup file. Every method swallows
/// its own I/O errors so logging can never break an update operation. The WinUI
/// logger supplies the application-data path and message formatting.
/// </summary>
public sealed class UpdateLogFile
{
    private readonly string _path;
    private readonly long _maxBytes;
    private readonly object _gate = new();

    public UpdateLogFile(string path, long maxBytes = 1024 * 1024)
    {
        _path = path;
        _maxBytes = Math.Max(maxBytes, 4096);
    }

    /// <summary>Appends a single line (newline appended) and rotates when full.</summary>
    public void AppendLine(string line)
    {
        try
        {
            lock (_gate)
            {
                string? directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string message = line + Environment.NewLine;
                if (File.Exists(_path) && new FileInfo(_path).Length + message.Length > _maxBytes)
                {
                    Rotate();
                }

                File.AppendAllText(_path, message);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Logging must never throw.
        }
    }

    /// <summary>Deletes the log files; used by tests and never throws.</summary>
    public void DeleteAll()
    {
        try
        {
            lock (_gate)
            {
                TryDelete(_path);
                TryDelete(BackupPath());
            }
        }
        catch (Exception)
        {
            // best effort
        }
    }

    private void Rotate()
    {
        string backup = BackupPath();
        TryDelete(backup);
        if (File.Exists(_path))
        {
            File.Move(_path, backup, overwrite: true);
        }
    }

    private string BackupPath() => _path + ".1";

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception)
        {
            // best effort
        }
    }
}
