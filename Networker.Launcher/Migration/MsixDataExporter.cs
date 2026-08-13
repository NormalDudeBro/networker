using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using Networker.Update.Contracts.Migration;
using Windows.Management.Core;

namespace Networker.Launcher.Migration;

internal sealed class MsixDataExporter
{
    private static readonly byte[] Entropy = "Networker MSIX migration v1"u8.ToArray();

    public string Export(LegacyMsixPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        var applicationData = ApplicationDataManager.CreateForPackageFamily(package.FamilyName);
        var settings = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (string key in MigrationAllowList.Settings)
        {
            if (applicationData.LocalSettings.Values.TryGetValue(key, out object? value)
                && value is string or bool or int or DateTimeOffset)
                settings[key] = value;
        }

        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Networker", "Migration", "msix-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss"));
        Directory.CreateDirectory(root);
        ApplyUserOnlyAcl(root);

        var files = new List<MigrationFile>();
        foreach (string name in MigrationAllowList.Files)
        {
            string source = Path.Combine(applicationData.LocalFolder.Path, name);
            if (!File.Exists(source)) continue;
            string destination = Path.Combine(root, name + ".migrating");
            File.Copy(source, destination, overwrite: false);
            files.Add(new MigrationFile(name, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(destination))).ToLowerInvariant(), new FileInfo(destination).Length));
        }

        var payload = new MigrationPayload(1, package.FullName, package.Version, DateTimeOffset.UtcNow, settings, files);
        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(payload);
        byte[] protectedBytes = ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);
        CryptographicOperations.ZeroMemory(plaintext);
        string partial = Path.Combine(root, "migration.dat.partial");
        string completed = Path.Combine(root, "migration.dat");
        File.WriteAllBytes(partial, protectedBytes);
        File.Move(partial, completed);
        File.WriteAllText(Path.Combine(root, ".complete"), package.FullName);
        return completed;
    }

    public MigrationPayload Read(string path)
    {
        byte[] protectedBytes = File.ReadAllBytes(path);
        byte[] plaintext = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
        try
        {
            return JsonSerializer.Deserialize<MigrationPayload>(plaintext)
                ?? throw new InvalidDataException("Migration payload is empty.");
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    private static void ApplyUserOnlyAcl(string path)
    {
        if (!OperatingSystem.IsWindows()) return;
        SecurityIdentifier user = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Current user identity is unavailable.");
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(path).SetAccessControl(security);
    }
}
