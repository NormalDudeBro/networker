using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using NetOps.Core.Llm;

namespace NetOps.Core.Tests.Llm;

public class GeminiProviderTests
{
    private static LlmConfig Config() => new() { GeminiApiKey = "test-key", GeminiModel = "gemini-2.5-flash" };

    [Fact]
    public async Task Complete_ReturnsJoinedTextParts()
    {
        var http = StubHttpMessageHandler.Client(_ => StubHttpMessageHandler.Json(
            """{"candidates":[{"content":{"parts":[{"text":"Hello"},{"text":" world"}]}}]}"""));

        var provider = new GeminiProvider(Config(), http);
        var response = await provider.CompleteAsync(new[] { LlmMessage.User("hi") });

        Assert.Equal("Hello world", response.Content);
        Assert.Equal("Gemini", response.Provider);
    }

    [Fact]
    public async Task Complete_ThrowsWhenNotConfigured()
    {
        var http = StubHttpMessageHandler.Client(_ => throw new System.InvalidOperationException("should not be called"));
        var provider = new GeminiProvider(new LlmConfig { GeminiApiKey = null }, http);

        await Assert.ThrowsAsync<LlmException>(() => provider.CompleteAsync(new[] { LlmMessage.User("hi") }));
    }

    [Fact]
    public async Task Stream_YieldsTextPartsFromCandidates()
    {
        var http = StubHttpMessageHandler.Client(_ => StubHttpMessageHandler.Sse(
            """{"candidates":[{"content":{"parts":[{"text":"Hel"}]}}]}""",
            """{"candidates":[{"content":{"parts":[{"text":"lo"}]}}]}"""));
        http.Timeout = System.TimeSpan.FromSeconds(30);

        var provider = new GeminiProvider(Config(), http);
        var deltas = new List<string>();
        await foreach (var d in provider.StreamAsync(new[] { LlmMessage.User("hi") }))
        {
            deltas.Add(d);
        }

        Assert.Equal(new[] { "Hel", "lo" }, deltas);
    }

    [Fact]
    public async Task ListModels_StripsModelsPrefix()
    {
        var http = StubHttpMessageHandler.Client(_ => StubHttpMessageHandler.Json(
            """{"models":[{"name":"models/gemini-2.5-flash","displayName":"Gemini 2.5 Flash"}]}"""));

        var provider = new GeminiProvider(Config(), http);
        var models = await provider.ListModelsAsync();

        Assert.Single(models);
        Assert.Equal("gemini-2.5-flash", models[0].Id);
        Assert.Equal("Gemini 2.5 Flash", models[0].Name);
    }

    [Fact]
    public async Task HealthCheck_FalseWithoutKey()
    {
        var http = StubHttpMessageHandler.Client(_ => throw new System.InvalidOperationException("should not be called"));
        var provider = new GeminiProvider(new LlmConfig { GeminiApiKey = null }, http);
        Assert.False(await provider.HealthCheckAsync());
    }

    [Fact]
    public async Task Request_UsesApiKeyHeaderAndMapsAssistantToModelRole()
    {
        var http = StubHttpMessageHandler.Client(request =>
        {
            Assert.Equal("test-key", request.Headers.GetValues("x-goog-api-key").Single());
            var body = request.Content!.ReadAsStringAsync().Result;
            using var doc = JsonDocument.Parse(body);
            Assert.Equal("user", doc.RootElement.GetProperty("contents")[0].GetProperty("role").GetString());
            Assert.Equal("model", doc.RootElement.GetProperty("contents")[1].GetProperty("role").GetString());
            Assert.Equal("sys-instruction", doc.RootElement.GetProperty("systemInstruction").GetProperty("parts")[0].GetProperty("text").GetString());
            return StubHttpMessageHandler.Json("""{"candidates":[{"content":{"parts":[{"text":"ok"}]}}]}""");
        });

        var provider = new GeminiProvider(Config(), http);
        var response = await provider.CompleteAsync(new[]
        {
            LlmMessage.System("sys-instruction"),
            LlmMessage.User("hi"),
            LlmMessage.Assistant("prev"),
        });

        Assert.Equal("ok", response.Content);
    }
}
