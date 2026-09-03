namespace Tui.Tests.Rendering;

/// <summary>
/// SubAgentCardManager 单元测试 — 验证展开/折叠/最多3个同时展开。
/// </summary>
public class SubAgentCardManagerTests
{
    [Fact]
    public void Expand_SingleAgent_IsExpanded()
    {
        var manager = new SubAgentCardManager();
        var evicted = manager.Expand("agent1");
        Assert.Null(evicted);
        Assert.True(manager.IsExpanded("agent1"));
        Assert.Equal(1, manager.ExpandedCount);
    }

    [Fact]
    public void Expand_AlreadyExpanded_ReturnsNull()
    {
        var manager = new SubAgentCardManager();
        manager.Expand("agent1");
        var evicted = manager.Expand("agent1");
        Assert.Null(evicted);
        Assert.Equal(1, manager.ExpandedCount);
    }

    [Fact]
    public void Expand_ThreeAgents_AllExpanded()
    {
        var manager = new SubAgentCardManager();
        manager.Expand("agent1");
        manager.Expand("agent2");
        manager.Expand("agent3");
        Assert.Equal(3, manager.ExpandedCount);
        Assert.True(manager.IsExpanded("agent1"));
        Assert.True(manager.IsExpanded("agent2"));
        Assert.True(manager.IsExpanded("agent3"));
    }

    [Fact]
    public void Expand_FourthAgent_EvictsOldest()
    {
        var manager = new SubAgentCardManager();
        manager.Expand("agent1");
        manager.Expand("agent2");
        manager.Expand("agent3");

        var evicted = manager.Expand("agent4");
        Assert.Equal("agent1", evicted);
        Assert.False(manager.IsExpanded("agent1"));
        Assert.True(manager.IsExpanded("agent4"));
        Assert.Equal(3, manager.ExpandedCount);
    }

    [Fact]
    public void Collapse_RemovesFromExpanded()
    {
        var manager = new SubAgentCardManager();
        manager.Expand("agent1");
        manager.Expand("agent2");

        var result = manager.Collapse("agent1");
        Assert.True(result);
        Assert.False(manager.IsExpanded("agent1"));
        Assert.True(manager.IsExpanded("agent2"));
        Assert.Equal(1, manager.ExpandedCount);
    }

    [Fact]
    public void Collapse_NotExpanded_ReturnsFalse()
    {
        var manager = new SubAgentCardManager();
        var result = manager.Collapse("agent1");
        Assert.False(result);
    }

    [Fact]
    public void Toggle_Expanded_Collapses()
    {
        var manager = new SubAgentCardManager();
        manager.Expand("agent1");

        var evicted = manager.Toggle("agent1");
        Assert.Null(evicted);
        Assert.False(manager.IsExpanded("agent1"));
    }

    [Fact]
    public void Toggle_Collapsed_Expands()
    {
        var manager = new SubAgentCardManager();
        var evicted = manager.Toggle("agent1");
        Assert.Null(evicted);
        Assert.True(manager.IsExpanded("agent1"));
    }

    [Fact]
    public void Toggle_ExpandOverLimit_EvictsOldest()
    {
        var manager = new SubAgentCardManager();
        manager.Expand("agent1");
        manager.Expand("agent2");
        manager.Expand("agent3");

        var evicted = manager.Toggle("agent4");
        Assert.Equal("agent1", evicted);
        Assert.True(manager.IsExpanded("agent4"));
        Assert.False(manager.IsExpanded("agent1"));
    }

    [Fact]
    public void CollapseAll_RemovesAll()
    {
        var manager = new SubAgentCardManager();
        manager.Expand("agent1");
        manager.Expand("agent2");
        manager.Expand("agent3");

        manager.CollapseAll();
        Assert.Equal(0, manager.ExpandedCount);
        Assert.False(manager.IsExpanded("agent1"));
    }

    [Fact]
    public void Expanded_ReturnsOrderedByTime()
    {
        var manager = new SubAgentCardManager();
        manager.Expand("agent1");
        manager.Expand("agent2");
        manager.Expand("agent3");

        var expanded = manager.Expanded;
        Assert.Equal(["agent1", "agent2", "agent3"], expanded);
    }

    [Fact]
    public void Expand_AfterCollapse_CanReexpand()
    {
        var manager = new SubAgentCardManager();
        manager.Expand("agent1");
        manager.Collapse("agent1");

        var evicted = manager.Expand("agent1");
        Assert.Null(evicted);
        Assert.True(manager.IsExpanded("agent1"));
    }
}
