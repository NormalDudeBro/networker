namespace Networker.Update.Contracts.State;

public sealed record LauncherState
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string Channel { get; init; } = Versioning.NetworkerVersionPolicy.StableChannel;
    public bool AutomaticChecksEnabled { get; init; } = true;
    public bool ManualCheckRequested { get; init; }
    public DateTimeOffset? LastSuccessfulCheckUtc { get; init; }
    public DateTimeOffset? NextCheckUtc { get; init; }
    public int FailureCount { get; init; }
    public string? ETag { get; init; }
    public string? LastObservedTarget { get; init; }
    public string? HighestAuthenticatedStableVersion { get; init; }
    public string? HighestAuthenticatedPreviewVersion { get; init; }
    public bool FirstRunCompleted { get; init; }
    public bool DesktopShortcutRequested { get; init; }
    public string? RecoveryJournalPath { get; init; }
    public string? PendingLegacyMsixRemoval { get; init; }

    public static LauncherState Default { get; } = new();
}
