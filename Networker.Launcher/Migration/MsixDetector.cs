using Networker.Update.Contracts.Migration;
using Windows.ApplicationModel;
using Windows.Management.Deployment;

namespace Networker.Launcher.Migration;

internal sealed record LegacyMsixPackage(
    string FullName,
    string FamilyName,
    string Version,
    Package Package);

internal static class MsixDetector
{
    public static LegacyMsixPackage? Find()
    {
        try
        {
            var manager = new PackageManager();
            Package? package = manager.FindPackagesForUser(string.Empty)
                .Where(value => string.Equals(value.Id.Name, LegacyMsixIdentity.Name, StringComparison.Ordinal))
                .OrderByDescending(value => value.Id.Version.Major)
                .ThenByDescending(value => value.Id.Version.Minor)
                .ThenByDescending(value => value.Id.Version.Build)
                .ThenByDescending(value => value.Id.Version.Revision)
                .FirstOrDefault();
            if (package is null
                || !string.Equals(package.Id.Publisher, LegacyMsixIdentity.Publisher, StringComparison.Ordinal)
                || !string.Equals(package.Id.Architecture.ToString(), "X64", StringComparison.OrdinalIgnoreCase)) return null;

            var version = package.Id.Version;
            return new LegacyMsixPackage(
                package.Id.FullName,
                package.Id.FamilyName,
                $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}",
                package);
        }
        catch (Exception) { return null; }
    }
}
