using System;
using System.IO;
using System.Reflection;
using Networker.Core.Updates;
using NuGet.Versioning;
using Windows.ApplicationModel;

namespace networker.Services.Updates
{
    /// <summary>
    /// The installed Networker version: the assembly informational version
    /// cross-checked against the MSIX package identity. Developer builds
    /// (informational <c>…-dev</c>), unpackaged launches, and assembly/package
    /// disagreements disable update installation instead of guessing.
    /// </summary>
    public sealed class InstalledVersionProvider : IInstalledVersionProvider
    {
        private readonly IUpdateLog _log;
        private readonly Lazy<InstalledVersion> _installed;

        public InstalledVersionProvider(IUpdateLog log)
        {
            _log = log;
            _installed = new Lazy<InstalledVersion>(Build);
        }

        public InstalledVersion GetInstalledVersion() => _installed.Value;

        private InstalledVersion Build()
        {
            string displayVersion = "0.0.0";
            NuGetVersion? semantic = null;
            bool isPackaged = false;
            string? packageName = null;
            string? packageFamilyName = null;
            string? packageFullName = null;
            string? publisher = null;
            string? packageVersion = null;
            string? architecture = null;

            try
            {
                string? informational = Assembly.GetEntryAssembly()?
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                    .InformationalVersion;
                if (!string.IsNullOrWhiteSpace(informational))
                {
                    displayVersion = informational;
                    if (NetworkerVersionPolicy.TryParseInformationalVersion(informational, out var parsed))
                    {
                        semantic = parsed;
                    }
                    else
                    {
                        _log.Debug($"Installed version {informational} is not a release label; updates disabled.");
                    }
                }
            }
            catch (Exception ex) when (ex is not (IOException or UnauthorizedAccessException))
            {
                _log.Warn($"Installed version: informational version unavailable: {ex.GetType().Name}.");
            }

            try
            {
                Package package = Package.Current;
                PackageId id = package.Id;
                isPackaged = true;
                packageName = id.Name;
                packageFamilyName = id.FamilyName;
                packageFullName = id.FullName;
                publisher = id.Publisher;
                packageVersion = FormatPackageVersion(id.Version);
                architecture = id.Architecture.ToString();
            }
            catch (Exception ex)
            {
                _log.Warn($"Installed version: package identity unavailable: {ex.GetType().Name}.");
            }

            bool canInstall = semantic is not null
                && isPackaged
                && PackageMatches(semantic, packageVersion);

            if (semantic is not null && isPackaged && !canInstall)
            {
                _log.Warn(
                    $"Installed version: assembly {semantic.ToNormalizedString()} does not map to package version {packageVersion}; updates disabled.");
            }

            return new InstalledVersion(
                semantic,
                displayVersion,
                isPackaged,
                packageName,
                packageFamilyName,
                packageFullName,
                publisher,
                packageVersion,
                architecture,
                canInstall);
        }

        private static bool PackageMatches(NuGetVersion semantic, string? packageVersion)
        {
            if (packageVersion is null)
            {
                return false;
            }

            try
            {
                Version mapped = NetworkerVersionPolicy.ToMsixVersion(semantic);
                return Version.TryParse(packageVersion, out Version? package) && package == mapped;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private static string FormatPackageVersion(PackageVersion version)
            => $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
    }
}
