using System.Text.Json;

namespace Networker.Update.Contracts.State;

public enum RecoveryPhase { Downloaded, Applying, AwaitingHealth }

public sealed record RecoveryJournal(
    int SchemaVersion,
    string PreviousSlot,
    string TargetSlot,
    string PreviousVersion,
    string TargetVersion,
    string PackagePath,
    RecoveryPhase Phase,
    int FailedHealthAttempts,
    DateTimeOffset CreatedAtUtc)
{
    public const int CurrentSchemaVersion = 1;
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Networker", "Updates", "recovery.json");

    public static RecoveryJournal? Read()
    {
        try
        {
            if (!File.Exists(DefaultPath)) return null;
            var value = JsonSerializer.Deserialize<RecoveryJournal>(File.ReadAllText(DefaultPath));
            return value is { SchemaVersion: CurrentSchemaVersion } ? value : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    public void Write()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DefaultPath)!);
        string temp = DefaultPath + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(this)); File.Move(temp, DefaultPath, true); }
        finally { try { File.Delete(temp); } catch { } }
    }

    public static void Delete() { try { File.Delete(DefaultPath); } catch { } }
}
