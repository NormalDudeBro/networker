using System.Net.Http;

namespace Networker.Core.Llm;

public static class LlmProviderFactory
{
    public static ILlmProvider Create(LlmProviderKind kind, LlmConfig config, HttpClient http)
    {
        return kind switch
        {
            LlmProviderKind.Ollama => new OllamaProvider(config, http),
            LlmProviderKind.Grok => new GrokProvider(config, http),
            LlmProviderKind.Gemini => new GeminiProvider(config, http),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown provider kind."),
        };
    }
}

