using Networker.Core.Prompting;
using Xunit;

namespace Networker.Core.Tests;

public class PromptBuilderTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("   ", "\n  ")]
    public void BuildUserMessage_NoGlobalPrompt_ReturnsMessageUnchanged(string? system, string? custom)
    {
        const string message = "  show running-config  ";
        string result = PromptBuilder.BuildUserMessage(system, custom, message);
        Assert.Equal(message, result);
    }

    [Fact]
    public void BuildUserMessage_Ordering_IsSystemThenCustomThenUser()
    {
        string result = PromptBuilder.BuildUserMessage("You are a NOC assistant.", "Always be concise.", "Troubleshoot BGP flapping.");
        Assert.Equal("You are a NOC assistant.\n\nAlways be concise.\n\nTroubleshoot BGP flapping.", result);
    }

    [Fact]
    public void BuildUserMessage_OnlyCustom_OmitsSystem()
    {
        string result = PromptBuilder.BuildUserMessage("", "Answer in Spanish.", "¿Qué es OSPF?");
        Assert.Equal("Answer in Spanish.\n\n¿Qué es OSPF?", result);
    }

    [Fact]
    public void BuildUserMessage_OnlySystem_OmitsCustom()
    {
        string result = PromptBuilder.BuildUserMessage("You are a helper.", null, "Hello");
        Assert.Equal("You are a helper.\n\nHello", result);
    }

    [Fact]
    public void BuildUserMessage_PreservesMultiLineFormatting()
    {
        string system = "Rule 1: no destructive commands\nRule 2: cite sources";
        string result = PromptBuilder.BuildUserMessage(system, "", "User\nline");
        Assert.Contains("Rule 1: no destructive commands\nRule 2: cite sources\n\nUser\nline", result);
    }

    [Fact]
    public void BuildUserMessage_NeverDuplicatesPrompt()
    {
        string result = PromptBuilder.BuildUserMessage("P", "P", "P");
        Assert.Equal("P\n\nP\n\nP", result);
    }

    [Fact]
    public void JoinNonEmpty_MergesSectionsInOrder()
    {
        string result = PromptBuilder.JoinNonEmpty("Tool prompt.", "Global prompt.", "Custom instructions.");
        Assert.Equal("Tool prompt.\n\nGlobal prompt.\n\nCustom instructions.", result);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "  ")]
    [InlineData("   ", null)]
    public void JoinNonEmpty_AllEmpty_ReturnsEmpty(string? a, string? b)
    {
        Assert.Equal("", PromptBuilder.JoinNonEmpty(a, b));
    }

    [Fact]
    public void JoinNonEmpty_OmitsEmptyMiddleSection()
    {
        string result = PromptBuilder.JoinNonEmpty("First", "   ", "Last");
        Assert.Equal("First\n\nLast", result);
    }

    [Fact]
    public void JoinNonEmpty_TrimsEachSection()
    {
        string result = PromptBuilder.JoinNonEmpty("  First  ", "\nSecond\n");
        Assert.Equal("First\n\nSecond", result);
    }
}

