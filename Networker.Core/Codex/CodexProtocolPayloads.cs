namespace Networker.Core.Codex;

/// <summary>
/// Stable v2 app-server request shapes. Field names match official generated schemas
/// (ThreadStartParams, TurnStartParams, UserInput).
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

    public static object AgentThreadStart(string model)
    {
        var body = new Dictionary<string, object?>
        {
            ["model"] = NullIfEmpty(model),
            // Assist is explicitly user-authorized.
            // The native Windows workspace sandbox can deadlock during account setup,
            // so command execution uses Codex's unsandboxed policy and approval flow.
            ["approvalPolicy"] = "on-request",
            ["sandbox"] = "danger-full-access",
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
        string? effort)
    {
        var body = (Dictionary<string, object?>)TurnStart(threadId, text, model, effort);
        body["approvalPolicy"] = "on-request";
        // Repeat the policy per turn because app-server resolves execution mode
        // from turn overrides as well as thread defaults.
        body["sandboxPolicy"] = new Dictionary<string, object?> { ["type"] = "dangerFullAccess" };
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
