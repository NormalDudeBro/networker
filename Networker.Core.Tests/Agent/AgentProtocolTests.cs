using Networker.Core.Agent;

namespace Networker.Core.Tests.Agent;

public sealed class AgentProtocolTests
{
    [Theory]
    [InlineData("prose {\"action\":\"finish\",\"summary\":\"done\"}")]
    [InlineData("{\"action\":\"finish\",\"summary\":\"done\"} trailing")]
    [InlineData("{\"action\":\"unknown\"}")]
    [InlineData("{\"action\":\"finish\",\"summary\":\"done\",\"extra\":true}")]
    [InlineData("{\"action\":\"command\",\"executable\":\"cmd\",\"arguments\":[\"/c\",\"whoami\"]}")]
    [InlineData("{\"action\":\"read\",\"path\":\"../secret\"}")]
    public void Parse_RejectsNonconformingInstruction(string value)
    {
        Assert.ThrowsAny<Exception>(() => AgentOrchestrator.Parse(value));
    }

    [Fact]
    public void Parse_AcceptsExactTypedInstruction()
    {
        AgentOrchestrator.AgentInstruction instruction = AgentOrchestrator.Parse("{\"action\":\"command\",\"executable\":\"dotnet\",\"arguments\":[\"test\"],\"workingDirectory\":\"src\",\"timeoutSeconds\":30}");
        Assert.Equal("command", instruction.Action);
        Assert.Equal("dotnet", instruction.Executable);
    }
}
