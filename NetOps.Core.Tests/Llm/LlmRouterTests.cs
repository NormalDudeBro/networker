using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using NetOps.Core.Llm;

namespace NetOps.Core.Tests.Llm;

public class LlmRouterTests
{
    private static LlmConfig Config() => new()
    {
        Provider = LlmProviderKind.Ollama,
        OllamaHost = "http://localhost:9",
        OllamaModel = "llama3.2",
        XaiApiKey = "test-key",
        XaiModel = "grok-3",
        RetryCount = 0,
    };

    private static HttpClient Client()
    {
        return StubHttpMessageHandler.Client(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/api/chat", StringComparison.Ordinal))
            {
                return StubHttpMessageHandler.Json("""{"error":"down"}""", HttpStatusCode.ServiceUnavailable);
            }

            if (request.RequestUri.AbsolutePath.EndsWith("/chat/completions", StringComparison.Ordinal))
            {
                return StubHttpMessageHandler.Json(
                    """{"choices":[{"message":{"role":"assistant","content":"from grok"}}]}""");
            }

            return StubHttpMessageHandler.Json("{}", HttpStatusCode.NotFound);
        });
    }

    [Fact]
    public async Task Complete_FallsBackToNextProvider()
    {
        var router = new LlmRouter(Config(), Client(), new[] { LlmProviderKind.Ollama, LlmProviderKind.Grok });
        var response = await router.CompleteAsync(new[] { LlmMessage.User("hi") });

        Assert.Equal("from grok", response.Content);
        Assert.Equal("Grok (xAI)", response.Provider);
    }

    [Fact]
    public async Task Complete_ThrowsWhenAllProvidersFail()
    {
        var http = StubHttpMessageHandler.Client(_ => StubHttpMessageHandler.Json(
            """{"error":"down"}""", HttpStatusCode.ServiceUnavailable));
        var router = new LlmRouter(Config(), http, new[] { LlmProviderKind.Ollama, LlmProviderKind.Grok });

        var ex = await Assert.ThrowsAsync<LlmException>(
            () => router.CompleteAsync(new[] { LlmMessage.User("hi") }));

        Assert.Contains("All providers failed", ex.Message);
    }

    [Fact]
    public async Task Stream_FallsBackAndYieldsFromWorkingProvider()
    {
        var http = StubHttpMessageHandler.Client(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/api/chat", StringComparison.Ordinal))
            {
                return StubHttpMessageHandler.Json("""{"error":"down"}""", HttpStatusCode.ServiceUnavailable);
            }

            return StubHttpMessageHandler.Sse(
                """{"choices":[{"delta":{"content":"streamed"}}]}""");
        });

        var router = new LlmRouter(Config(), http, new[] { LlmProviderKind.Ollama, LlmProviderKind.Grok });
        var deltas = new List<string>();
        await foreach (var d in router.StreamAsync(new[] { LlmMessage.User("hi") }))
        {
            deltas.Add(d);
        }

        Assert.Equal(new[] { "streamed" }, deltas);
    }

    [Fact]
    public async Task HealthCheckAll_ReportsEachProvider()
    {
        var http = StubHttpMessageHandler.Client(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/api/tags", StringComparison.Ordinal))
            {
                return StubHttpMessageHandler.Json("{}", HttpStatusCode.ServiceUnavailable);
            }

            return StubHttpMessageHandler.Json("""{"data":[]}""");
        });

        var router = new LlmRouter(Config(), http, new[] { LlmProviderKind.Ollama, LlmProviderKind.Grok });
        var statuses = await router.HealthCheckAllAsync();

        Assert.Equal(2, statuses.Count);
        Assert.False(statuses[0].IsAvailable);
        Assert.Equal("Ollama", statuses[0].Provider);
        Assert.True(statuses[1].IsAvailable);
        Assert.Equal("Grok (xAI)", statuses[1].Provider);
    }

    [Fact]
    public async Task ListModels_FallsBackToConfiguredProvider()
    {
        var http = StubHttpMessageHandler.Client(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/api/tags", StringComparison.Ordinal))
            {
                return StubHttpMessageHandler.Json("{}", HttpStatusCode.ServiceUnavailable);
            }

            return StubHttpMessageHandler.Json("""{"data":[{"id":"grok-3"}]}""");
        });

        var router = new LlmRouter(Config(), http, new[] { LlmProviderKind.Ollama, LlmProviderKind.Grok });
        var models = await router.ListModelsAsync();

        Assert.Equal(new[] { "grok-3" }, models.Select(m => m.Id));
    }
}
