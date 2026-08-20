namespace Networker.Core.Agent;

/// <summary>One todo entry carried by a plan activity.</summary>
public sealed record AgentPlanItem(string Title, string Status = "pending");

/// <summary>
/// A structured activity event raised during an agent run. The chat surface
/// maps these to turn blocks (thinking / plan / tool / edit / activity / text)
/// so the user can see what the agent is doing, distinct from its final answer.
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
    bool IsStreaming = false,
    string? CommandLine = null,
    int? ExitCode = null,
    bool IsTerminalStyle = false,
    IReadOnlyList<AgentPlanItem>? Plan = null);
