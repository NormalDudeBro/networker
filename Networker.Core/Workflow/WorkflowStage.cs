namespace Networker.Core.Workflow;

public enum WorkflowStage
{
    Start = 1,
    Inspect,
    Diagnose,
    Map,
    Compare,
    Plan,
    Resolve,
    Assist,
    Settings,
}

public sealed record WorkflowStageDefinition(
    WorkflowStage Stage,
    int Number,
    string Key,
    string Label,
    string Description);

public sealed record LegacyToolRoute(string ToolKey, WorkflowStage Stage);

public static class WorkflowStageCatalog
{
    public static IReadOnlyList<WorkflowStageDefinition> All { get; } = new[]
    {
        new WorkflowStageDefinition(WorkflowStage.Start, 1, "start", "Start", "Capture the incident and establish its scope."),
        new WorkflowStageDefinition(WorkflowStage.Inspect, 2, "inspect", "Inspect", "Collect device configuration and operational evidence."),
        new WorkflowStageDefinition(WorkflowStage.Diagnose, 3, "diagnose", "Diagnose", "Identify faults, risks, and likely causes."),
        new WorkflowStageDefinition(WorkflowStage.Map, 4, "map", "Map", "Map addresses, dependencies, and topology."),
        new WorkflowStageDefinition(WorkflowStage.Compare, 5, "compare", "Compare", "Compare observed state with baselines or revisions."),
        new WorkflowStageDefinition(WorkflowStage.Plan, 6, "plan", "Plan", "Build and review a safe remediation plan."),
        new WorkflowStageDefinition(WorkflowStage.Resolve, 7, "resolve", "Resolve", "Generate, translate, and apply the resolution."),
        new WorkflowStageDefinition(WorkflowStage.Assist, 8, "assist", "Assist", "Work with the assistant using gathered evidence."),
        new WorkflowStageDefinition(WorkflowStage.Settings, 9, "settings", "Settings", "Configure models, credentials, templates, and preferences."),
    };

    public static WorkflowStageDefinition Get(WorkflowStage stage) =>
        All.First(definition => definition.Stage == stage);

    public static bool TryFind(string? value, out WorkflowStageDefinition definition)
    {
        string candidate = value?.Trim() ?? string.Empty;
        definition = All.FirstOrDefault(item =>
            string.Equals(item.Key, candidate, StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.Label, candidate, StringComparison.OrdinalIgnoreCase)
            || item.Number.ToString() == candidate)!;
        return definition is not null;
    }

    public static bool TryFindLegacyTool(string? toolKey, out LegacyToolRoute route)
    {
        string key = toolKey?.Trim().ToLowerInvariant() ?? string.Empty;
        WorkflowStage? stage = key switch
        {
            "config-import" or "import" => WorkflowStage.Inspect,
            "config-audit" or "audit" or "log-analyzer" or "logs" => WorkflowStage.Diagnose,
            "topology" or "diagram" or "ip" or "subnet" or "cidr" => WorkflowStage.Map,
            "quick-diff" or "config-diff" or "compare" or "workspace diff" => WorkflowStage.Compare,
            "playbooks" or "runbook" => WorkflowStage.Plan,
            "json-generator" or "config-generate" or "config generator" or "configuration workspace" or "full generator"
                or "translator" or "translate" => WorkflowStage.Resolve,
            "assistant" or "chat" => WorkflowStage.Assist,
            "settings" or "vault" or "secrets" or "templates" or "template library" => WorkflowStage.Settings,
            _ => null,
        };

        route = stage is null ? null! : new LegacyToolRoute(key, stage.Value);
        return stage is not null;
    }
}

public static class WorkflowNavigationPolicy
{
    public static bool TryGetStageForNumber(
        int number,
        bool textEntry,
        bool modifier,
        out WorkflowStage stage)
    {
        stage = default;
        if (textEntry || modifier || number < 1 || number > WorkflowStageCatalog.All.Count)
        {
            return false;
        }

        stage = (WorkflowStage)number;
        return true;
    }
}

public static class WorkflowStages
{
    public static IReadOnlyList<WorkflowStage> All { get; } = WorkflowStageCatalog.All.Select(item => item.Stage).ToArray();

    public static bool TryFromNavigationNumber(string? input, out WorkflowStage stage)
    {
        stage = default;
        return int.TryParse(input?.Trim(), out int number)
            && number >= 1
            && number <= All.Count
            && Enum.IsDefined(stage = (WorkflowStage)number);
    }

    public static WorkflowStage FromLegacyTool(string? toolKey)
    {
        return WorkflowStageCatalog.TryFindLegacyTool(toolKey, out LegacyToolRoute route)
            ? route.Stage
            : WorkflowStage.Start;
    }
}
