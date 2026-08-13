using System.Text.Json;

namespace Networker.Update.Contracts.State;

public sealed record ActiveSlotState
{
    public const int CurrentSchemaVersion = 1;
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string ActiveSlot { get; init; } = "app-a";
    public string? PreviousSlot { get; init; }
    public string Version { get; init; } = "0.0.0";
    public bool PendingHealth { get; init; }
    public int FailedHealthAttempts { get; init; }
}

public sealed class ActiveSlotStore
{
    private readonly string _path;

    public ActiveSlotStore(string installRoot) => _path = Path.Combine(installRoot, "active-slot.txt");

    public ActiveSlotState Read()
    {
        try
        {
            string slot = File.ReadAllText(_path).Trim();
            return slot is "app-a" or "app-b" ? new ActiveSlotState { ActiveSlot = slot } : new ActiveSlotState();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new ActiveSlotState(); }
    }

    public void Write(ActiveSlotState state)
    {
        if (state.ActiveSlot is not ("app-a" or "app-b")) throw new ArgumentException("Invalid application slot.", nameof(state));
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, state.ActiveSlot + Environment.NewLine);
            File.Move(temp, _path, overwrite: true);
        }
        finally { try { File.Delete(temp); } catch { } }
    }
}
