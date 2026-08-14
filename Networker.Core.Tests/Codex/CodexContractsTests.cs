using Networker.Core.Codex;
using Networker.Core.Llm;

namespace Networker.Core.Tests.Codex;

public sealed class CodexContractsTests
{
    [Fact]
    public void ParseProvider_MapsLegacyChatGptToCodex()
    {
        Assert.Equal(LlmProviderKind.Codex, LlmConfig.ParseProvider("chatgpt"));
        Assert.Equal(LlmProviderKind.Codex, LlmConfig.ParseProvider("codex"));
        Assert.Equal(LlmProviderKind.Codex, LlmConfig.ParseProvider("openai-codex"));
    }

    [Fact]
    public void DisconnectedAccount_HasNoIdentityFields()
    {
        CodexAccount account = CodexAccount.Disconnected("Not connected");
        Assert.False(account.IsConnected);
        Assert.Null(account.Email);
        Assert.Null(account.PlanType);
        Assert.Null(account.AuthMode);
        Assert.Equal("Not connected", account.Message);
    }

    [Fact]
    public void ModelDescriptor_PreservesOrderedEfforts()
    {
        var efforts = new[]
        {
            new CodexReasoningOption("low", "Faster"),
            new CodexReasoningOption("xhigh", "Maximum"),
        };
        var model = new CodexModelDescriptor(
            "gpt-test",
            "Test",
            "desc",
            true,
            "low",
            efforts,
            new[] { "text" });

        Assert.Equal(new[] { "low", "xhigh" }, model.SupportedReasoningEfforts.Select(item => item.Id));
        Assert.Equal("low", model.DefaultReasoningEffort);
    }

    [Fact]
    public void ProtocolException_CanCarryOptionalCode()
    {
        var error = new CodexProtocolException("denied", -32000);
        Assert.Equal(-32000, error.Code);
        Assert.Equal("denied", error.Message);
    }
}
