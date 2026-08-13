using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using Networker.Update.Contracts.State;

namespace Networker.UpdateHost;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            Dictionary<string, string> options = Parse(args);
            string root = Path.GetFullPath(options["root"]);
            string package = Path.GetFullPath(options["package"]);
            string targetSlot = options["target-slot"];
            string expectedHash = options["sha256"];
            string version = options["version"];
            uint waitPid = uint.Parse(options["wait-pid"]);
            if (targetSlot is not ("app-a" or "app-b")) throw new InvalidDataException("Invalid target slot.");
            WaitForExit(waitPid);
            using var appMutex = new Mutex(false, "Local\\Networker.MainApp");
            bool ownsAppMutex;
            try { ownsAppMutex = appMutex.WaitOne(0); }
            catch (AbandonedMutexException) { ownsAppMutex = true; }
            if (!ownsAppMutex) throw new InvalidOperationException("Networker is still running.");
            try
            {
            using var packageStream = new FileStream(package, FileMode.Open, FileAccess.Read, FileShare.Read);
            string digest = Convert.ToHexString(SHA256.HashData(packageStream)).ToLowerInvariant();
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(digest), Convert.FromHexString(expectedHash)))
                throw new InvalidDataException("Update package hash changed.");

            string staging = Path.Combine(root, targetSlot + ".staging-" + Guid.NewGuid().ToString("N"));
            string target = Path.Combine(root, targetSlot);
            Extract(package, staging);
            if (!File.Exists(Path.Combine(staging, "Networker.Launcher.exe"))
                || !File.Exists(Path.Combine(staging, "Networker.UpdateHost.exe"))
                || !File.Exists(Path.Combine(staging, "networker.exe"))
                || File.ReadAllText(Path.Combine(staging, "version.txt")).Trim() != version)
                throw new InvalidDataException("Update payload is incomplete.");

            string? old = null;
            if (Directory.Exists(target)) { old = target + ".old-" + Guid.NewGuid().ToString("N"); Directory.Move(target, old); }
            try { Directory.Move(staging, target); }
            catch { if (old is not null && !Directory.Exists(target)) Directory.Move(old, target); throw; }

            var slots = new ActiveSlotStore(root);
            string previous = slots.Read().ActiveSlot;
            slots.Write(new ActiveSlotState { ActiveSlot = targetSlot });
            RecoveryJournal? journal = RecoveryJournal.Read();
            if (journal is not null) (journal with { Phase = RecoveryPhase.AwaitingHealth }).Write();
            if (old is not null) try { Directory.Delete(old, true); } catch { }
            try { File.Delete(package); } catch { }
            appMutex.ReleaseMutex();
            ownsAppMutex = false;
            Process.Start(new ProcessStartInfo(Path.Combine(root, "Networker.exe"), "--networker-updated") { UseShellExecute = true, WorkingDirectory = root });
            return 0;
            }
            finally { if (ownsAppMutex) appMutex.ReleaseMutex(); }
        }
        catch { return 1; }
    }

    private static void Extract(string package, string staging)
    {
        Directory.CreateDirectory(staging);
        string root = Path.GetFullPath(staging) + Path.DirectorySeparatorChar;
        using ZipArchive archive = ZipFile.OpenRead(package);
        if (archive.Entries.Count > 10_000 || archive.Entries.Sum(value => value.Length) > 4L * 1024 * 1024 * 1024)
            throw new InvalidDataException("Update archive exceeds extraction limits.");
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string name = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            if (string.IsNullOrEmpty(name) || name.EndsWith(Path.DirectorySeparatorChar)) continue;
            if (Path.IsPathRooted(name) || name.Contains(':', StringComparison.Ordinal)
                || name.Split(Path.DirectorySeparatorChar).Any(x => x is ".." or "." or ""))
                throw new InvalidDataException("Unsafe archive path.");
            string destination = Path.GetFullPath(Path.Combine(staging, name));
            if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Archive escaped staging root.");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, false);
        }
    }

    private static void WaitForExit(uint pid)
    {
        try { using Process process = Process.GetProcessById((int)pid); if (!process.WaitForExit(60_000)) throw new TimeoutException(); }
        catch (ArgumentException) { }
    }

    private static Dictionary<string, string> Parse(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i + 1 < args.Length; i += 2)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("Invalid updater argument.");
            result[args[i][2..]] = args[i + 1];
        }
        foreach (string key in new[] { "root", "package", "target-slot", "sha256", "version", "wait-pid" })
            if (!result.ContainsKey(key)) throw new ArgumentException("Missing updater argument.");
        return result;
    }
}
