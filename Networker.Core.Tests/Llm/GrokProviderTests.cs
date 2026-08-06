using System.Net.Http;
using System.Threading.Tasks;
using Networker.Core.Llm;

namespace Networker.Core.Tests.Llm;

public class GrokProviderTests
{
    private static LlmConfig Config() => new() { XaiApiKey = "test-key", XaiModel = "grok-3" };

    [Fact]
    public async Task Complete_ReturnsAssistantContent()
    {
        var http = StubHttpMessageHandler.Client(_ => StubHttpMessageHandler.Json(
            """{"choices":[{"message":{"role":"assistant","content":"from grok"}}]}"""));

        var provider = new GrokProvider(Config(), http);
        var response = await provider.CompleteAsync(new[] { LlmMessage.User("hi") });

        Assert.Equal("from grok", response.Content);
        Assert.Equal("Grok (xAI)", response.Provider);
        Assert.Equal("grok-3", response.Model);
    }

    [Fact]
    public async Task Complete_ThrowsWhenNotConfigured()
    {
        var http = StubHttpMessageHandler.Client(_ => throw new System.InvalidOperationException("should not be called"));
        var provider = new GrokProvider(new LlmConfig { XaiApiKey = null }, http);

        await Assert.ThrowsAsync<LlmException>(() => provider.CompleteAsync(new[] { LlmMessage.User("hi") }));
    }

    [Fact]
    public async Task Stream_YieldsContentDeltas()
    {
        var http = StubHttpMessageHandler.Client(_ => StubHttpMessageHandler.Sse(
            """{"choices":[{"delta":{"content":"Hel"}}]}""",
            """{"choices":[{"delta":{"content":"lo"}}]}"""));
        http.Timeout = System.TimeSpan.FromSeconds(30);

        var provider = new GrokProvider(Config(), http);
        var deltas = new List<string>();
        await foreach (var d in provider.StreamAsync(new[] { LlmMessage.User("hi") }))
        {
            deltas.Add(d);
        }

        Assert.Equal(new[] { "Hel", "lo" }, deltas);
    }

    [Fact]
    public async Task ListModels_ParsesIds()
    {
        var http = StubHttpMessageHandler.Client(_ => StubHttpMessageHandler.Json(
            """{"data":[{"id":"grok-3"},{"id":"grok-3-mini"}]}"""));

        var provider = new GrokProvider(Config(), http);
        var models = await provider.ListModelsAsync();

        Assert.Equal(new[] { "grok-3", "grok-3-mini" }, models.Select(m => m.Id));
    }

    [Fact]
    public async Task HealthCheck_FalseWithoutKey()
    {
        var http = StubHttpMessageHandler.Client(_ => throw new System.InvalidOperationException("should not be called"));
        var provider = new GrokProvider(new LlmConfig { XaiApiKey = null }, http);
        Assert.False(await provider.HealthCheckAsync());
    }

    [Fact]
    public async Task HealthCheck_FalseOnFailure()
    {
        var http = StubHttpMessageHandler.Client(_ => throw new HttpRequestException("401"));
        var provider = new GrokProvider(Config(), http);
        Assert.False(await provider.HealthCheckAsync());
    }
}

