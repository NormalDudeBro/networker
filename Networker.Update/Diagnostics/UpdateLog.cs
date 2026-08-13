namespace Networker.Update.Diagnostics;

public sealed class UpdateLog
{
    private const long MaxBytes = 1024 * 1024;
    private readonly string _path;
    private readonly object _gate = new();

    public UpdateLog(string? path = null)
    {
        _path = path ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Networker", "Logs", "launcher.log");
    }

    public void Info(string message) => Write("INF", message);
    public void Warn(string message) => Write("WRN", message);
    public void Error(string message, Exception? exception = null)
        => Write("ERR", exception is null ? message : $"{message} ({exception.GetType().Name})");

    private void Write(string level, string message)
    {
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
                string line = $"{DateTimeOffset.UtcNow:O} [{level}] {Sanitize(message)}{Environment.NewLine}";
                if (File.Exists(_path) && new FileInfo(_path).Length + line.Length > MaxBytes)
                {
                    File.Move(_path, _path + ".1", overwrite: true);
                }
                File.AppendAllText(_path, line);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static string Sanitize(string message)
    {
        string singleLine = message.Replace('\r', ' ').Replace('\n', ' ');
        int query = singleLine.IndexOf('?');
        string sanitized = query >= 0 ? singleLine[..query] + "?[redacted]" : singleLine;
        return sanitized[..Math.Min(sanitized.Length, 2000)];
    }
}
