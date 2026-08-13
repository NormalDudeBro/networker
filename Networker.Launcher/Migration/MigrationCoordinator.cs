using System.Text.Json;
using Networker.Update.Contracts.Migration;
using Networker.Update.Contracts.State;
using Windows.Management.Deployment;

namespace Networker.Launcher.Migration;

internal sealed class MigrationCoordinator
{
    private readonly MsixDataExporter _exporter = new();

    public bool HasLegacyInstall => MsixDetector.Find() is not null;

    public Task<bool> RunAsync(IWin32Window owner)
    {
        LegacyMsixPackage? package = MsixDetector.Find();
        if (package is null) return Task.FromResult(true);

        DialogResult consent = MessageBox.Show(owner,
            "Networker found your existing installation. It can preserve your settings and move you to automatic updates now.",
            "Update Networker installation",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Information);
        if (consent != DialogResult.OK) return Task.FromResult(false);

        string exportPath;
        try { exportPath = _exporter.Export(package); }
        catch (Exception ex)
        {
            MessageBox.Show(owner, "Networker couldn't prepare your existing settings. The old installation was left unchanged.", "Migration couldn't continue", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            throw new InvalidOperationException("MSIX data export failed.", ex);
        }

        try
        {
            Import(exportPath);
            new LauncherStateStore().Update(state => state with { PendingLegacyMsixRemoval = package.FullName });
            return Task.FromResult(true);
        }
        catch
        {
            MessageBox.Show(owner, "Your settings backup is safe and the old Networker installation remains available. You can retry setup later.", "Migration incomplete", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return Task.FromResult(false);
        }
    }

    public static async Task<bool> TryRemoveLegacyPackageAsync(string packageFullName)
    {
        try
        {
            DeploymentResult result = await new PackageManager().RemovePackageAsync(packageFullName, RemovalOptions.PreserveApplicationData).AsTask();
            return result.ExtendedErrorCode is null;
        }
        catch { return false; }
    }

    private void Import(string exportPath)
    {
        MigrationPayload payload = _exporter.Read(exportPath);
        string dataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Networker");
        Directory.CreateDirectory(dataRoot);
        string settingsPath = Path.Combine(dataRoot, "settings.json");
        Dictionary<string, JsonElement> existing;
        try
        {
            existing = File.Exists(settingsPath)
                ? JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(settingsPath)) ?? new()
                : new();
        }
        catch (JsonException) { existing = new(); }
        foreach ((string key, object? value) in payload.Settings)
        {
            if (!existing.ContainsKey(key)) existing[key] = JsonSerializer.SerializeToElement(value);
        }
        string settingsTemp = settingsPath + ".migration.tmp";
        File.WriteAllText(settingsTemp, JsonSerializer.Serialize(existing));
        File.Move(settingsTemp, settingsPath, overwrite: true);

        string exportRoot = Path.GetDirectoryName(exportPath)!;
        foreach (MigrationFile file in payload.Files)
        {
            string source = Path.Combine(exportRoot, file.Name + ".migrating");
            string destination = Path.Combine(dataRoot, file.Name);
            if (File.Exists(destination) || !File.Exists(source)) continue;
            string digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source))).ToLowerInvariant();
            if (!string.Equals(digest, file.Sha256, StringComparison.Ordinal)) throw new InvalidDataException("Migration file failed verification.");
            File.Copy(source, destination, overwrite: false);
        }

        var stateStore = new LauncherStateStore();
        bool automatic = payload.Settings.TryGetValue("AutomaticUpdateChecksEnabled", out object? automaticValue)
            && ReadBoolean(automaticValue, fallback: true);
        bool preview = payload.Settings.TryGetValue("IncludePrereleaseUpdates", out object? previewValue)
            && ReadBoolean(previewValue, fallback: false);
        stateStore.Update(state => state with
        {
            AutomaticChecksEnabled = automatic,
            Channel = preview
                ? Networker.Update.Contracts.Versioning.NetworkerVersionPolicy.PreviewChannel
                : Networker.Update.Contracts.Versioning.NetworkerVersionPolicy.StableChannel,
            NextCheckUtc = null,
        });
    }

    private static bool ReadBoolean(object? value, bool fallback) => value switch
    {
        bool direct => direct,
        JsonElement { ValueKind: JsonValueKind.True } => true,
        JsonElement { ValueKind: JsonValueKind.False } => false,
        _ => fallback,
    };
}
