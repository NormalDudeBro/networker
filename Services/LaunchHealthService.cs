using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace networker.Services
{
    public sealed class LaunchHealthService : IDisposable
    {
        private const string ArgumentName = "--networker-health-token";
        private readonly string? _token;
        private readonly Mutex _appMutex = new(false, "Local\\Networker.MainApp");
        private bool _ownsMutex;

        public LaunchHealthService()
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.FindIndex(args, value => value == ArgumentName);
            if (index >= 0 && index + 1 < args.Length && IsToken(args[index + 1])) _token = args[index + 1];
            try { _ownsMutex = _appMutex.WaitOne(0); } catch (AbandonedMutexException) { _ownsMutex = true; }
        }

        public void SignalHealthy()
        {
            if (_token is null) return;
            try
            {
                string directory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Networker", "Updates", "health");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, _token + ".ok");
                string temp = path + ".tmp";
                File.WriteAllText(temp, DateTimeOffset.UtcNow.ToString("O"));
                File.Move(temp, path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        public void Dispose()
        {
            if (_ownsMutex) _appMutex.ReleaseMutex();
            _appMutex.Dispose();
        }

        private static bool IsToken(string value) => value.Length == 48 && value.All(char.IsAsciiHexDigit);
    }
}
