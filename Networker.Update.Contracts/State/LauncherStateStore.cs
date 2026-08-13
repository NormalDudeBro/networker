using System.Text.Json;

namespace Networker.Update.Contracts.State;

public sealed class LauncherStateStore
{
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(2);
    private readonly string _path;
    private readonly string _mutexName;

    public LauncherStateStore(string? path = null)
    {
        _path = path ?? GetDefaultPath();
        _mutexName = "Local\\Networker.LauncherState." + Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(System.IO.Path.GetFullPath(_path))))[..16];
    }

    public string Path => _path;

    public LauncherState Read()
    {
        try
        {
            using var lease = Acquire();
            if (!File.Exists(_path)) return LauncherState.Default;
            LauncherState? state = JsonSerializer.Deserialize<LauncherState>(File.ReadAllText(_path));
            return state is { SchemaVersion: LauncherState.CurrentSchemaVersion } ? state : LauncherState.Default;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or TimeoutException)
        {
            return LauncherState.Default;
        }
    }

    public void Write(LauncherState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.SchemaVersion != LauncherState.CurrentSchemaVersion)
            throw new ArgumentException("Unsupported launcher-state schema.", nameof(state));

        string? temp = null;
        using var lease = Acquire();
        try
        {
            string directory = System.IO.Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(directory);
            temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(state));
            File.Move(temp, _path, overwrite: true);
            temp = null;
        }
        finally
        {
            if (temp is not null) try { File.Delete(temp); } catch { }
        }
    }

    public LauncherState Update(Func<LauncherState, LauncherState> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        using var lease = Acquire();
        LauncherState current;
        try
        {
            current = File.Exists(_path)
                ? JsonSerializer.Deserialize<LauncherState>(File.ReadAllText(_path)) ?? LauncherState.Default
                : LauncherState.Default;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            current = LauncherState.Default;
        }

        LauncherState next = update(current);
        string directory = System.IO.Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        string temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(next));
            File.Move(temp, _path, overwrite: true);
        }
        finally
        {
            try { File.Delete(temp); } catch { }
        }
        return next;
    }

    public static string GetDefaultPath() => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Networker", "Updates", "launcher-state.json");

    private IDisposable Acquire()
    {
        var mutex = new Mutex(false, _mutexName);
        try
        {
            bool acquired;
            try { acquired = mutex.WaitOne(LockTimeout); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new TimeoutException("Launcher state is busy.");
            return new MutexLease(mutex);
        }
        catch
        {
            mutex.Dispose();
            throw;
        }
    }

    private sealed class MutexLease(Mutex mutex) : IDisposable
    {
        public void Dispose()
        {
            try { mutex.ReleaseMutex(); } finally { mutex.Dispose(); }
        }
    }
}
