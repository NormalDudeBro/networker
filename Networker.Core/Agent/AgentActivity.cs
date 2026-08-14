namespace Networker.Core.Agent;

/// <summary>
/// A structured activity event raised during an agent run. The chat surface
/// maps these to turn blocks (thinking / tool / edit / activity / text) so the
/// user can see what the agent is doing, distinct from its final answer.
/// </summary>
public sealed record AgentActivity(
    string Action,
    string Detail,
    bool IsError = false,
    string? Kind = null,
    string? State = null,
    string? Output = null,
    string? Path = null,
    string? CallId = null,
    int? Additions = null,
    int? Deletions = null,
    string? Verdict = null,
    double? DurationSeconds = null,
    bool IsStreaming = false);
