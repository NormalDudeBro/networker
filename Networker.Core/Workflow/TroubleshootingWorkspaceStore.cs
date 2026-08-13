using System.Text.Json;
using System.Text.Json.Serialization;

namespace Networker.Core.Workflow;

public sealed class TroubleshootingWorkspaceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public TroubleshootingWorkspaceStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public async Task SaveAsync(TroubleshootingWorkspace workspace, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string temporaryPath = _path + ".tmp";
        try
        {
            TroubleshootingWorkspace safe = CreateSafeCopy(workspace);
            safe.Version = TroubleshootingWorkspace.CurrentVersion;
            safe.UpdatedAt = DateTimeOffset.UtcNow;
            string? directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using (FileStream stream = new(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, safe, JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, _path, true);
        }
        finally
        {
            TryDelete(temporaryPath);
            _gate.Release();
        }
    }

    public void Save(TroubleshootingWorkspace workspace) =>
        SaveAsync(workspace).GetAwaiter().GetResult();

    public async Task<WorkspaceLoadResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_path))
            {
                return new WorkspaceLoadResult(new TroubleshootingWorkspace(), null);
            }

            try
            {
                await using FileStream stream = new(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
                TroubleshootingWorkspace? workspace = await JsonSerializer.DeserializeAsync<TroubleshootingWorkspace>(stream, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                if (workspace is null)
                {
                    return CorruptResult();
                }

                if (workspace.Version > TroubleshootingWorkspace.CurrentVersion)
                {
                    return new WorkspaceLoadResult(
                        new TroubleshootingWorkspace(),
                        $"The saved workspace uses newer version {workspace.Version}; this app supports version {TroubleshootingWorkspace.CurrentVersion}.");
                }

                Normalize(workspace);
                return new WorkspaceLoadResult(workspace, null);
            }
            catch (JsonException)
            {
                return CorruptResult();
            }
            catch (NotSupportedException)
            {
                return CorruptResult();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public WorkspaceLoadResult Load() => LoadAsync().GetAwaiter().GetResult();

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }

            TryDelete(_path + ".tmp");
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Clear() => ClearAsync().GetAwaiter().GetResult();

    private static WorkspaceLoadResult CorruptResult() =>
        new(new TroubleshootingWorkspace(), "The saved workspace is corrupt and could not be loaded.");

    private static TroubleshootingWorkspace CreateSafeCopy(TroubleshootingWorkspace workspace)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(workspace, JsonOptions);
        TroubleshootingWorkspace copy = JsonSerializer.Deserialize<TroubleshootingWorkspace>(json, JsonOptions)
            ?? new TroubleshootingWorkspace();
        Normalize(copy);
        RemoveSensitiveValues(copy.NamedValues);
        foreach (WorkflowStageState state in copy.Stages.Values)
        {
            RemoveSensitiveValues(state.NamedValues);
        }

        if (copy.Generate is not null)
        {
            copy.Generate.Basic.EnableSecret = string.Empty;
        }

        return copy;
    }

    private static void Normalize(TroubleshootingWorkspace workspace)
    {
        workspace.Incident ??= new WorkspaceIncident();
        workspace.NamedValues ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        workspace.Stages ??= new Dictionary<WorkflowStage, WorkflowStageState>();
        workspace.Chat ??= new List<WorkspaceChatMessage>();
        workspace.Activity ??= new List<WorkspaceActivity>();
        workspace.AssistantEvidence ??= new List<AssistantEvidence>();
        foreach (WorkspaceChatMessage message in workspace.Chat)
        {
            message.Role ??= string.Empty;
            message.Text ??= string.Empty;
            if (message.Kind == WorkspaceChatMessageKind.Conversation &&
                message.Role.Equals("Error", StringComparison.OrdinalIgnoreCase))
            {
                message.Kind = WorkspaceChatMessageKind.Error;
            }

            if (message.Text.Length > TroubleshootingWorkspace.MaximumChatMessageLength)
            {
                message.Text = message.Text[..TroubleshootingWorkspace.MaximumChatMessageLength];
            }
        }

        if (workspace.Chat.Count > TroubleshootingWorkspace.MaximumChatMessages)
        {
            workspace.Chat.RemoveRange(0, workspace.Chat.Count - TroubleshootingWorkspace.MaximumChatMessages);
        }
        foreach (WorkflowStage stage in WorkflowStages.All)
        {
            workspace.StateFor(stage);
        }

        foreach (WorkflowStageState state in workspace.Stages.Values)
        {
            state.ProgressPercent = Math.Clamp(state.ProgressPercent, 0, 100);
            state.NamedValues ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        foreach (AssistantEvidence evidence in workspace.AssistantEvidence)
        {
            evidence.Content ??= string.Empty;
            if (evidence.Content.Length > TroubleshootingWorkspace.MaximumAssistantEvidenceLength)
            {
                evidence.Content = evidence.Content[..TroubleshootingWorkspace.MaximumAssistantEvidenceLength];
            }
        }

        if (workspace.AssistantEvidence.Count > TroubleshootingWorkspace.MaximumAssistantEvidenceItems)
        {
            workspace.AssistantEvidence.RemoveRange(0,
                workspace.AssistantEvidence.Count - TroubleshootingWorkspace.MaximumAssistantEvidenceItems);
        }
    }

    private static void RemoveSensitiveValues(Dictionary<string, string> values)
    {
        foreach (string key in values.Keys.Where(IsSensitiveKey).ToArray())
        {
            values.Remove(key);
        }
    }

    private static bool IsSensitiveKey(string key)
    {
        string normalized = new(key.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        return normalized.Contains("password", StringComparison.Ordinal)
            || normalized.Contains("secret", StringComparison.Ordinal)
            || normalized.Contains("apikey", StringComparison.Ordinal);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

public sealed record WorkspaceLoadResult(TroubleshootingWorkspace Workspace, string? Warning)
{
    public bool HasWarning => !string.IsNullOrWhiteSpace(Warning);
}
