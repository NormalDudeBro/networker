using NetOps.Core.NetTools.Playbooks;

namespace NetOps.Core.Tests.NetTools;

public class PlaybookGeneratorTests
{
    [Theory]
    [InlineData("new-switch")]
    [InlineData("bgp-flap")]
    [InlineData("high-cpu")]
    [InlineData("interface-down")]
    [InlineData("ospf-adjacency")]
    [InlineData("security-hardening")]
    public void Generate_KnownScenarios_ProduceSteps(string scenario)
    {
        var playbook = PlaybookGenerator.Generate(scenario);

        Assert.Equal(scenario, playbook.Name);
        Assert.NotEmpty(playbook.Steps);
        foreach (var step in playbook.Steps)
        {
            Assert.False(string.IsNullOrWhiteSpace(step.Title));
            Assert.False(string.IsNullOrWhiteSpace(step.Command));
            Assert.False(string.IsNullOrWhiteSpace(step.Expected));
            Assert.False(string.IsNullOrWhiteSpace(step.Reasoning));
        }
    }

    [Fact]
    public void Generate_UnknownScenario_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PlaybookGenerator.Generate("nope"));
    }

    [Fact]
    public void RenderMarkdown_IncludesStepsAndCodeBlocks()
    {
        var playbook = PlaybookGenerator.Generate("bgp-flap");
        var markdown = PlaybookGenerator.RenderMarkdown(playbook);

        Assert.Contains("# bgp-flap", markdown);
        Assert.Contains("## Step 1:", markdown);
        Assert.Contains("```", markdown);
        Assert.Contains("**Expected:**", markdown);
    }

    [Fact]
    public void KnownScenarios_ListsAllSupported()
    {
        Assert.Contains("high-cpu", PlaybookGenerator.KnownScenarios);
        Assert.Contains("interface-down", PlaybookGenerator.KnownScenarios);
    }
}
