namespace Networker.Core.Llm;

public sealed class LlmConfig
{
    public LlmProviderKind Provider { get; set; } = LlmProviderKind.Ollama;

    public IReadOnlyList<LlmProviderKind> FallbackChain { get; set; } = Array.Empty<LlmProviderKind>();

    public string OllamaHost { get; set; } = "http://localhost:11434";
    public string? OllamaApiKey { get; set; }
    public string? OllamaModel { get; set; }

    public string? XaiApiKey { get; set; }
    public string? XaiModel { get; set; } = "grok-3";

    public string? GeminiApiKey { get; set; }
    public string? GeminiModel { get; set; } = "gemini-2.5-flash";

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(120);
    public int RetryCount { get; set; } = 2;
    public TimeSpan BaseRetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    public static LlmProviderKind ParseProvider(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return LlmProviderKind.Ollama;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "ollama" => LlmProviderKind.Ollama,
            "grok" or "xai" => LlmProviderKind.Grok,
            "gemini" or "google" => LlmProviderKind.Gemini,
            _ => LlmProviderKind.Ollama,
        };
    }

    public string ProviderDisplayName(LlmProviderKind kind) => kind switch
    {
        LlmProviderKind.Ollama => "Ollama",
        LlmProviderKind.Grok => "Grok (xAI)",
        LlmProviderKind.Gemini => "Gemini",
        _ => kind.ToString(),
    };
}

