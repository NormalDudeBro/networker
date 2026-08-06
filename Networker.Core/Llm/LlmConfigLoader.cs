namespace Networker.Core.Llm;

public sealed class LlmEnvOverrides
{
    public string? OllamaHost { get; set; }
    public string? OllamaModel { get; set; }
    public string? OllamaApiKey { get; set; }
    public string? XaiApiKey { get; set; }
    public string? XaiModel { get; set; }
    public string? GeminiApiKey { get; set; }
    public string? GeminiModel { get; set; }
}

public static class LlmConfigLoader
{
    private const string ProviderKey = "LLM_PROVIDER";
    private const string ChainKey = "LLM_FALLBACK_CHAIN";

    /// <summary>
    /// Loads LLM configuration. Precedence (highest first):
    /// real process environment variables, then app-provided overrides,
    /// then values from a .env file, then built-in defaults.
    /// </summary>
    public static LlmConfig Load(
        LlmEnvOverrides? overrides = null,
        params string[] envFileSearchDirectories)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var kvp in LoadDotEnvFiles(envFileSearchDirectories))
        {
            merged[kvp.Key] = kvp.Value;
        }

        foreach (var kvp in ToEnvOverrides(Environment.GetEnvironmentVariables()))
        {
            merged[kvp.Key] = kvp.Value;
        }

        if (overrides is not null)
        {
            if (overrides.OllamaHost is not null)
            {
                merged["OLLAMA_HOST"] = overrides.OllamaHost;
            }

            if (overrides.OllamaModel is not null)
            {
                merged["OLLAMA_MODEL"] = overrides.OllamaModel;
            }

            if (overrides.OllamaApiKey is not null)
            {
                merged["OLLAMA_API_KEY"] = overrides.OllamaApiKey;
            }

            if (overrides.XaiApiKey is not null)
            {
                merged["XAI_API_KEY"] = overrides.XaiApiKey;
            }

            if (overrides.XaiModel is not null)
            {
                merged["XAI_MODEL"] = overrides.XaiModel;
            }

            if (overrides.GeminiApiKey is not null)
            {
                merged["GEMINI_API_KEY"] = overrides.GeminiApiKey;
            }

            if (overrides.GeminiModel is not null)
            {
                merged["GEMINI_MODEL"] = overrides.GeminiModel;
            }
        }

        return Apply(merged);
    }

    private static Dictionary<string, string> ToEnvOverrides(System.Collections.IDictionary environment)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in environment.Keys)
        {
            if (key is string envKey && IsKnownKey(envKey))
            {
                var value = environment[envKey]?.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    result[envKey] = value;
                }
            }
        }

        return result;
    }

    private static bool IsKnownKey(string key) => key switch
    {
        ProviderKey or ChainKey or
        "OLLAMA_HOST" or "OLLAMA_API_KEY" or "OLLAMA_MODEL" or
        "XAI_API_KEY" or "XAI_MODEL" or
        "GEMINI_API_KEY" or "GEMINI_MODEL" => true,
        _ => false,
    };

    private static LlmConfig Apply(IReadOnlyDictionary<string, string> values)
    {
        var config = new LlmConfig();

        if (values.TryGetValue(ProviderKey, out var provider))
        {
            config.Provider = LlmConfig.ParseProvider(provider);
        }

        if (values.TryGetValue(ChainKey, out var chain))
        {
            config.FallbackChain = ParseChain(chain);
        }

        if (values.TryGetValue("OLLAMA_HOST", out var host) && !string.IsNullOrWhiteSpace(host))
        {
            config.OllamaHost = host;
        }

        if (values.TryGetValue("OLLAMA_API_KEY", out var ollamaKey))
        {
            config.OllamaApiKey = ollamaKey;
        }

        if (values.TryGetValue("OLLAMA_MODEL", out var ollamaModel) && !string.IsNullOrWhiteSpace(ollamaModel))
        {
            config.OllamaModel = ollamaModel;
        }

        if (values.TryGetValue("XAI_API_KEY", out var xaiKey))
        {
            config.XaiApiKey = xaiKey;
        }

        if (values.TryGetValue("XAI_MODEL", out var xaiModel) && !string.IsNullOrWhiteSpace(xaiModel))
        {
            config.XaiModel = xaiModel;
        }

        if (values.TryGetValue("GEMINI_API_KEY", out var geminiKey))
        {
            config.GeminiApiKey = geminiKey;
        }

        if (values.TryGetValue("GEMINI_MODEL", out var geminiModel) && !string.IsNullOrWhiteSpace(geminiModel))
        {
            config.GeminiModel = geminiModel;
        }

        if (config.FallbackChain.Count == 0)
        {
            config.FallbackChain = new[] { config.Provider };
        }

        return config;
    }

    private static IReadOnlyList<LlmProviderKind> ParseChain(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<LlmProviderKind>();
        }

        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(LlmConfig.ParseProvider)
            .Distinct()
            .ToList();
    }

    private static IReadOnlyDictionary<string, string> LoadDotEnvFiles(params string[] searchDirectories)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<string>(searchDirectories.Where(d => !string.IsNullOrWhiteSpace(d)));
        candidates.Add(Environment.CurrentDirectory);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in candidates)
        {
            var path = System.IO.Path.Combine(dir, ".env");
            var full = System.IO.Path.GetFullPath(path);
            if (seen.Add(full) && System.IO.File.Exists(full))
            {
                ParseDotEnvFile(full, values);
            }
        }

        return values;
    }

    private static void ParseDotEnvFile(string path, Dictionary<string, string> values)
    {
        foreach (var rawLine in System.IO.File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            var equalsIndex = line.IndexOf('=');
            if (equalsIndex <= 0)
            {
                continue;
            }

            var key = line[..equalsIndex].Trim();
            var value = line[(equalsIndex + 1)..].Trim();

            if (value.Length >= 2)
            {
                var first = value[0];
                var last = value[^1];
                if ((first == '"' && last == '"') || (first == '\'' && last == '\''))
                {
                    value = value[1..^1];
                }
            }

            if (key.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            values[key] = value;
        }
    }
}

