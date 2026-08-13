using System;
using System.IO;
using System.Text;
using Networker.Core.Updates;

namespace networker.Services.Updates
{
    /// <summary>
    /// Bounded update diagnostics at <c>LocalFolder\Logs\updates.log</c>. Every
    /// method tolerates I/O errors; messages never contain response bodies,
    /// credentials, certificate data, or full query-bearing URLs.
    /// </summary>
    public sealed class UpdateFileLogger : IUpdateLog
    {
        private const string LogFileName = "updates.log";
        private readonly UpdateLogFile _log;

        public UpdateFileLogger()
        {
            string directory = Path.Combine(AppSettings.GetLocalDataDirectory(), "Logs");
            _log = new UpdateLogFile(Path.Combine(directory, LogFileName));
        }

        public void Info(string message) => Append("INFO", message, null);

        public void Warn(string message) => Append("WARN", message, null);

        public void Error(string message, Exception? exception = null) => Append("ERROR", message, exception);

        public void Debug(string message) => Append("DEBUG", message, null);

        private void Append(string level, string message, Exception? exception)
        {
            var builder = new StringBuilder();
            builder.Append(DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            builder.Append(" [").Append(level).Append("] ").Append(message);
            if (exception is not null)
            {
                builder.Append(" -- ").Append(exception.GetType().Name);
                if (!string.IsNullOrWhiteSpace(exception.Message))
                {
                    builder.Append(": ").Append(exception.Message);
                }
            }

            _log.AppendLine(builder.ToString());
        }
    }
}
