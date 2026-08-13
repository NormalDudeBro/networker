using System;
using System.Collections.Generic;
using System.IO;
using Networker.Core.Updates;

namespace networker.Services.Updates
{
    /// <summary>
    /// Update staging storage confined to
    /// <c>ApplicationData.Current.TemporaryFolder\NetworkerUpdates</c>. A
    /// successfully staged package is marked with a <c>.staged</c> marker so
    /// <see cref="UpdateCoordinator.CleanupConfirmedStaged"/> can remove it after
    /// the next launch confirms the installed version. Every path is derived from
    /// a validated release tag; nothing outside the update root is ever touched.
    /// </summary>
    public sealed class UpdatePackageStorage : IUpdatePackageStorage
    {
        private const string RootDirectoryName = "NetworkerUpdates";
        private const string StagedMarkerName = ".staged";
        private readonly string _rootPath;
        private readonly IUpdateLog _log;

        public UpdatePackageStorage(IUpdateLog log)
        {
            _log = log;
            _rootPath = Path.Combine(AppSettings.GetTemporaryDataDirectory(), RootDirectoryName);
        }

        public string GetDownloadDirectoryPath(string tag)
        {
            if (!NetworkerVersionPolicy.TryParseTag(tag, out _))
            {
                throw new ArgumentException("Invalid release tag for update staging.", nameof(tag));
            }

            return Path.Combine(_rootPath, tag);
        }

        public void PreserveStaged(string tag)
        {
            try
            {
                string directory = GetDownloadDirectoryPath(tag);
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, StagedMarkerName), tag);
                _log.Debug($"Preserved staged update {tag}.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log.Warn($"PreserveStaged failed for {tag}: {ex.GetType().Name}.");
            }
        }

        public IReadOnlyList<string> GetStagedTags()
        {
            var tags = new List<string>();
            try
            {
                if (!Directory.Exists(_rootPath))
                {
                    return tags;
                }

                foreach (string directory in Directory.EnumerateDirectories(_rootPath))
                {
                    string tag = Path.GetFileName(directory);
                    if (NetworkerVersionPolicy.TryParseTag(tag, out _)
                        && File.Exists(Path.Combine(directory, StagedMarkerName)))
                    {
                        tags.Add(tag);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log.Warn($"GetStagedTags failed: {ex.GetType().Name}.");
            }

            return tags;
        }

        public void RemoveStaged(string tag)
        {
            try
            {
                string directory = GetDownloadDirectoryPath(tag);
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }

                _log.Info($"Removed staged update {tag}.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log.Warn($"RemoveStaged failed for {tag}: {ex.GetType().Name}.");
            }
        }

        public void Cleanup(string tag)
        {
            try
            {
                string directory = GetDownloadDirectoryPath(tag);
                if (!Directory.Exists(directory))
                {
                    return;
                }

                bool preserved = File.Exists(Path.Combine(directory, StagedMarkerName));
                if (preserved)
                {
                    // A preserved package awaits restart confirmation; only clear
                    // leftover partial files so a failed re-download never lingers.
                    foreach (string file in Directory.EnumerateFiles(directory, "*.partial"))
                    {
                        TryDelete(file);
                    }

                    return;
                }

                Directory.Delete(directory, recursive: true);
                _log.Debug($"Cleaned staging for {tag}.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log.Warn($"Cleanup failed for {tag}: {ex.GetType().Name}.");
            }
        }

        public void CleanupAll()
        {
            try
            {
                if (Directory.Exists(_rootPath))
                {
                    Directory.Delete(_rootPath, recursive: true);
                    _log.Info("Cleared all update staging data.");
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log.Warn($"CleanupAll failed: {ex.GetType().Name}.");
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception)
            {
                // best effort
            }
        }
    }
}
