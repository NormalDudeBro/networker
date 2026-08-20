using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Networker.Core.Terminal;

/// <summary>
/// A long-lived interactive child-shell session backed by anonymous pipes.
/// Commands are sent as lines through <see cref="WriteLine"/>; incremental output is
/// surfaced through <see cref="OutputReceived"/> and naturally ends with
/// <see cref="ProcessExited"/> when the shell exits on its own.
///
/// This is deliberately NOT subject to <see cref="Networker.Core.Agent.CommandPolicy"/>:
/// it is the user's own interactive terminal, not an agent tool.
///
/// Scope boundary: this is a line-oriented pipe terminal (like a cmd box). It does not
/// emulate a PTY, so cursor-addressable full-screen apps do not render correctly, and
/// <c>Resize</c> is intentionally a no-op.
/// </summary>
public sealed class TerminalSession : IDisposable
{
    /// <summary>Raised on a background thread as chunks of shell output arrive.</summary>
    public event Action<string>? OutputReceived;

    /// <summary>Raised when the shell exits on its own (not via <see cref="Stop"/>).</summary>
    public event Action? ProcessExited;

    private readonly object _gate = new();
    private Process? _process;
    private WindowsJob? _job;
    private FileStream? _stdinStream;
    private SafeFileHandle? _stdoutRead;
    private SafeFileHandle? _stderrRead;
    private int _finishedReaders;

    public TerminalSession() { }

    public bool IsRunning
    {
        get { lock (_gate) return _process is not null && !_process.HasExited; }
    }

    public string ShellPath { get; private set; } = string.Empty;

    /// <summary>Starts the interactive shell from the current user's profile.</summary>
    public void Start(string shell = "cmd.exe")
    {
        lock (_gate)
        {
            if (_process is not null) throw new InvalidOperationException("Terminal session is already running.");
        }

        string shellPath = ResolveShell(shell);
        string startDirectory = ResolveStartDirectory();

        var stdoutPipe = AnonymousPipe.Create(childReads: false);
        var stderrPipe = AnonymousPipe.Create(childReads: false);
        var stdinPipe = AnonymousPipe.Create(childReads: true);
        var job = WindowsJob.Create();

        PROCESS_INFORMATION processInfo;
        try
        {
            var startup = new STARTUPINFO
            {
                cb = Marshal.SizeOf<STARTUPINFO>(),
                dwFlags = 0x00000100, // STARTF_USESTDHANDLES
                hStdInput = stdinPipe.Read.DangerousGetHandle(),
                hStdOutput = stdoutPipe.Write.DangerousGetHandle(),
                hStdError = stderrPipe.Write.DangerousGetHandle(),
            };

            IntPtr environment = BuildEnvironmentBlock();
            try
            {
                if (!CreateProcess(shellPath, new StringBuilder(Quote(shellPath)), IntPtr.Zero, IntPtr.Zero, true,
                        0x00000004 | 0x00000400 | 0x08000000, // CREATE_SUSPENDED | CREATE_UNICODE_ENVIRONMENT | CREATE_NO_WINDOW
                        environment, startDirectory, ref startup, out processInfo))
                    throw new IOException("Unable to start the terminal shell.", Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error()));
            }
            finally { Marshal.FreeHGlobal(environment); }

            using var processHandle = new SafeFileHandle(processInfo.hProcess, ownsHandle: true);
            using var threadHandle = new SafeFileHandle(processInfo.hThread, ownsHandle: true);
            job.Assign(processHandle);

            // Hand the child its pipe ends; the parent ends are retained by the session.
            stdoutPipe.Write.Dispose();
            stderrPipe.Write.Dispose();
            stdinPipe.Read.Dispose();

            if (ResumeThread(threadHandle) == uint.MaxValue)
                throw new IOException("Unable to resume the terminal shell.");

            lock (_gate)
            {
                _process = Process.GetProcessById((int)processInfo.dwProcessId);
                _job = job;
                _stdinStream = new FileStream(stdinPipe.Write, FileAccess.Write, 4096, isAsync: false);
                _stdoutRead = stdoutPipe.Read;
                _stderrRead = stderrPipe.Read;
                _finishedReaders = 0;
            }
            ShellPath = shellPath;

            // Ownership transferred to the session.
            job = null!;
            stdoutPipe = null!;
            stderrPipe = null!;
            stdinPipe = null!;
        }
        catch
        {
            job.Dispose();
            stdoutPipe.Dispose();
            stderrPipe.Dispose();
            stdinPipe.Dispose();
            throw;
        }

