namespace Host.Tests.Tui.Pipes;

/// <summary>
/// PipeRegistry 单元测试 — 验证注册、注销、获取、主管道查找。
/// </summary>
public class PipeRegistryTests
{
    [Fact]
    public void Register_IncreasesCount()
    {
        var registry = new PipeRegistry();
        registry.Register(new MessagePipe("main", "Main", isMain: true));
        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public void Register_DuplicateId_Overwrites()
    {
        var registry = new PipeRegistry();
        registry.Register(new MessagePipe("agent1", "Agent One"));
        registry.Register(new MessagePipe("agent1", "Agent One Updated"));
        Assert.Equal(1, registry.Count);
        Assert.Equal("Agent One Updated", registry.Get("agent1")!.AgentName);
    }

    [Fact]
    public void Unregister_RemovesPipe()
    {
        var registry = new PipeRegistry();
        registry.Register(new MessagePipe("agent1", "Agent One"));
        Assert.True(registry.Unregister("agent1"));
        Assert.Equal(0, registry.Count);
        Assert.Null(registry.Get("agent1"));
    }

    [Fact]
    public void Unregister_NonExistent_ReturnsFalse()
    {
        var registry = new PipeRegistry();
        Assert.False(registry.Unregister("nonexistent"));
    }

    [Fact]
    public void Get_NonExistent_ReturnsNull()
    {
        var registry = new PipeRegistry();
        Assert.Null(registry.Get("nonexistent"));
    }

    [Fact]
    public void MainPipe_ReturnsMainAgentPipe()
    {
        var registry = new PipeRegistry();
        registry.Register(new MessagePipe("sub1", "Sub One"));
        registry.Register(new MessagePipe("main", "Main", isMain: true));
        registry.Register(new MessagePipe("sub2", "Sub Two"));

        var main = registry.MainPipe;
        Assert.NotNull(main);
        Assert.Equal("main", main!.AgentId);
        Assert.True(main.IsMain);
    }

    [Fact]
    public void MainPipe_NoMainRegistered_ReturnsNull()
    {
        var registry = new PipeRegistry();
        registry.Register(new MessagePipe("sub1", "Sub One"));
        Assert.Null(registry.MainPipe);
    }

    [Fact]
    public void All_ReturnsAllPipes()
    {
        var registry = new PipeRegistry();
        registry.Register(new MessagePipe("a1", "Agent 1"));
        registry.Register(new MessagePipe("a2", "Agent 2"));
        registry.Register(new MessagePipe("a3", "Agent 3"));
        Assert.Equal(3, registry.All.Count);
    }

    [Fact]
    public void Clear_RemovesAllPipes()
    {
        var registry = new PipeRegistry();
        registry.Register(new MessagePipe("a1", "Agent 1"));
        registry.Register(new MessagePipe("a2", "Agent 2"));
        registry.Clear();
        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public void Contains_ReturnsTrueForRegistered()
    {
        var registry = new PipeRegistry();
        registry.Register(new MessagePipe("a1", "Agent 1"));
        Assert.True(registry.Contains("a1"));
        Assert.False(registry.Contains("a2"));
    }
}
