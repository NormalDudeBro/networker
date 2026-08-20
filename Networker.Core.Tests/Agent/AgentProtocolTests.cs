using Networker.Core.Agent;

namespace Networker.Core.Tests.Agent;

public sealed class AgentProtocolTests
{
    [Theory]
    [InlineData("prose {\"action\":\"finish\",\"summary\":\"done\"}")]
    [InlineData("{\"action\":\"finish\",\"summary\":\"done\"} trailing")]
    [InlineData("{\"action\":\"unknown\"}")]
    [InlineData("{\"action\":\"finish\",\"summary\":\"done\",\"extra\":true}")]
    [InlineData("{\"action\":\"command\",\"executable\":\"cmd.exe\",\"arguments\":[\"/c\",\"whoami\"],\"workingDirectory\":\"C:/tmp\"}")]
    public void Parse_RejectsNonconformingInstruction(string value)
    {
        Assert.ThrowsAny<Exception>(() => AgentOrchestrator.Parse(value));
    }

    [Fact]
    public void Parse_AcceptsExactTypedInstruction()
    {
        AgentOrchestrator.AgentInstruction instruction = AgentOrchestrator.Parse("{\"action\":\"command\",\"executable\":\"cmd.exe\",\"arguments\":[\"/c\",\"whoami\"],\"timeoutSeconds\":30}");
        Assert.Equal("command", instruction.Action);
        Assert.Equal("cmd.exe", instruction.Executable);
    }

    [Fact]
    public void Parse_AcceptsAbsoluteGlobalPath()
    {
        AgentOrchestrator.AgentInstruction instruction = AgentOrchestrator.Parse("{\"action\":\"read\",\"path\":\"C:\\\\Windows\\\\win.ini\"}");
        Assert.Equal(@"C:\Windows\win.ini", instruction.Path);
    }
}
