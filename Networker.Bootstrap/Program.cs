using System.Diagnostics;

namespace Networker.Bootstrap;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            string root = AppContext.BaseDirectory;
            string pointer = Path.Combine(root, "active-slot.txt");
            string slot = File.Exists(pointer) ? File.ReadAllText(pointer).Trim() : "app-a";
            if (slot is not ("app-a" or "app-b")) slot = "app-a";
            string launcher = Path.Combine(root, slot, "Networker.Launcher.exe");
            if (!File.Exists(launcher)) return 2;
            var start = new ProcessStartInfo(launcher) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(launcher)! };
            foreach (string arg in args) start.ArgumentList.Add(arg);
            Process.Start(start);
            return 0;
        }
        catch { return 1; }
    }
}
