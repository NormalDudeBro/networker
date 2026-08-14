namespace Networker.Core.Codex;

/// <summary>
/// Stable v2 app-server request shapes. Field names match official generated schemas
/// (ThreadStartParams, TurnStartParams, SandboxPolicy, UserInput).
/// </summary>
public static class CodexProtocolPayloads
{
    public static object ChatThreadStart(string model, string? effort, string? developerInstructions)
    {
        var body = new Dictionary<string, object?>
        {
            ["model"] = NullIfEmpty(model),
            ["approvalPolicy"] = "never",
            ["sandbox"] = "read-only",
            ["ephemeral"] = false,
        };
        // ThreadStartParams has no effort field; effort is applied on turn/start.
        if (!string.IsNullOrWhiteSpace(developerInstructions))
            body["developerInstructions"] = developerInstructions;
        return body;
    }

    public static object AgentThreadStart(string model, string cwd, bool networkEnabled)
    {
        var body = new Dictionary<string, object?>
        {
            ["model"] = NullIfEmpty(model),
            ["cwd"] = cwd,
            ["approvalPolicy"] = "never",
            ["sandbox"] = "workspace-write",
            // Official config.toml equivalent: [sandbox_workspace_write] network_access = bool
            ["config"] = new Dictionary<string, object?>
            {
                ["sandbox_workspace_write"] = new Dictionary<string, object?>
                {
                    ["network_access"] = networkEnabled,
                },
            },
        };
        return body;
    }

    public static object TurnStart(string threadId, string text, string? model, string? effort)
    {
        var body = new Dictionary<string, object?>
        {
            ["threadId"] = threadId,
            ["input"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["type"] = "text",
                    ["text"] = text,
                    ["text_elements"] = Array.Empty<object>(),
                },
            },
        };
        if (!string.IsNullOrWhiteSpace(model)) body["model"] = model;
        // TurnStartParams uses "effort", not "reasoningEffort".
        if (!string.IsNullOrWhiteSpace(effort)) body["effort"] = effort;
        return body;
    }

    public static object AgentTurnStart(
        string threadId,
        string text,
        string? model,
        string? effort,
        string cwd,
        bool networkEnabled)
    {
        var body = (Dictionary<string, object?>)TurnStart(threadId, text, model, effort);
        body["cwd"] = cwd;
        body["approvalPolicy"] = "never";
        body["sandboxPolicy"] = new Dictionary<string, object?>
        {
            ["type"] = "workspaceWrite",
            // Additional roots beyond cwd; cwd is already the thread working directory.
            ["writableRoots"] = Array.Empty<object>(),
            ["networkAccess"] = networkEnabled,
            ["excludeTmpdirEnvVar"] = false,
            ["excludeSlashTmp"] = false,
        };
        return body;
    }

    public static object TextInput(string text) => new Dictionary<string, object?>
    {
        ["type"] = "text",
        ["text"] = text,
        ["text_elements"] = Array.Empty<object>(),
    };

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;
}
