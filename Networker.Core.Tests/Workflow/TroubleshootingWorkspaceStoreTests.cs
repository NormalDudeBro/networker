using System.Text.Json;
using Networker.Core.Services.NetworkConfig;
using Networker.Core.Workflow;

namespace Networker.Core.Tests.Workflow;

public sealed class TroubleshootingWorkspaceStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "networker-workspace-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SaveAndLoad_RoundTripsIncidentStageChatAndActivity()
    {
        var store = Store();
        var workspace = new TroubleshootingWorkspace
        {
            IncidentTitle = "Branch outage",
            CurrentStage = WorkflowStage.Diagnose,
            Chat = { new WorkspaceChatMessage { Role = "user", Text = "BGP is down" } },
            Activity = { new WorkspaceActivity { Kind = "ping", Detail = "No reply" } },
        };
        workspace.StateFor(WorkflowStage.Inspect).ProgressPercent = 70;

        await store.SaveAsync(workspace);
        WorkspaceLoadResult result = await store.LoadAsync();

        Assert.Null(result.Warning);
        Assert.Equal("Branch outage", result.Workspace.IncidentTitle);
        Assert.Equal(70, result.Workspace.StateFor(WorkflowStage.Inspect).ProgressPercent);
        Assert.Single(result.Workspace.Chat);
        Assert.Single(result.Workspace.Activity);
    }

    [Fact]
    public void ProgressAndSelection_ExposeShellApi()
    {
        var workspace = new TroubleshootingWorkspace { SelectedStage = WorkflowStage.Plan };
        workspace.GetProgress(WorkflowStage.Plan).State = WorkflowProgressState.Completed;
        workspace.GetProgress(WorkflowStage.Plan).Message = "Runbook ready";

        Assert.Equal(WorkflowStage.Plan, workspace.SelectedStage);
        Assert.Equal(WorkflowProgressState.Completed, workspace.GetProgress(WorkflowStage.Plan).State);
        Assert.Equal("Runbook ready", workspace.GetProgress(WorkflowStage.Plan).Message);
    }

    [Fact]
    public void ShellWorkspaceApi_ProvidesIncidentAndSynchronousPersistence()
    {
        TroubleshootingWorkspaceStore store = Store();
        TroubleshootingWorkspace workspace = TroubleshootingWorkspace.CreateEmpty();
        Assert.True(workspace.IsEmpty);
        workspace.Incident.Title = "WAN outage";
        workspace.Incident.Symptoms = "Packet loss";

        store.Save(workspace);
        TroubleshootingWorkspace loaded = store.Load().Workspace;

        Assert.False(loaded.IsEmpty);
        Assert.Equal("WAN outage", loaded.Incident.Title);
        store.Clear();
        Assert.True(store.Load().Workspace.IsEmpty);
    }

    [Fact]
    public async Task Save_RemovesSensitiveNamedValuesAndGenerateSecret()
    {
        var store = Store();
        var workspace = new TroubleshootingWorkspace
        {
            NamedValues =
            {
                ["site"] = "west",
                ["admin-password"] = "do-not-save",
                ["Api Key"] = "do-not-save",
            },
            Generate = new TemplateFormData { Basic = new TemplateBasic { EnableSecret = "do-not-save" } },
        };
        workspace.StateFor(WorkflowStage.Resolve).NamedValues["shared_secret"] = "do-not-save";

        await store.SaveAsync(workspace);
        TroubleshootingWorkspace loaded = (await store.LoadAsync()).Workspace;

        Assert.Equal("west", loaded.NamedValues["site"]);
        Assert.DoesNotContain(loaded.NamedValues.Keys, key => key.Contains("password", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(loaded.StateFor(WorkflowStage.Resolve).NamedValues);
        Assert.Equal(string.Empty, loaded.Generate!.Basic.EnableSecret);
        Assert.Equal("do-not-save", workspace.Generate.Basic.EnableSecret);
    }

    [Fact]
    public async Task Evidence_IsBoundedWhenPersisted()
    {
        var store = Store();
        var workspace = new TroubleshootingWorkspace();
        for (int i = 0; i < 60; i++)
        {
            workspace.AssistantEvidence.Add(new AssistantEvidence { Source = i.ToString(), Content = new string('x', 20_000) });
        }

        await store.SaveAsync(workspace);
        TroubleshootingWorkspace loaded = (await store.LoadAsync()).Workspace;

        Assert.Equal(TroubleshootingWorkspace.MaximumAssistantEvidenceItems, loaded.AssistantEvidence.Count);
        Assert.All(loaded.AssistantEvidence, item => Assert.True(item.Content.Length <= TroubleshootingWorkspace.MaximumAssistantEvidenceLength));
        Assert.Equal("10", loaded.AssistantEvidence[0].Source);
    }

    [Fact]
    public async Task Load_CorruptFileReturnsFreshWorkspaceWithWarning()
    {
        TroubleshootingWorkspaceStore store = Store();
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Combine(_directory, "workspace.json"), "not json");

        WorkspaceLoadResult result = await store.LoadAsync();

        Assert.True(result.HasWarning);
        Assert.Contains("corrupt", result.Warning!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(WorkflowStage.Start, result.Workspace.CurrentStage);
    }

    [Fact]
    public async Task Load_NewerVersionReturnsFreshWorkspaceWithWarning()
    {
        TroubleshootingWorkspaceStore store = Store();
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Combine(_directory, "workspace.json"),
            JsonSerializer.Serialize(new { Version = TroubleshootingWorkspace.CurrentVersion + 1 }));

        WorkspaceLoadResult result = await store.LoadAsync();

        Assert.True(result.HasWarning);
        Assert.Contains("newer", result.Warning!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Clear_RemovesSavedWorkspace()
    {
        TroubleshootingWorkspaceStore store = Store();
        await store.SaveAsync(new TroubleshootingWorkspace { IncidentTitle = "saved" });

        await store.ClearAsync();
        WorkspaceLoadResult result = await store.LoadAsync();

        Assert.False(result.HasWarning);
        Assert.Equal(string.Empty, result.Workspace.IncidentTitle);
    }

    private TroubleshootingWorkspaceStore Store() =>
        new(Path.Combine(_directory, "workspace.json"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }
}
