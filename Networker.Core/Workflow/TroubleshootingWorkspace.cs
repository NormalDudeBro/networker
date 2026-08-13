using Networker.Core.Services.NetworkConfig;
using System.Text.Json.Serialization;

namespace Networker.Core.Workflow;

public sealed class TroubleshootingWorkspace
{
    public const int CurrentVersion = 1;
    public const int MaximumAssistantEvidenceItems = 50;
    public const int MaximumAssistantEvidenceLength = 16_384;

    public int Version { get; set; } = CurrentVersion;
    public Guid IncidentId { get; set; } = Guid.NewGuid();
    public WorkspaceIncident Incident { get; set; } = new();

    [JsonIgnore]
    public string IncidentTitle
    {
        get => Incident.Title;
        set => Incident.Title = value;
    }

    [JsonIgnore]
    public string IncidentSummary
    {
        get => Incident.Symptoms;
        set => Incident.Symptoms = value;
    }
    public string Environment { get; set; } = string.Empty;
    public string Impact { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public WorkflowStage SelectedStage { get; set; } = WorkflowStage.Start;

    [JsonIgnore]
    public WorkflowStage CurrentStage
    {
        get => SelectedStage;
        set => SelectedStage = value;
    }
    public Dictionary<WorkflowStage, WorkflowStageState> Stages { get; set; } = CreateStages();
    public Dictionary<string, string> NamedValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public TemplateFormData? Generate { get; set; }
    public List<WorkspaceChatMessage> Chat { get; set; } = new();
    public List<WorkspaceActivity> Activity { get; set; } = new();
    public List<AssistantEvidence> AssistantEvidence { get; set; } = new();

    [JsonIgnore]
    public bool IsEmpty => string.IsNullOrWhiteSpace(Incident.Title)
        && string.IsNullOrWhiteSpace(Incident.Symptoms)
        && string.IsNullOrWhiteSpace(Incident.Context)
        && Chat.Count == 0
        && Activity.Count == 0
        && AssistantEvidence.Count == 0
        && Stages.Values.All(state => state.State == WorkflowProgressState.Available);

    [JsonIgnore]
    public bool HasEvidence => !string.IsNullOrWhiteSpace(Incident.Title)
        || !string.IsNullOrWhiteSpace(Incident.Symptoms)
        || AssistantEvidence.Count > 0
        || Stages.Values.Any(state => state.NamedValues.Count > 0);

    public string BuildAssistantEvidence()
    {
        var sections = new List<string>();
        if (!string.IsNullOrWhiteSpace(Incident.Title) || !string.IsNullOrWhiteSpace(Incident.Symptoms))
        {
            sections.Add($"INCIDENT\nTitle: {Incident.Title}\nSymptoms: {Incident.Symptoms}\nContext: {Incident.Context}".Trim());
        }

        foreach (AssistantEvidence evidence in AssistantEvidence.TakeLast(MaximumAssistantEvidenceItems))
        {
            if (!string.IsNullOrWhiteSpace(evidence.Content))
            {
                sections.Add($"{evidence.Source.ToUpperInvariant()}\n{evidence.Content}");
            }
        }

        string result = string.Join("\n\n", sections);
        return result.Length <= MaximumAssistantEvidenceLength
            ? result
            : result[..MaximumAssistantEvidenceLength];
    }

    public static TroubleshootingWorkspace CreateEmpty() => new();

    public WorkflowStageState StateFor(WorkflowStage stage)
    {
        if (!Stages.TryGetValue(stage, out WorkflowStageState? state))
        {
            state = new WorkflowStageState();
            Stages[stage] = state;
        }

        return state;
    }

    public WorkflowStageState GetProgress(WorkflowStage stage) => StateFor(stage);

    public void AddEvidence(AssistantEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        evidence.Content = evidence.Content.Length <= MaximumAssistantEvidenceLength
            ? evidence.Content
            : evidence.Content[..MaximumAssistantEvidenceLength];
        AssistantEvidence.Add(evidence);
        if (AssistantEvidence.Count > MaximumAssistantEvidenceItems)
        {
            AssistantEvidence.RemoveRange(0, AssistantEvidence.Count - MaximumAssistantEvidenceItems);
        }
    }

    private static Dictionary<WorkflowStage, WorkflowStageState> CreateStages() =>
        WorkflowStages.All.ToDictionary(stage => stage, _ => new WorkflowStageState());
}

public sealed class WorkflowStageState
{
    public WorkflowProgressState State { get; set; } = WorkflowProgressState.Available;
    public string? Message { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    [JsonIgnore]
    public bool IsComplete
    {
        get => State == WorkflowProgressState.Completed;
        set => State = value ? WorkflowProgressState.Completed : WorkflowProgressState.Available;
    }
    public int ProgressPercent { get; set; }
    public string Notes { get; set; } = string.Empty;
    public Dictionary<string, string> NamedValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class WorkspaceIncident
{
    public string Title { get; set; } = string.Empty;
    public string Symptoms { get; set; } = string.Empty;
    public string Context { get; set; } = string.Empty;
}

public enum WorkflowProgressState
{
    Available,
    Completed,
    Error,
}

public sealed class WorkspaceChatMessage
{
    public string Role { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class WorkspaceActivity
{
    public string Kind { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AssistantEvidence
{
    public string Source { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;
}
