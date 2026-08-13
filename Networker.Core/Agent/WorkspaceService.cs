using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Networker.Core.Agent;

public sealed record WorkspaceEntry(string Path, bool IsDirectory, long Length, bool IsDenied = false);

public sealed class WorkspaceService : IDisposable
{
    private static readonly HashSet<string> DosDevices = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CLOCK$", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    private readonly string _root;
    private readonly string _rootPrefix;
    private readonly SafeFileHandle? _rootHandle;
    private readonly FileIdentity _rootIdentity;
    private readonly string _rootFinal;
    private readonly string _rootFinalPrefix;

    public WorkspaceService(string root, IEnumerable<string>? protectedRoots = null)
    {
        if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("A workspace root is required.", nameof(root));
        _root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!Directory.Exists(_root)) throw new DirectoryNotFoundException($"Workspace does not exist: {_root}");
        if (Path.GetPathRoot(_root)?.TrimEnd(Path.DirectorySeparatorChar).Equals(_root, StringComparison.OrdinalIgnoreCase) == true)
            throw new UnauthorizedAccessException("A filesystem root cannot be used as an agent workspace.");
        RejectReparsePoint(_root);
        _rootPrefix = _root + Path.DirectorySeparatorChar;
        if (OperatingSystem.IsWindows())
        {
            _rootHandle = OpenPath(_root, directory: true);
            _rootIdentity = Identity(_rootHandle);
            _rootFinal = FinalPath(_rootHandle);
        }
        else _rootFinal = _root;
        _rootFinalPrefix = _rootFinal + Path.DirectorySeparatorChar;
        foreach (string protectedRoot in protectedRoots ?? Array.Empty<string>()) RejectOverlap(_rootFinal, ResolveExistingFinalPath(protectedRoot));
    }

    public string Root => _root;

    public IReadOnlyList<WorkspaceEntry> List(string relativePath = "", int maximumEntries = 500)
    {
        VerifyRoot();
        string directory = Resolve(relativePath, mustExist: true);
        if (!Directory.Exists(directory)) throw new InvalidOperationException("The requested path is not a directory.");
        var entries = new List<WorkspaceEntry>();
        foreach (string path in Directory.EnumerateFileSystemEntries(directory).Take(Math.Clamp(maximumEntries, 1, 2000)))
        {
            FileAttributes attributes = File.GetAttributes(path);
            bool denied = (attributes & FileAttributes.ReparsePoint) != 0;
            entries.Add(new WorkspaceEntry(Path.GetRelativePath(_root, path), (attributes & FileAttributes.Directory) != 0,
                !denied && File.Exists(path) ? new FileInfo(path).Length : 0, denied));
        }
        return entries;
    }

    public string ReadText(string relativePath, int maximumCharacters = 131_072)
    {
        VerifyRoot();
        string path = Resolve(relativePath, mustExist: true);
        if (!File.Exists(path)) throw new FileNotFoundException("Workspace file not found.", relativePath);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        VerifyOpenedPath(stream.SafeFileHandle, path);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        char[] buffer = new char[Math.Clamp(maximumCharacters, 1, 1_048_576) + 1];
        int count = reader.ReadBlock(buffer, 0, buffer.Length);
        if (count == buffer.Length) throw new InvalidOperationException("File exceeds the agent read limit.");
        return new string(buffer, 0, count);
    }

    public void WriteText(string relativePath, string content)
    {
        VerifyRoot();
        if (content.Length > 1_048_576) throw new InvalidOperationException("File exceeds the agent write limit.");
        string path = Resolve(relativePath, mustExist: false);
        string parent = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Invalid workspace file path.");
        Directory.CreateDirectory(parent);
        ValidateExistingSegments(parent);
        VerifyRoot();
        string temp = Path.Combine(parent, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.networker.tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(content);
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
                VerifyOpenedPath(stream.SafeFileHandle, temp);
            }
            VerifyRoot();
            ValidateExistingSegments(parent);
            if (File.Exists(path)) RejectReparsePoint(path);
            File.Move(temp, path, overwrite: true);
        }
        finally { try { File.Delete(temp); } catch { } }
    }

    public void DeleteFile(string relativePath)
    {
        VerifyRoot();
        string path = Resolve(relativePath, mustExist: true);
        if (!File.Exists(path)) throw new InvalidOperationException("Only files can be deleted by this tool.");
        using (SafeFileHandle handle = OpenPath(path, directory: false)) VerifyOpenedPath(handle, path);
        VerifyRoot();
        File.Delete(path);
    }

    public string Resolve(string relativePath, bool mustExist)
    {
        VerifyRoot();
        ValidateRelativePath(relativePath ?? string.Empty, allowEmpty: true);
        string full = Path.GetFullPath(Path.Combine(_root, relativePath ?? string.Empty));
        if (!full.Equals(_root, StringComparison.OrdinalIgnoreCase) && !full.StartsWith(_rootPrefix, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Path escapes the selected workspace.");
        ValidateExistingSegments(full);
        if (mustExist && !File.Exists(full) && !Directory.Exists(full)) throw new FileNotFoundException("Workspace path not found.", relativePath);
        return full;
    }

    public static void ValidateRelativePath(string path, bool allowEmpty = false)
    {
        if (path.Length == 0) { if (allowEmpty) return; throw new UnauthorizedAccessException("A relative path is required."); }
        if (path.Any(character => character == '\0' || char.IsControl(character)) || Path.IsPathRooted(path) || path.StartsWith("\\", StringComparison.Ordinal) || path.Contains(':'))
            throw new UnauthorizedAccessException("Agent paths must be plain relative workspace paths.");
        foreach (string component in path.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (component is "." or ".." || component.EndsWith(' ') || component.EndsWith('.'))
                throw new UnauthorizedAccessException("Agent path contains an ambiguous component.");
            string deviceStem = component.Split('.')[0];
            if (DosDevices.Contains(deviceStem)) throw new UnauthorizedAccessException("DOS device names are not valid agent paths.");
        }
    }

    public void Dispose() => _rootHandle?.Dispose();

    private void ValidateExistingSegments(string fullPath)
    {
        string current = _root;
        string relative = Path.GetRelativePath(_root, fullPath);
        if (relative == ".") return;
        foreach (string segment in relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!File.Exists(current) && !Directory.Exists(current)) break;
            RejectReparsePoint(current);
        }
    }

    private void VerifyRoot()
    {
        if (_rootHandle is null) { if (!Directory.Exists(_root)) throw new UnauthorizedAccessException("The selected workspace no longer exists."); return; }
        using SafeFileHandle current = OpenPath(_root, directory: true);
        if (Identity(current) != _rootIdentity) throw new UnauthorizedAccessException("The selected workspace was replaced or redirected.");
    }

    private void VerifyOpenedPath(SafeFileHandle handle, string expected)
    {
        if (!OperatingSystem.IsWindows()) return;
        string final = FinalPath(handle);
        if (!final.Equals(_rootFinal, StringComparison.OrdinalIgnoreCase) && !final.StartsWith(_rootFinalPrefix, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Opened object escapes the selected workspace.");
    }

    private static void RejectOverlap(string root, string protectedRoot)
    {
        if (string.IsNullOrWhiteSpace(protectedRoot)) return;
        string protectedFull = Path.GetFullPath(protectedRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (IsWithin(root, protectedFull) || IsWithin(protectedFull, root)) throw new UnauthorizedAccessException("Agent workspace cannot overlap Networker application data or installation files.");
    }

    private static bool IsWithin(string candidate, string root) => candidate.Equals(root, StringComparison.OrdinalIgnoreCase) || candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static string ResolveExistingFinalPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!OperatingSystem.IsWindows() || (!Directory.Exists(full) && !File.Exists(full))) return full;
        using SafeFileHandle handle = OpenPath(full, Directory.Exists(full));
        return FinalPath(handle);
    }
    private static void RejectReparsePoint(string path) { if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new UnauthorizedAccessException("Workspace reparse points are not accessible to the agent."); }

    private static SafeFileHandle OpenPath(string path, bool directory)
    {
        SafeFileHandle handle = CreateFile(path, 0, 7, IntPtr.Zero, 3, directory ? 0x02200000u : 0x00200000u, IntPtr.Zero);
        if (handle.IsInvalid) throw new IOException($"Unable to validate workspace path '{path}'.", Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error()));
        return handle;
    }

    private static FileIdentity Identity(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out BY_HANDLE_FILE_INFORMATION info)) throw new IOException("Unable to read workspace identity.");
        return new FileIdentity(info.VolumeSerialNumber, ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow);
    }

    private static string FinalPath(SafeFileHandle handle)
    {
        var buffer = new StringBuilder(1024);
        uint length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
        if (length == 0 || length >= buffer.Capacity) throw new IOException("Unable to resolve workspace object path.");
        string value = buffer.ToString();
        return value.StartsWith("\\\\?\\UNC\\", StringComparison.OrdinalIgnoreCase) ? "\\\\" + value[8..]
            : value.StartsWith("\\\\?\\", StringComparison.OrdinalIgnoreCase) ? value[4..] : value;
    }

    private readonly record struct FileIdentity(uint Volume, ulong Index);
    [StructLayout(LayoutKind.Sequential)] private struct BY_HANDLE_FILE_INFORMATION { public uint FileAttributes; public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime, LastAccessTime, LastWriteTime; public uint VolumeSerialNumber, FileSizeHigh, FileSizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetFileInformationByHandle(SafeFileHandle file, out BY_HANDLE_FILE_INFORMATION info);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint GetFinalPathNameByHandle(SafeFileHandle file, StringBuilder path, uint length, uint flags);
}
