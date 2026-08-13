using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Networker.Core.Agent;

public sealed record AgentCommand(string Executable, IReadOnlyList<string> Arguments, string WorkingDirectory = "", int TimeoutSeconds = 120);
public sealed record AgentCommandResult(int ExitCode, string StandardOutput, string StandardError, bool TimedOut, bool OutputTruncated = false);

public sealed class CommandPolicy
{
    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    { "git", "dotnet", "node", "cargo", "go", "python", "python3", "pytest", "cmake", "ctest", "msbuild", "java", "javac", "gradle", "mvn" };

    private static readonly char[] ShellMetacharacters = { '&', '|', '<', '>', '`', '\r', '\n' };

    public void Validate(AgentCommand command)
    {
        string executable = command.Executable.Trim();
        if (executable.Length == 0 || executable.Contains('/') || executable.Contains('\\') || executable.Contains(':'))
            throw new UnauthorizedAccessException("Commands must use an approved executable name without a path.");
        string name = Path.GetFileNameWithoutExtension(executable);
        if (!Allowed.Contains(name)) throw new UnauthorizedAccessException($"Executable '{name}' is not allowed in Agent mode.");
        if (command.Arguments.Count > 100) throw new InvalidOperationException("Command has too many arguments.");
        foreach (string argument in command.Arguments)
        {
            if (argument.Length > 8192 || argument.IndexOf('\0') >= 0 || argument.IndexOfAny(ShellMetacharacters) >= 0 || argument.Contains("$(", StringComparison.Ordinal))
                throw new InvalidOperationException("Command contains a denied argument form.");
            if (Path.IsPathRooted(argument) || argument.StartsWith("\\\\", StringComparison.Ordinal) || argument.StartsWith("\\?\\", StringComparison.Ordinal))
                throw new UnauthorizedAccessException("Command arguments cannot name paths outside the workspace.");
        }
        if (name.Equals("git", StringComparison.OrdinalIgnoreCase) && command.Arguments.FirstOrDefault() is string verb &&
            verb is "config" or "credential" or "clean" or "reset")
            throw new UnauthorizedAccessException($"git {verb} is not allowed in Agent mode.");
    }
}

public sealed class CommandRunner
{
    private const int MaximumOutputCharacters = 131_072;
    private readonly WorkspaceService _workspace;
    private readonly CommandPolicy _policy;

    public CommandRunner(WorkspaceService workspace, CommandPolicy? policy = null) { _workspace = workspace; _policy = policy ?? new CommandPolicy(); }

    public async Task<AgentCommandResult> RunAsync(AgentCommand command, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Agent commands require the Windows Job Object boundary.");
        _policy.Validate(command);
        string workingDirectory = _workspace.Resolve(command.WorkingDirectory, mustExist: true);
        if (!Directory.Exists(workingDirectory)) throw new InvalidOperationException("Command working directory is not a directory.");
        string executable = ResolveExecutable(command.Executable);
        string commandLine = BuildCommandLine(executable, command.Arguments);

        using var stdout = AnonymousPipe.Create(childReads: false);
        using var stderr = AnonymousPipe.Create(childReads: false);
        using var input = AnonymousPipe.Create(childReads: true);
        using var job = WindowsJob.Create();
        var startup = new STARTUPINFO
        {
            cb = Marshal.SizeOf<STARTUPINFO>(), dwFlags = 0x00000100,
            hStdInput = input.Read.DangerousGetHandle(), hStdOutput = stdout.Write.DangerousGetHandle(), hStdError = stderr.Write.DangerousGetHandle()
        };

        IntPtr environment = BuildEnvironmentBlock();
        PROCESS_INFORMATION processInfo;
        try
        {
            if (!CreateProcess(executable, new StringBuilder(commandLine), IntPtr.Zero, IntPtr.Zero, true, 0x00000004 | 0x00000400 | 0x08000000,
                environment, workingDirectory, ref startup, out processInfo))
                throw new IOException("Unable to create the agent command process.", Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error()));
        }
        finally { Marshal.FreeHGlobal(environment); }