        _ = Task.Run(() => PumpReader(_stdoutRead));
        _ = Task.Run(() => PumpReader(_stderrRead));
    }

    /// <summary>Sends a single command line to the shell. Writes to a dead session are ignored.</summary>
    public void WriteLine(string line)
    {
        FileStream? stdin;
        bool running;
        lock (_gate)
        {
            stdin = _stdinStream;
            running = _process is not null && !_process.HasExited;
        }
        if (stdin is null || !running) return;

        string normalized = line.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal);
        byte[] bytes = OemEncoding().GetBytes(normalized + "\r\n");
        try
        {
            lock (_gate)
            {
                // FileStream buffers small writes; a buffered command would never reach the shell.
                stdin.Write(bytes, 0, bytes.Length);
                stdin.Flush();
            }
        }
        catch (IOException) { } // shell died between the check and the write
        catch (ObjectDisposedException) { }
    }

    /// <summary>Terminates the shell and its whole process tree. Raises no <see cref="ProcessExited"/>.</summary>
    public void Stop()
    {
        Process? process;
        WindowsJob? job;
        FileStream? stdin;
        lock (_gate)
        {
            if (_process is null) return;
            process = _process;
            job = _job;
            stdin = _stdinStream;
            _process = null;
            _job = null;
            _stdinStream = null;
        }
        Teardown(process, job, stdin);
    }

    public void Restart(string shell = "cmd.exe")
    {
        Stop();
        Start(shell);
    }

    public void Dispose() => Stop();

    // ---- internals ----------------------------------------------------------

    private void PumpReader(SafeFileHandle? handle)
    {
        if (handle is null) { OnReaderEnded(); return; }
        var builder = new StringBuilder();
        var buffer = new char[4096];
        try
        {
            using var stream = new FileStream(handle, FileAccess.Read, 4096, isAsync: false);
            using var reader = new StreamReader(stream, OemEncoding(), detectEncodingFromByteOrderMarks: true);
            int read;
            while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
            {
                builder.Append(buffer, 0, read);
                if (builder.Length > 0)
                {
                    string chunk = builder.ToString();
                    builder.Clear();
                    OutputReceived?.Invoke(chunk);
                }
            }
        }
        catch (ObjectDisposedException) { }
        catch (IOException) { } // pipe closed during Stop
        finally { OnReaderEnded(); }
    }

    private void OnReaderEnded()
    {
        bool raiseExit;
        lock (_gate)
        {
            _finishedReaders++;
            raiseExit = _finishedReaders >= 2 && _process is not null;
        }
        if (!raiseExit) return;

        Process? process;
        WindowsJob? job;
        FileStream? stdin;
        lock (_gate)
        {
            process = _process;
            job = _job;
            stdin = _stdinStream;
            _process = null;
            _job = null;
            _stdinStream = null;
        }
        Teardown(process, job, stdin);
        ProcessExited?.Invoke();
    }

    private static void Teardown(Process? process, WindowsJob? job, FileStream? stdin)
    {
        try { job?.Terminate(); } catch { }
        try { process?.WaitForExit(1500); } catch { }
        try { stdin?.Dispose(); } catch { }
        try { job?.Dispose(); } catch { }
    }

    private static Encoding OemEncoding()
    {
        try
        {
            return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage,
                EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback);
        }
        catch
        {
            return Encoding.UTF8;
        }
    }

    private static string ResolveStartDirectory()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Directory.Exists(profile) ? profile : Environment.CurrentDirectory;
    }

    private static string ResolveShell(string shell)
    {
        if (shell.IndexOfAny(new[] { '/', '\\', ':' }) >= 0)
        {
            string full = Path.GetFullPath(shell);
            if (!File.Exists(full)) throw new FileNotFoundException($"Terminal shell not found: {full}");
            return full;
        }
        string fileName = Path.HasExtension(shell) ? shell : shell + ".exe";
        foreach (string folder in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                string candidate = Path.GetFullPath(Path.Combine(folder.Trim('"'), fileName));
                if (File.Exists(candidate)) return candidate;
            }
            catch { }
        }
        throw new FileNotFoundException($"Terminal shell '{shell}' was not found on PATH.");
    }

    private static IntPtr BuildEnvironmentBlock()
    {
        string[] names = { "SystemRoot", "WINDIR", "PATH", "TEMP", "TMP", "USERPROFILE", "HOMEDRIVE", "HOMEPATH", "LOCALAPPDATA", "APPDATA", "ProgramFiles", "ProgramFiles(x86)", "ProgramData" };
        string block = string.Join('\0', names.Select(name => (Name: name, Value: Environment.GetEnvironmentVariable(name)))
            .Where(pair => !string.IsNullOrEmpty(pair.Value)).OrderBy(pair => pair.Name, StringComparer.OrdinalIgnoreCase)
            .Select(pair => pair.Name + "=" + pair.Value)) + "\0\0";
        return Marshal.StringToHGlobalUni(block);
    }

    private static string Quote(string value)
    {
        if (value.Length == 0) return "\"\"";
        var result = new StringBuilder("\""); int slashes = 0;
        foreach (char character in value)
        {
            if (character == '\\') { slashes++; continue; }
            if (character == '"') result.Append('\\', slashes * 2 + 1).Append('"');
            else { result.Append('\\', slashes).Append(character); }
            slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }

    private sealed class AnonymousPipe : IDisposable
    {
        private AnonymousPipe(SafeFileHandle read, SafeFileHandle write) { Read = read; Write = write; }
        public SafeFileHandle Read { get; }
        public SafeFileHandle Write { get; }
        public static AnonymousPipe Create(bool childReads)
        {
            var security = new SECURITY_ATTRIBUTES { nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(), bInheritHandle = true };
            if (!CreatePipe(out SafeFileHandle read, out SafeFileHandle write, ref security, 0)) throw new IOException("Unable to create terminal pipe.");
            SafeFileHandle parentEnd = childReads ? write : read;
            if (!SetHandleInformation(parentEnd, 1, 0)) { read.Dispose(); write.Dispose(); throw new IOException("Unable to secure terminal pipe."); }
            return new AnonymousPipe(read, write);
        }
        public void Dispose() { Read.Dispose(); Write.Dispose(); }
    }

    private sealed class WindowsJob : IDisposable
    {
        private readonly SafeFileHandle _handle;
        private WindowsJob(SafeFileHandle handle) => _handle = handle;
        public static WindowsJob Create()
        {
            var handle = new SafeFileHandle(CreateJobObject(IntPtr.Zero, null), true);
            if (handle.IsInvalid) throw new IOException("Unable to create terminal Job Object.");
            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            info.BasicLimitInformation.LimitFlags = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            int length = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
            IntPtr pointer = Marshal.AllocHGlobal(length);
            try
            {
                Marshal.StructureToPtr(info, pointer, false);
                if (!SetInformationJobObject(handle, 9, pointer, (uint)length)) throw new IOException("Unable to configure terminal Job Object.");
            }
            catch { handle.Dispose(); throw; }
            finally { Marshal.FreeHGlobal(pointer); }
            return new WindowsJob(handle);
        }
        public void Assign(SafeFileHandle process) { if (!AssignProcessToJobObject(_handle, process)) throw new IOException("Unable to assign terminal to its Job Object; execution was denied."); }
        public void Terminate() { if (!_handle.IsInvalid && !_handle.IsClosed) TerminateJobObject(_handle, 1); }
        public void Dispose() => _handle.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)] private struct SECURITY_ATTRIBUTES { public int nLength; public IntPtr lpSecurityDescriptor; [MarshalAs(UnmanagedType.Bool)] public bool bInheritHandle; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct STARTUPINFO { public int cb; public string? lpReserved, lpDesktop, lpTitle; public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags; public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError; }
    [StructLayout(LayoutKind.Sequential)] private struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public uint dwProcessId, dwThreadId; }
    [StructLayout(LayoutKind.Sequential)] private struct JOBOBJECT_BASIC_LIMIT_INFORMATION { public long PerProcessUserTimeLimit, PerJobUserTimeLimit; public uint LimitFlags; public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize; public uint ActiveProcessLimit; public UIntPtr Affinity; public uint PriorityClass, SchedulingClass; }
    [StructLayout(LayoutKind.Sequential)] private struct IO_COUNTERS { public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount; }
    [StructLayout(LayoutKind.Sequential)] private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION { public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation; public IO_COUNTERS IoInfo; public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed; }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write, ref SECURITY_ATTRIBUTES attributes, uint size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetHandleInformation(SafeFileHandle handle, uint mask, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CreateProcess(string application, StringBuilder commandLine, IntPtr processAttributes, IntPtr threadAttributes, bool inheritHandles, uint creationFlags, IntPtr environment, string currentDirectory, ref STARTUPINFO startup, out PROCESS_INFORMATION information);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(SafeFileHandle thread);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetInformationJobObject(SafeFileHandle job, int infoClass, IntPtr info, uint length);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AssignProcessToJobObject(SafeFileHandle job, SafeFileHandle process);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool TerminateJobObject(SafeFileHandle job, uint exitCode);
}
