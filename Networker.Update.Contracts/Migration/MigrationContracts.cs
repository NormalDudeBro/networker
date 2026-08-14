namespace Networker.Update.Contracts.Migration;

public static class LegacyMsixIdentity
{
    public const string Name = "12266223-d1a1-43c3-aca2-59c9ae71cd23";
    public const string Publisher = "CN=Kenny";
}

public static class MigrationAllowList
{
    public static IReadOnlySet<string> Settings { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "OllamaEndpoint", "OllamaApiKey", "SelectedModel", "ThemeMode", "SelectedProvider",
        "NetworkConfigDirectory", "DefaultVendor", "SelectedToolKey",
        "AutomaticUpdateChecksEnabled", "IncludePrereleaseUpdates",
        // Codex UI preferences only — never auth/token/Codex-home paths.
        "CodexSelectedModel", "CodexReasoningEffort", "CodexChatThreadId",
        "CodexAgentNetworkEnabled", "CodexAgentAuthorizedWorkspace",
        "LastAgentWorkspacePath", "AgentDisclosureAccepted",
    };

    public static IReadOnlySet<string> Files { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "GlobalSystemPrompt.txt", "GlobalCustomInstructions.txt", "troubleshooting-workspace.json",
    };
}

public sealed record MigrationPayload(
    int SchemaVersion,
    string SourcePackageFullName,
    string SourceVersion,
    DateTimeOffset ExportedAtUtc,
    IReadOnlyDictionary<string, object?> Settings,
    IReadOnlyList<MigrationFile> Files);

public sealed record MigrationFile(string Name, string Sha256, long Length);
