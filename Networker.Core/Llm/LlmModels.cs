namespace Networker.Core.Llm;

public enum LlmProviderKind
{
    Ollama = 0,
    Grok = 1,
    Gemini = 2,
}

public sealed class LlmResponse
{
    public required string Provider { get; init; }
    public required string Model { get; init; }
    public required string Content { get; init; }
}

public sealed class LlmModelInfo
{
    public required string Id { get; init; }
    public string? Name { get; init; }
}

public sealed class LlmProviderStatus
{
    public required LlmProviderKind Kind { get; init; }
    public required string Provider { get; init; }
    public required bool IsAvailable { get; init; }
    public string? Model { get; init; }
    public string? Message { get; init; }
}

public sealed class LlmException : Exception
{
    public LlmException(string message) : base(message)
    {
    }

    public LlmException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public string? Provider { get; init; }
}

