using System.Text.Json;
using Networker.Core.Codex;

namespace Networker.Core.Tests.Codex;

public sealed class CodexProtocolPayloadsTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void ChatThreadStart_IsReadOnlyNeverApproval()
    {
        string json = Serialize(CodexProtocolPayloads.ChatThreadStart("gpt-test", "high", "sys"));
        using var doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        Assert.Equal("read-only", root.GetProperty("sandbox").GetString());
        Assert.Equal("never", root.GetProperty("approvalPolicy").GetString());
        Assert.Equal("gpt-test", root.GetProperty("model").GetString());
        Assert.Equal("sys", root.GetProperty("developerInstructions").GetString());
        Assert.False(root.TryGetProperty("reasoningEffort", out _));
        Assert.False(root.TryGetProperty("effort", out _));
        Assert.False(root.TryGetProperty("networkAccessEnabled", out _));
    }

    [Fact]
    public void AgentThreadStart_UsesAuthorizedGlobalExecution()
    {
        string json = Serialize(CodexProtocolPayloads.AgentThreadStart("gpt-test"));
        using var doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        Assert.Equal("danger-full-access", root.GetProperty("sandbox").GetString());
        Assert.Equal("on-request", root.GetProperty("approvalPolicy").GetString());
        Assert.False(root.TryGetProperty("cwd", out _));
        Assert.False(root.TryGetProperty("config", out _));
    }

    [Fact]
    public void TurnStart_UsesEffortNotReasoningEffort()
    {
        string json = Serialize(CodexProtocolPayloads.TurnStart("thread-1", "hello", "gpt-test", "xhigh"));
        using var doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        Assert.Equal("thread-1", root.GetProperty("threadId").GetString());
        Assert.Equal("xhigh", root.GetProperty("effort").GetString());
        Assert.False(root.TryGetProperty("reasoningEffort", out _));
        JsonElement input0 = root.GetProperty("input")[0];
        Assert.Equal("text", input0.GetProperty("type").GetString());
        Assert.Equal("hello", input0.GetProperty("text").GetString());
        Assert.Equal(JsonValueKind.Array, input0.GetProperty("text_elements").ValueKind);
    }

    [Fact]
    public void AgentTurnStart_RepeatsAuthorizedExecutionPolicyPerTurn()
    {
        string json = Serialize(CodexProtocolPayloads.AgentTurnStart(
            "t1", "goal", "model", "medium"));
        using var doc = JsonDocument.Parse(json);
        JsonElement policy = doc.RootElement.GetProperty("sandboxPolicy");
        Assert.Equal("dangerFullAccess", policy.GetProperty("type").GetString());
        Assert.Single(policy.EnumerateObject());
        Assert.Equal("on-request", doc.RootElement.GetProperty("approvalPolicy").GetString());
        Assert.Equal("medium", doc.RootElement.GetProperty("effort").GetString());
        Assert.False(doc.RootElement.TryGetProperty("cwd", out _));
    }

    [Fact]
    public void Payloads_NeverIncludeApiKeyOrTokenFields()
    {
        string[] payloads =
        {
            Serialize(CodexProtocolPayloads.ChatThreadStart("m", "low", null)),
            Serialize(CodexProtocolPayloads.AgentThreadStart("m")),
            Serialize(CodexProtocolPayloads.TurnStart("t", "hi", "m", "low")),
            Serialize(CodexProtocolPayloads.AgentTurnStart("t", "g", "m", "low")),
        };
        foreach (string json in payloads)
        {
            string lower = json.ToLowerInvariant();
            Assert.DoesNotContain("api_key", lower);
            Assert.DoesNotContain("apikey", lower);
            Assert.DoesNotContain("openai_api_key", lower);
            Assert.DoesNotContain("access_token", lower);
            Assert.DoesNotContain("refresh_token", lower);
            Assert.DoesNotContain("authorization", lower);
        }
    }

    private static string Serialize(object value) => JsonSerializer.Serialize(value, Json);
}
