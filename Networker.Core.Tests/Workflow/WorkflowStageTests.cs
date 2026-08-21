using Networker.Core.Workflow;

namespace Networker.Core.Tests.Workflow;

public sealed class WorkflowStageTests
{
    [Fact]
    public void Catalog_HasExactlyNineOrderedStages()
    {
        Assert.Equal(new[]
        {
            WorkflowStage.Start, WorkflowStage.Inspect, WorkflowStage.Diagnose,
            WorkflowStage.Map, WorkflowStage.Compare, WorkflowStage.Plan,
            WorkflowStage.Resolve, WorkflowStage.Assist, WorkflowStage.Settings,
        }, WorkflowStageCatalog.All.Select(item => item.Stage));
    }

    [Theory]
    [InlineData("1", WorkflowStage.Start)]
    [InlineData(" 9 ", WorkflowStage.Settings)]
    public void Navigation_AcceptsPlainNumbers(string text, WorkflowStage expected)
    {
        Assert.True(WorkflowStages.TryFromNavigationNumber(text, out WorkflowStage actual));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("10")]
    [InlineData("go 2")]
    [InlineData("2. Inspect")]
    public void Navigation_RejectsAnythingExceptAStageNumber(string text) =>
        Assert.False(WorkflowStages.TryFromNavigationNumber(text, out _));

    [Theory]
    [InlineData("config-audit", WorkflowStage.Diagnose)]
    [InlineData("topology", WorkflowStage.Map)]
    [InlineData("quick-diff", WorkflowStage.Compare)]
    [InlineData("playbooks", WorkflowStage.Plan)]
    [InlineData("config-generate", WorkflowStage.Resolve)]
    [InlineData("assistant", WorkflowStage.Assist)]
    public void LegacyTools_MapToWorkflow(string key, WorkflowStage expected) =>
        Assert.Equal(expected, WorkflowStages.FromLegacyTool(key));

    [Fact]
    public void Catalog_FindsStagesAndLegacyRoutes()
    {
        Assert.True(WorkflowStageCatalog.TryFind("diagnose", out WorkflowStageDefinition definition));
        Assert.Equal(WorkflowStage.Diagnose, definition.Stage);
        Assert.Equal(3, definition.Number);
        Assert.True(WorkflowStageCatalog.TryFindLegacyTool("log-analyzer", out LegacyToolRoute route));
        Assert.Equal(WorkflowStage.Diagnose, route.Stage);
    }

    [Theory]
    [InlineData(3, false, false, true)]
    [InlineData(3, true, false, false)]
    [InlineData(3, false, true, false)]
    [InlineData(0, false, false, false)]
    public void NavigationPolicy_OnlyAcceptsUnmodifiedNumbersOutsideTextEntry(
        int number, bool textEntry, bool modifier, bool expected) =>
        Assert.Equal(expected, WorkflowNavigationPolicy.TryGetStageForNumber(number, textEntry, modifier, out _));

    [Theory]
    [InlineData(720, ResponsiveWidthMode.Compact)]
    [InlineData(1099, ResponsiveWidthMode.Compact)]
    [InlineData(1100, ResponsiveWidthMode.Standard)]
    [InlineData(1399, ResponsiveWidthMode.Standard)]
    [InlineData(1400, ResponsiveWidthMode.Wide)]
    public void ResponsiveWidthMode_UsesLogicalWidth(double width, ResponsiveWidthMode expected)
        => Assert.Equal(expected, ResponsiveLayout.WidthMode(width));
}
