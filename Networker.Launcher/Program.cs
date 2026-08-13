using Networker.Update.Diagnostics;

namespace Networker.Launcher;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var log = new UpdateLog();
        try
        {
            ApplicationConfiguration.Initialize();
            using var mutex = new Mutex(initiallyOwned: true, "Local\\Networker.Launcher", out bool ownsMutex);
            if (!ownsMutex) return;
            new LauncherCoordinator().RunAsync(args).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            log.Error("Networker launcher failed.", ex);
            TryDirectLaunch(args);
        }
    }

    private static void TryDirectLaunch(string[] args)
    {
        try { MainAppProcess.Start(args); } catch { }
    }
}
