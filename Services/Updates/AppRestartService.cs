using System;
using Microsoft.Windows.AppLifecycle;
using Networker.Core.Updates;

namespace networker.Services.Updates
{
    /// <summary>
    /// User-initiated restart after a staged update. The underlying
    /// <see cref="AppInstance.Restart(string)"/> is a static OS call; this
    /// wrapper exists for DI and failure mapping. A failed restart leaves the
    /// staged update intact for the next normal activation.
    /// </summary>
    public sealed class AppRestartService
    {
        private readonly IUpdateLog _log;

        public AppRestartService(IUpdateLog log)
        {
            _log = log;
        }

        /// <summary>
        /// Restarts Networker with the <c>--updated</c> argument. Returns false
        /// and sets <paramref name="error"/> (non-technical) when the OS call fails.
        /// </summary>
        public bool TryRestart(out string? error)
        {
            try
            {
                AppInstance.Restart("--updated");
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                _log.Error("App restart failed.", ex);
                error = "Couldn't restart Networker. Close and reopen it to apply the update.";
                return false;
            }
        }
    }
}
