using Networker.Core.Prompting;
using Xunit;

namespace Networker.Core.Tests;

public class ToolPromptsTests
{
    [Fact]
    public void AllToolPrompts_AreNonEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(ToolPrompts.ConfigAudit));
        Assert.False(string.IsNullOrWhiteSpace(ToolPrompts.ConfigDiff));
        Assert.False(string.IsNullOrWhiteSpace(ToolPrompts.LogAnalysis));
        Assert.False(string.IsNullOrWhiteSpace(ToolPrompts.Playbook));
        Assert.False(string.IsNullOrWhiteSpace(ToolPrompts.Topology));
        Assert.False(string.IsNullOrWhiteSpace(ToolPrompts.Translation));
    }

    [Fact]
    public void PlaybookPrompt_RequestsPlainStepFormat()
    {
        // The AI playbook must match PlaybookGenerator.RenderPlain's format so
        // AI- and rule-generated playbooks render identically.
        Assert.Contains("Step N:", ToolPrompts.Playbook);
        Assert.Contains("Expected:", ToolPrompts.Playbook);
        Assert.Contains("Why:", ToolPrompts.Playbook);
        Assert.Contains("No markdown", ToolPrompts.Playbook);
    }

    [Fact]
    public void AuditPrompt_AnchorsOnRuleBasedFindings()
    {
        Assert.Contains("rule-based audit", ToolPrompts.ConfigAudit);
    }

    [Fact]
    public void DiffPrompt_CoversImpactAndRisk()
    {
        Assert.Contains("impact", ToolPrompts.ConfigDiff);
        Assert.Contains("risk", ToolPrompts.ConfigDiff);
    }

    [Fact]
    public void TranslationPrompt_PreservesSemantics()
    {
        Assert.Contains("preserving semantics", ToolPrompts.Translation);
    }

    [Fact]
    public void Prompts_ComposeCleanlyWithGlobalPrompt()
    {
        // The tool prompt is joined ahead of a hypothetical global prompt; the
        // merge must keep both and preserve section boundaries.
        string merged = PromptBuilder.JoinNonEmpty(ToolPrompts.ConfigAudit, "Global instructions.");
        Assert.StartsWith("You are a senior network engineer", merged);
        Assert.EndsWith("Global instructions.", merged);
        Assert.Contains("\n\n", merged);
    }
}
