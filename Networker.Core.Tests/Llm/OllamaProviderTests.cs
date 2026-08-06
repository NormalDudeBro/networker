using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Networker.Core.Llm;

namespace Networker.Core.Tests.Llm;

public class OllamaProviderTests
{
    private static LlmConfig Config(string host = "http://localhost:11434", string model = "llama3.2")
        => new() { OllamaHost = host, OllamaModel = model };

    [Fact]
    public async Task Complete_ReturnsAssistantContent()
    {
        var http = StubHttpMessageHandler.Client(_ => StubHttpMessageHandler.Json(
            """{"model":"llama3.2","message":{"role":"assistant","content":"Hello there"},"done":true}"""));

        var provider = new OllamaProvider(Config(), http);
        var response = await provider.CompleteAsync(new[] { LlmMessage.User("hi") });

        Assert.Equal("Hello there", response.Content);
        Assert.Equal("Ollama", response.Provider);
        Assert.Equal("llama3.2", response.Model);
    }

    [Fact]
    public async Task Complete_ThrowsWhenNoModelSelected()
    {
        var http = StubHttpMessageHandler.Client(_ => throw new System.InvalidOperationException("should not be called"));
        var provider = new OllamaProvider(Config(model: string.Empty), http);

        await Assert.ThrowsAsync<LlmException>(() => provider.CompleteAsync(new[] { LlmMessage.User("hi") }));
    }

    [Fact]
    public async Task Complete_SurfacesHttpErrors()
    {
        var http = StubHttpMessageHandler.Client(_ => StubHttpMessageHandler.Json(
            """{"error":"model not found"}""", HttpStatusCode.NotFound));

        var provider = new OllamaProvider(Config(), http);

        var ex = await Assert.ThrowsAsync<LlmException>(() => provider.CompleteAsync(new[] { LlmMessage.User("hi") }));
        Assert.Contains("model not found", ex.Message);
    }

    [Fact]
    public async Task Stream_YieldsDeltasAndStopsAtDone()
    {
        var http = StubHttpMessageHandler.Client(_ => StubHttpMessageHandler.NdJson(
            """{"message":{"content":"Hello"},"done":false}""",
            """{"message":{"content":" world"},"done":false}""",
            """{"message":{"content":""},"done":true}"""));

        var provider = new OllamaProvider(Config(), http);
        var deltas = new List<string>();
        await foreach (var d in provider.StreamAsync(new[] { LlmMessage.User("hi") }))
        {
            deltas.Add(d);
        }

        Assert.Equal(new[] { "Hello", " world" }, deltas);
    }

    [Fact]
    public async Task ListModels_ParsesModelNames()
    {
        var http = StubHttpMessageHandler.Client(_ => StubHttpMessageHandler.Json(
            """{"models":[{"name":"llama3.2"},{"name":"mistral"}]}"""));

        var provider = new OllamaProvider(Config(), http);
        var models = await provider.ListModelsAsync();

        Assert.Equal(2, models.Count);
        Assert.Equal("llama3.2", models[0].Id);
        Assert.Equal("mistral", models[1].Id);
    }

    [Fact]
    public async Task HealthCheck_IsTrueOnSuccessFalseOnFailure()
    {
        var okHttp = StubHttpMessageHandler.Client(_ => StubHttpMessageHandler.Json("""{"models":[]}"""));
        var ok = await new OllamaProvider(Config(), okHttp).HealthCheckAsync();
        Assert.True(ok);

        var badHttp = StubHttpMessageHandler.Client(_ => StubHttpMessageHandler.Json("{}", HttpStatusCode.ServiceUnavailable));
        var bad = await new OllamaProvider(Config(), badHttp).HealthCheckAsync();
        Assert.False(bad);
    }

    [Fact]
    public async Task HealthCheck_IsFalseOnConnectionFailure()
    {
        var http = StubHttpMessageHandler.Client(_ => throw new HttpRequestException("connection refused"));
        var result = await new OllamaProvider(Config(), http).HealthCheckAsync();
        Assert.False(result);
    }
}

