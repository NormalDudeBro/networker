namespace NetOps.Core.Llm;

public sealed class LlmMessage
{
    public LlmMessage(string role, string content)
    {
        Role = role;
        Content = content;
    }

    public string Role { get; }
    public string Content { get; }

    public static LlmMessage System(string content) => new("system", content);
    public static LlmMessage User(string content) => new("user", content);
    public static LlmMessage Assistant(string content) => new("assistant", content);
}
