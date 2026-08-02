namespace Core.Goal.Tests;

public sealed class AgentRegistryTests
{
    private static AgentRegistry CreateRegistry() => new();

    private static AgentDescriptor MakeMainAgent(string? id = null, string? goalId = null)
        => new()
        {
            Id = id ?? AgentDescriptor.GenerateId(),
            Name = "mainAgent",
            IsSubAgent = false,
            GoalId = goalId ?? "goal-1",
        };

    private static AgentDescriptor MakeSubAgent(string parentId, string? id = null, string? goalId = null)
        => new()
        {
            Id = id ?? AgentDescriptor.GenerateId(),
            Name = "subAgent",
            IsSubAgent = true,
            ParentAgentId = parentId,
            GoalId = goalId ?? "goal-1",
        };

    [Fact]
    public void Register_MainAgent_Should_AppearInGetMainAgents()
    {
        var registry = CreateRegistry();
        var main = MakeMainAgent();

        registry.Register(main);

        var mains = registry.GetMainAgents();
        Assert.Single(mains);
        Assert.Equal(main.Id, mains[0].Id);
        Assert.False(mains[0].IsSubAgent);
    }

    [Fact]
    public void Register_SubAgent_Should_AppearInSubAgentMap()
    {
        var registry = CreateRegistry();
        var main = MakeMainAgent();
        registry.Register(main);

        var sub = MakeSubAgent(main.Id);
        registry.Register(sub);

        var subs = registry.GetSubAgents(main.Id);
        Assert.Single(subs);
        Assert.Equal(sub.Id, subs[0].Id);
        Assert.True(subs[0].IsSubAgent);
    }

    [Fact]
    public void SubAgentMap_Should_ReturnSubAgentsByMainAgentId()
    {
        var registry = CreateRegistry();
        var main1 = MakeMainAgent(goalId: "g1");
        var main2 = MakeMainAgent(goalId: "g2");
        registry.Register(main1);
        registry.Register(main2);

        var sub1a = MakeSubAgent(main1.Id, goalId: "g1");
        var sub1b = MakeSubAgent(main1.Id, goalId: "g1");
        var sub2a = MakeSubAgent(main2.Id, goalId: "g2");
        registry.Register(sub1a);
        registry.Register(sub1b);
        registry.Register(sub2a);

        var map = registry.SubAgentMap;
        Assert.Equal(2, map[main1.Id].Count);
        Assert.Single(map[main2.Id]);
    }

    [Fact]
    public void Unregister_MainAgent_Should_RemoveOrphanSubAgents()
    {
        var registry = CreateRegistry();
        var main = MakeMainAgent();
        registry.Register(main);
        var sub1 = MakeSubAgent(main.Id);
        var sub2 = MakeSubAgent(main.Id);
        registry.Register(sub1);
        registry.Register(sub2);

        registry.Unregister(main.Id);

        Assert.Null(registry.Get(main.Id));
        Assert.Null(registry.Get(sub1.Id));
        Assert.Null(registry.Get(sub2.Id));
        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public void Unregister_SubAgent_Should_RemoveFromSubAgentMap()
    {
        var registry = CreateRegistry();
        var main = MakeMainAgent();
        registry.Register(main);
        var sub1 = MakeSubAgent(main.Id);
        var sub2 = MakeSubAgent(main.Id);
        registry.Register(sub1);
        registry.Register(sub2);

        registry.Unregister(sub1.Id);

        Assert.Null(registry.Get(sub1.Id));
        var subs = registry.GetSubAgents(main.Id);
        Assert.Single(subs);
        Assert.Equal(sub2.Id, subs[0].Id);
    }

    [Fact]
    public void GetByGoalId_Should_ReturnAllAgentsForGoal()
    {
        var registry = CreateRegistry();
        var main1 = MakeMainAgent(goalId: "g1");
        var main2 = MakeMainAgent(goalId: "g2");
        registry.Register(main1);
        registry.Register(main2);
        registry.Register(MakeSubAgent(main1.Id, goalId: "g1"));

        var g1Agents = registry.GetByGoalId("g1");
        Assert.Equal(2, g1Agents.Count);

        var g2Agents = registry.GetByGoalId("g2");
        Assert.Single(g2Agents);
    }

    [Fact]
    public void GetByStatus_Should_FilterByStatus()
    {
        var registry = CreateRegistry();
        var main = MakeMainAgent();
        main.Status = AgentStatus.Running;
        registry.Register(main);

        var sub = MakeSubAgent(main.Id);
        sub.Status = AgentStatus.Pending;
        registry.Register(sub);

        var running = registry.GetByStatus(AgentStatus.Running);
        Assert.Single(running);
        Assert.Equal(main.Id, running[0].Id);

        var pending = registry.GetByStatus(AgentStatus.Pending);
        Assert.Single(pending);
        Assert.Equal(sub.Id, pending[0].Id);
    }

    [Fact]
    public void Clear_Should_RemoveAll()
    {
        var registry = CreateRegistry();
        var main = MakeMainAgent();
        registry.Register(main);
        registry.Register(MakeSubAgent(main.Id));

        registry.Clear();

        Assert.Equal(0, registry.Count);
        Assert.Empty(registry.GetMainAgents());
    }

    [Fact]
    public void Register_DuplicateId_Should_Ignore()
    {
        var registry = CreateRegistry();
        var main = MakeMainAgent(id: "fixed-id");
        registry.Register(main);

        var dup = MakeMainAgent(id: "fixed-id");
        registry.Register(dup);

        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public void AgentDescriptor_GenerateId_Should_BeUnique()
    {
        var ids = new HashSet<string>();
        for (var i = 0; i < 100; i++)
        {
            ids.Add(AgentDescriptor.GenerateId());
        }
        Assert.Equal(100, ids.Count);
    }

    [Fact]
    public void AgentDescriptor_GenerateId_Should_StartWithAgentPrefix()
    {
        var id = AgentDescriptor.GenerateId();
        Assert.StartsWith("agent-", id);
        Assert.True(id.Length <= 20);
    }

    [Fact]
    public void GetSubAgents_NonExistentMain_Should_ReturnEmpty()
    {
        var registry = CreateRegistry();
        var subs = registry.GetSubAgents("nonexistent");
        Assert.Empty(subs);
    }
}
