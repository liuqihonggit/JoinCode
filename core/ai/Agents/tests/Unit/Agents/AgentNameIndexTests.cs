namespace Core.Agents.Tests.Unit.Agents;

/// <summary>
/// AgentNameIndex 单元测试 — 多键映射 name→agentId，O(1) 查找
/// </summary>
public class AgentNameIndexTests
{
    [Fact]
    public void Find_ByAgentId_ReturnsAgentId()
    {
        var index = new Coordinator.Core.Messaging.AgentNameIndex();
        index.Register("agent-001", "explorer", "探索代码库", "Explorer");

        Assert.Equal("agent-001", index.Find("agent-001"));
    }

    [Fact]
    public void Find_ByName_ReturnsAgentId()
    {
        var index = new Coordinator.Core.Messaging.AgentNameIndex();
        index.Register("agent-001", "explorer", "探索代码库", "Explorer");

        Assert.Equal("agent-001", index.Find("explorer"));
    }

    [Fact]
    public void Find_ByTask_ReturnsAgentId()
    {
        var index = new Coordinator.Core.Messaging.AgentNameIndex();
        index.Register("agent-001", "explorer", "探索代码库", "Explorer");

        Assert.Equal("agent-001", index.Find("探索代码库"));
    }

    [Fact]
    public void Find_ByDisplayName_ReturnsAgentId()
    {
        var index = new Coordinator.Core.Messaging.AgentNameIndex();
        index.Register("agent-001", "explorer", "探索代码库", "Explorer");

        Assert.Equal("agent-001", index.Find("Explorer"));
    }

    [Fact]
    public void Find_CaseInsensitive_ReturnsAgentId()
    {
        var index = new Coordinator.Core.Messaging.AgentNameIndex();
        index.Register("agent-001", "explorer", "探索代码库", "Explorer");

        Assert.Equal("agent-001", index.Find("EXPLORER"));
        Assert.Equal("agent-001", index.Find("Agent-001"));
    }

    [Fact]
    public void Find_NotRegistered_ReturnsNull()
    {
        var index = new Coordinator.Core.Messaging.AgentNameIndex();
        index.Register("agent-001", "explorer", "探索代码库", "Explorer");

        Assert.Null(index.Find("nonexistent"));
    }

    [Fact]
    public void Find_EmptyIndex_ReturnsNull()
    {
        var index = new Coordinator.Core.Messaging.AgentNameIndex();
        Assert.Null(index.Find("anything"));
    }

    [Fact]
    public void Unregister_RemovesAllKeysForAgent()
    {
        var index = new Coordinator.Core.Messaging.AgentNameIndex();
        index.Register("agent-001", "explorer", "探索代码库", "Explorer");

        index.Unregister("agent-001", "explorer", "探索代码库", "Explorer");

        Assert.Null(index.Find("agent-001"));
        Assert.Null(index.Find("explorer"));
        Assert.Null(index.Find("探索代码库"));
        Assert.Null(index.Find("Explorer"));
    }

    [Fact]
    public void Unregister_SameNameDifferentAgents_OnlyRemovesTargetAgent()
    {
        var index = new Coordinator.Core.Messaging.AgentNameIndex();
        index.Register("agent-001", "worker", "任务A", "Worker");
        index.Register("agent-002", "worker", "任务B", "Worker");

        index.Unregister("agent-001", "worker", "任务A", "Worker");

        Assert.Null(index.Find("任务A"));
        Assert.Equal("agent-002", index.Find("worker"));
        Assert.Equal("agent-002", index.Find("Worker"));
        Assert.Equal("agent-002", index.Find("任务B"));
    }

    [Fact]
    public void Register_MultipleAgents_AllFindable()
    {
        var index = new Coordinator.Core.Messaging.AgentNameIndex();
        index.Register("agent-001", "explorer", "探索代码库", "Explorer");
        index.Register("agent-002", "planner", "制定计划", "Planner");
        index.Register("agent-003", "coder", "编写代码", null);

        Assert.Equal("agent-001", index.Find("explorer"));
        Assert.Equal("agent-002", index.Find("planner"));
        Assert.Equal("agent-003", index.Find("coder"));
        Assert.Equal("agent-003", index.Find("编写代码"));
    }

    [Fact]
    public void Register_NullDisplayName_SkipsDisplayNameKey()
    {
        var index = new Coordinator.Core.Messaging.AgentNameIndex();
        index.Register("agent-001", "explorer", "探索代码库", null);

        Assert.Equal("agent-001", index.Find("explorer"));
        Assert.Equal("agent-001", index.Find("探索代码库"));
    }

    [Fact]
    public void Register_EmptyName_SkipsNameKey()
    {
        var index = new Coordinator.Core.Messaging.AgentNameIndex();
        index.Register("agent-001", "", "探索代码库", "Explorer");

        Assert.Equal("agent-001", index.Find("agent-001"));
        Assert.Equal("agent-001", index.Find("探索代码库"));
        Assert.Equal("agent-001", index.Find("Explorer"));
    }
}
