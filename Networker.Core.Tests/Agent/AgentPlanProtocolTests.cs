using Networker.Core.Agent;
using Networker.Core.Llm;

namespace Networker.Core.Tests.Agent;

public sealed class AgentPlanProtocolTests
{
    [Fact]
    public void Parse_AcceptsPlanInstructionAndNormalizesStatuses()
    {
        AgentOrchestrator.AgentInstruction instruction = AgentOrchestrator.Parse(
            "{\"action\":\"plan\",\"plan\":[" +
            "{\"title\":\"first\",\"status\":\"in progress\"}," +
            "{\"title\":\"second\",\"status\":\"DONE\"}," +
            "{\"title\":\"third\",\"status\":\"cancelled\"}," +
            "{\"title\":\"fourth\",\"status\":\"pending\"}]}");

        Assert.Equal("plan", instruction.Action);
        Assert.NotNull(instruction.Plan);
        Assert.Equal(4, instruction.Plan.Length);
        Assert.Equal("in_progress", instruction.Plan[0].Status);
        Assert.Equal("completed", instruction.Plan[1].Status);
        Assert.Equal("skipped", instruction.Plan[2].Status);
        Assert.Equal("pending", instruction.Plan[3].Status);
    }

    [Theory]
    [InlineData("{\"action\":\"plan\"}")]
    [InlineData("{\"action\":\"plan\",\"plan\":[]}")]
    [InlineData("{\"action\":\"plan\",\"plan\":[{\"title\":\"  \"}]}")]
    [InlineData("{\"action\":\"plan\",\"plan\":[{\"title\":\"ok\"}],\"extra\":true}")]
    public void Parse_RejectsMalformedPlan(string value)
    {
        Assert.ThrowsAny<Exception>(() => AgentOrchestrator.Parse(value));
    }

    [Fact]
    public void Parse_RejectsOversizedPlan()
    {
        string items = string.Join(',', Enumerable.Range(0, 65).Select(i => $"{{\"title\":\"step {i}\"}}"));
        Assert.ThrowsAny<Exception>(() => AgentOrchestrator.Parse($"{{\"action\":\"plan\",\"plan\":[{items}]}}"));
    }

    [Fact]
    public async Task RunAsync_EmitsPlanActivityThenFinishes()
    {
        var responses = new Queue<string>(new[]
        {
            "{\"action\":\"plan\",\"plan\":[{\"title\":\"first\",\"status\":\"in_progress\"},{\"title\":\"second\",\"status\":\"pending\"}]}",
            "{\"action\":\"finish\",\"summary\":\"all good\"}",
        });
        var orchestrator = new AgentOrchestrator(
            (messages, cancellationToken) => Task.FromResult(new LlmResponse { Provider = "test", Model = "test", Content = responses.Dequeue() }));

        var activities = new List<AgentActivity>();
        orchestrator.Activity += activities.Add;

        AgentResult result = await orchestrator.RunAsync("plan then finish");

        Assert.Equal("all good", result.Summary);
        AgentActivity? plan = activities.FirstOrDefault(activity => activity.Action == "plan");
        Assert.NotNull(plan);
        Assert.NotNull(plan.Plan);
        Assert.Equal(2, plan.Plan.Count);
        Assert.Equal("first", plan.Plan[0].Title);
        Assert.Equal("in_progress", plan.Plan[0].Status);
        Assert.Equal("pending", plan.Plan[1].Status);
        Assert.Contains(activities, activity => activity.Action == "finish");
    }

    [Fact]
    public async Task RunAsync_EmitsPlanRunningThenSettlesItCompletedAtFinish()
    {
        var responses = new Queue<string>(new[]
        {
            "{\"action\":\"plan\",\"plan\":[{\"title\":\"first\",\"status\":\"in_progress\"},{\"title\":\"second\",\"status\":\"pending\"}]}",
            "{\"action\":\"plan\",\"plan\":[{\"title\":\"first\",\"status\":\"completed\"},{\"title\":\"second\",\"status\":\"in_progress\"}]}",
            "{\"action\":\"finish\",\"summary\":\"all good\"}",
        });
        var orchestrator = new AgentOrchestrator(
            (messages, cancellationToken) => Task.FromResult(new LlmResponse { Provider = "test", Model = "test", Content = responses.Dequeue() }));

        var activities = new List<AgentActivity>();
        orchestrator.Activity += activities.Add;

        AgentResult result = await orchestrator.RunAsync("plan then finish");

        List<AgentActivity> planActivities = activities.Where(activity => activity.Action == "plan").ToList();
        // Two working snapshots stay live (running) while the agent works through them.
        Assert.Equal(3, planActivities.Count);
        Assert.Equal("running", planActivities[0].State);
        Assert.Equal("running", planActivities[1].State);
        // The finish re-emits the last-known plan as a completed snapshot so the
        // UI plan row settles with the final per-item statuses and spinner stops.
        AgentActivity settled = planActivities[^1];
        Assert.Equal("completed", settled.State);
        Assert.NotNull(settled.Plan);
        Assert.Equal(2, settled.Plan.Count);
        Assert.Equal("completed", settled.Plan[0].Status);
        Assert.Equal("in_progress", settled.Plan[1].Status);
        Assert.Equal("all good", result.Summary);
    }

    [Fact]
    public async Task RunAsync_PreservesPriorPromptAndToolResults()
    {
        var responses = new Queue<string>(new[]
        {
            "{\"action\":\"plan\",\"plan\":[{\"title\":\"inspect\",\"status\":\"completed\"}]}",
            "{\"action\":\"finish\",\"summary\":\"first complete\"}",
            "{\"action\":\"finish\",\"summary\":\"second complete\"}",
        });
        var snapshots = new List<string[]>();
        var orchestrator = new AgentOrchestrator((messages, cancellationToken) =>
        {
            snapshots.Add(messages.Select(message => message.Content).ToArray());
            return Task.FromResult(new LlmResponse { Provider = "test", Model = "test", Content = responses.Dequeue() });
        });

        await orchestrator.RunAsync("first prompt");
        await orchestrator.RunAsync("second prompt");

        string[] secondTurn = snapshots[^1];
        Assert.Contains(secondTurn, message => message.Contains("first prompt", StringComparison.Ordinal));
        Assert.Contains(secondTurn, message => message.Contains("Tool result:\nPlan recorded.", StringComparison.Ordinal));
        Assert.Contains(secondTurn, message => message.Contains("second prompt", StringComparison.Ordinal));
    }
}