        using var processHandle = new SafeFileHandle(processInfo.hProcess, ownsHandle: true);
        using var threadHandle = new SafeFileHandle(processInfo.hThread, ownsHandle: true);
        try
        {
            job.Assign(processHandle);
            using Process process = Process.GetProcessById((int)processInfo.dwProcessId);
            stdout.Write.Dispose(); stderr.Write.Dispose(); input.Read.Dispose(); input.Write.Dispose();
            if (ResumeThread(threadHandle) == uint.MaxValue) throw new IOException("Unable to resume the assigned agent command.");

            using var stdoutReader = new StreamReader(new FileStream(stdout.Read, FileAccess.Read, 4096, isAsync: false), Encoding.UTF8, true);
            using var stderrReader = new StreamReader(new FileStream(stderr.Read, FileAccess.Read, 4096, isAsync: false), Encoding.UTF8, true);
            Task<BoundedOutput> stdoutTask = ReadBoundedAsync(stdoutReader);
            Task<BoundedOutput> stderrTask = ReadBoundedAsync(stderrReader);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(command.TimeoutSeconds, 1, 900)));
            bool timedOut = false;
            try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                timedOut = !cancellationToken.IsCancellationRequested;
                job.Terminate();
                await process.WaitForExitAsync().ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }
            BoundedOutput outValue = await stdoutTask.ConfigureAwait(false);
            BoundedOutput errorValue = await stderrTask.ConfigureAwait(false);
            return new AgentCommandResult(process.ExitCode, outValue.Text, errorValue.Text, timedOut, outValue.Truncated || errorValue.Truncated);
        }
        catch
        {
            job.Terminate();
            throw;
        }
    }

    private static async Task<BoundedOutput> ReadBoundedAsync(StreamReader reader)
    {
        var result = new StringBuilder(); char[] buffer = new char[4096]; bool truncated = false;
        while (true)
        {
            int read = await reader.ReadAsync(buffer).ConfigureAwait(false); if (read == 0) break;
            int remaining = MaximumOutputCharacters - result.Length;
            if (remaining > 0) result.Append(buffer, 0, Math.Min(read, remaining));
            if (read > remaining) truncated = true;
        }
        if (truncated) result.Append("\n[output truncated]");
        return new BoundedOutput(result.ToString(), truncated);
    }

    private static string ResolveExecutable(string executable)
    {
        string fileName = Path.HasExtension(executable) ? executable : executable + ".exe";
        foreach (string folder in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try { string candidate = Path.GetFullPath(Path.Combine(folder.Trim('"'), fileName)); if (File.Exists(candidate)) return candidate; } catch { }
        }
        throw new FileNotFoundException($"Approved executable '{executable}' was not found as a deterministic .exe on PATH.");
    }

    private static string BuildCommandLine(string executable, IReadOnlyList<string> arguments) => string.Join(' ', new[] { Quote(executable) }.Concat(arguments.Select(Quote)));
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

    private sealed record BoundedOutput(string Text, bool Truncated);

    private sealed class AnonymousPipe : IDisposable
    {
        private AnonymousPipe(SafeFileHandle read, SafeFileHandle write) { Read = read; Write = write; }
        public SafeFileHandle Read { get; } public SafeFileHandle Write { get; }
        public static AnonymousPipe Create(bool childReads)
        {
            var security = new SECURITY_ATTRIBUTES { nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(), bInheritHandle = true };
            if (!CreatePipe(out SafeFileHandle read, out SafeFileHandle write, ref security, 0)) throw new IOException("Unable to create command pipe.");
            SafeFileHandle parentEnd = childReads ? write : read;
            if (!SetHandleInformation(parentEnd, 1, 0)) { read.Dispose(); write.Dispose(); throw new IOException("Unable to secure command pipe."); }
            return new AnonymousPipe(read, write);
        }
        public void Dispose() { Read.Dispose(); Write.Dispose(); }
    }

    private sealed class WindowsJob : IDisposable
    {
        private readonly SafeFileHandle _handle; private WindowsJob(SafeFileHandle handle) => _handle = handle;
        public static WindowsJob Create()
        {
            var handle = new SafeFileHandle(CreateJobObject(IntPtr.Zero, null), true);
            if (handle.IsInvalid) throw new IOException("Unable to create command Job Object.");
            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION(); info.BasicLimitInformation.LimitFlags = 0x2000;
            int length = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>(); IntPtr pointer = Marshal.AllocHGlobal(length);
            try { Marshal.StructureToPtr(info, pointer, false); if (!SetInformationJobObject(handle, 9, pointer, (uint)length)) throw new IOException("Unable to configure command Job Object."); }
            catch { handle.Dispose(); throw; } finally { Marshal.FreeHGlobal(pointer); }
            return new WindowsJob(handle);
        }
        public void Assign(SafeFileHandle process) { if (!AssignProcessToJobObject(_handle, process)) throw new IOException("Unable to assign command to its Job Object; execution was denied."); }
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
