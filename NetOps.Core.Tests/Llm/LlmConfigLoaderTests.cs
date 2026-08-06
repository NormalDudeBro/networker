using System.IO;
using NetOps.Core.Llm;

namespace NetOps.Core.Tests.Llm;

public class LlmConfigLoaderTests
{
    [Fact]
    public void Load_WithNoSources_UsesDefaults()
    {
        var config = LlmConfigLoader.Load(envFileSearchDirectories: TempDir());
        Assert.Equal(LlmProviderKind.Ollama, config.Provider);
        Assert.Equal("http://localhost:11434", config.OllamaHost);
        Assert.Equal(new[] { LlmProviderKind.Ollama }, config.FallbackChain);
    }

    [Fact]
    public void Load_FromDotEnv_AppliesValues()
    {
        var dir = TempDir();
        File.WriteAllText(Path.Combine(dir, ".env"), """
            LLM_PROVIDER=gemini
            GEMINI_API_KEY=secret-key
            GEMINI_MODEL=gemini-2.0-flash
            OLLAMA_HOST=http://ollama.local:8080
            """);

        var config = LlmConfigLoader.Load(envFileSearchDirectories: dir);

        Assert.Equal(LlmProviderKind.Gemini, config.Provider);
        Assert.Equal("secret-key", config.GeminiApiKey);
        Assert.Equal("gemini-2.0-flash", config.GeminiModel);
        Assert.Equal("http://ollama.local:8080", config.OllamaHost);
    }

    [Fact]
    public void Load_OverridesBeatDotEnv()
    {
        var dir = TempDir();
        File.WriteAllText(Path.Combine(dir, ".env"), """
            OLLAMA_HOST=http://from-file:11434
            OLLAMA_MODEL=llama3.2
            """);

        var config = LlmConfigLoader.Load(
            new LlmEnvOverrides { OllamaHost = "http://from-app:11435", OllamaModel = "qwen2.5" },
            dir);

        Assert.Equal("http://from-app:11435", config.OllamaHost);
        Assert.Equal("qwen2.5", config.OllamaModel);
    }

    [Fact]
    public void Load_RealEnvironmentBeatsDotEnv()
    {
        const string key = "OLLAMA_HOST";
        var previous = Environment.GetEnvironmentVariable(key);
        var dir = TempDir();
        File.WriteAllText(Path.Combine(dir, ".env"), "OLLAMA_HOST=http://from-file:11434\n");

        try
        {
            Environment.SetEnvironmentVariable(key, "http://from-env:9999");
            var config = LlmConfigLoader.Load(envFileSearchDirectories: dir);
            Assert.Equal("http://from-env:9999", config.OllamaHost);
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, previous);
        }
    }

    [Fact]
    public void Load_FallbackChain_ParsesAndDeduplicates()
    {
        var dir = TempDir();
        File.WriteAllText(Path.Combine(dir, ".env"), "LLM_FALLBACK_CHAIN=ollama,grok,grok,gemini\n");

        var config = LlmConfigLoader.Load(envFileSearchDirectories: dir);

        Assert.Equal(
            new[] { LlmProviderKind.Ollama, LlmProviderKind.Grok, LlmProviderKind.Gemini },
            config.FallbackChain);
    }

    [Fact]
    public void Load_IgnoresCommentsAndQuotes()
    {
        var dir = TempDir();
        File.WriteAllText(Path.Combine(dir, ".env"), """
            # comment
            GEMINI_API_KEY="quoted-value"

            XAI_MODEL='grok-quoted'
            """);

        var config = LlmConfigLoader.Load(envFileSearchDirectories: dir);

        Assert.Equal("quoted-value", config.GeminiApiKey);
        Assert.Equal("grok-quoted", config.XaiModel);
    }

    [Fact]
    public void ParseProvider_HandlesAliases()
    {
        Assert.Equal(LlmProviderKind.Ollama, LlmConfig.ParseProvider("ollama"));
        Assert.Equal(LlmProviderKind.Grok, LlmConfig.ParseProvider("xai"));
        Assert.Equal(LlmProviderKind.Grok, LlmConfig.ParseProvider("Grok"));
        Assert.Equal(LlmProviderKind.Gemini, LlmConfig.ParseProvider("google"));
        Assert.Equal(LlmProviderKind.Ollama, LlmConfig.ParseProvider("bogus"));
        Assert.Equal(LlmProviderKind.Ollama, LlmConfig.ParseProvider(null));
    }

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "netops-config-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
