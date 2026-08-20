using System.Text;

namespace Networker.Core.Agent;

public sealed record AgentFileEntry(string Path, bool IsDirectory, long Length);

/// <summary>Bounded global filesystem operations executed as the current user.</summary>
public sealed class AgentFileSystem
{
    private static readonly string DefaultDirectory = ResolveDefaultDirectory();

    public IReadOnlyList<AgentFileEntry> List(string path = "", int maximumEntries = 500)
    {
        string directory = ResolvePath(path);
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException($"Directory not found: {directory}");

        var entries = new List<AgentFileEntry>();
        foreach (string entry in Directory.EnumerateFileSystemEntries(directory).Take(Math.Clamp(maximumEntries, 1, 2000)))
        {
            FileAttributes attributes = File.GetAttributes(entry);
            bool isDirectory = (attributes & FileAttributes.Directory) != 0;
            entries.Add(new AgentFileEntry(entry, isDirectory, !isDirectory && File.Exists(entry) ? new FileInfo(entry).Length : 0));
        }
        return entries;
    }

    public string ReadText(string path, int maximumCharacters = 131_072)
    {
        string fullPath = ResolvePath(path);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("File not found.", fullPath);
        using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        char[] buffer = new char[Math.Clamp(maximumCharacters, 1, 1_048_576) + 1];
        int count = reader.ReadBlock(buffer, 0, buffer.Length);
        if (count == buffer.Length) throw new InvalidOperationException("File exceeds the agent read limit.");
        return new string(buffer, 0, count);
    }

    public void WriteText(string path, string content)
    {
        if (content.Length > 1_048_576) throw new InvalidOperationException("File exceeds the agent write limit.");
        string fullPath = ResolvePath(path);
        string parent = Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException("Invalid file path.");
        Directory.CreateDirectory(parent);
        string temp = Path.Combine(parent, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.networker.tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(content);
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, fullPath, overwrite: true);
        }
        finally
        {
            try { File.Delete(temp); } catch { }
        }
    }

    public void DeleteFile(string path)
    {
        string fullPath = ResolvePath(path);
        if (!File.Exists(fullPath)) throw new InvalidOperationException("Only existing files can be deleted by this tool.");
        File.Delete(fullPath);
    }

    public static string ResolvePath(string path)
    {
        if (path.Any(character => character == '\0' || char.IsControl(character)))
            throw new InvalidOperationException("Path contains invalid control characters.");
        return Path.GetFullPath(string.IsNullOrWhiteSpace(path) ? DefaultDirectory
            : Path.IsPathRooted(path) ? path : Path.Combine(DefaultDirectory, path));
    }

    public static string GetDefaultDirectory() => DefaultDirectory;

    private static string ResolveDefaultDirectory()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Directory.Exists(profile) ? profile : Environment.CurrentDirectory;
    }
}
