using System.Diagnostics;
using System.Security.Cryptography;

namespace Networker.Launcher;

internal static class MainAppProcess
{
    public static (Process Process, string HealthToken) Start(IReadOnlyList<string> arguments)
    {
        string executable = Path.Combine(AppContext.BaseDirectory, "networker.exe");
        if (!File.Exists(executable)) throw new FileNotFoundException("Networker application is missing.", executable);
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var info = new ProcessStartInfo(executable) { UseShellExecute = true, WorkingDirectory = AppContext.BaseDirectory };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        info.ArgumentList.Add("--networker-health-token");
        info.ArgumentList.Add(token);
        return (Process.Start(info) ?? throw new InvalidOperationException("Networker could not be started."), token);
    }

    public static string HealthMarkerPath(string token) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Networker", "Updates", "health", token + ".ok");
}
