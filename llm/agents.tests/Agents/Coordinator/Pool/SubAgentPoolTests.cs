namespace Sync.Tests.Agents.Coordinator.Pool;

/// <summary>
/// SubAgentPool 单元测试 — 验证代理池回池/抢塞/清理逻辑（ADR 0106 L3）
/// </summary>
public sealed class SubAgentPoolTests {
    private static AgentBase CreateAgent(string task = "test task") {
        var queryEngineMock = new Mock<IQueryEngine>();
        return new AgentBase(task, null, queryEngineMock.Object, null);
    }

    private static SubAgentLivenessOptions DefaultOptions() => new() {
        PoolMaxSize = 4,
        PoolIdleTimeoutSeconds = 60,
    };

    [Fact]
    public async Task Return_PoolEnabled_AddsToPool() {
        await using var pool = new SubAgentPool(DefaultOptions());
        var agent = CreateAgent("task A");

        var result = await pool.Return(agent);

        result.Should().BeTrue();
        pool.Count.Should().Be(1);
    }

    [Fact]
    public async Task Return_PoolFull_DisposesAgent() {
        var options = DefaultOptions();
        options.PoolMaxSize = 1;
        await using var pool = new SubAgentPool(options);
        var agent1 = CreateAgent("task A");
        var agent2 = CreateAgent("task B");

        await pool.Return(agent1);
        var result = await pool.Return(agent2);

        result.Should().BeFalse();
        pool.Count.Should().Be(1);
    }

    [Fact]
    public async Task Return_PoolDisabled_DisposesAgent() {
        var options = DefaultOptions();
        options.PoolMaxSize = 0;
        await using var pool = new SubAgentPool(options);
        var agent = CreateAgent();

        var result = await pool.Return(agent);

        result.Should().BeFalse();
        pool.Count.Should().Be(0);
        pool.IsFull.Should().BeTrue();
    }

    [Fact]
    public async Task TryAcquire_EmptyPool_ReturnsNull() {
        await using var pool = new SubAgentPool(DefaultOptions());

        var agent = pool.TryAcquire("any task");

        agent.Should().BeNull();
    }

    [Fact]
    public async Task TryAcquire_MatchingTask_ReturnsAgent() {
        await using var pool = new SubAgentPool(DefaultOptions());
        var agent = CreateAgent("fix bug in parser");
        agent.Status = TaskExecutionStatus.Completed;
        await pool.Return(agent);

        var acquired = pool.TryAcquire("fix bug in parser");

        acquired.Should().NotBeNull();
        acquired!.ObjectId.UniqueId.Should().Be(agent.ObjectId.UniqueId);
        pool.Count.Should().Be(0);
    }

    [Fact]
    public async Task TryAcquire_NoCompletedAgent_ReturnsNull() {
        await using var pool = new SubAgentPool(DefaultOptions());
        var agent = CreateAgent("task A");
        agent.Status = TaskExecutionStatus.Running;
        await pool.Return(agent);

        var acquired = pool.TryAcquire("task A");

        acquired.Should().BeNull();
    }

    [Fact]
    public async Task TryAcquire_PartialMatch_ReturnsBestMatch() {
        await using var pool = new SubAgentPool(DefaultOptions());
        var agent1 = CreateAgent("fix bug in parser");
        agent1.Status = TaskExecutionStatus.Completed;
        var agent2 = CreateAgent("refactor code module");
        agent2.Status = TaskExecutionStatus.Completed;
        await pool.Return(agent1);
        await pool.Return(agent2);

        var acquired = pool.TryAcquire("fix bug in parser");

        acquired.Should().NotBeNull();
        acquired!.ObjectId.UniqueId.Should().Be(agent1.ObjectId.UniqueId);
    }

    [Fact]
    public async Task Remove_ExistingAgent_DisposesAndRemoves() {
        await using var pool = new SubAgentPool(DefaultOptions());
        var agent = CreateAgent("task A");
        await pool.Return(agent);

        var result = await pool.Remove(agent.ObjectId.UniqueId);

        result.Should().BeTrue();
        pool.Count.Should().Be(0);
    }

    [Fact]
    public async Task Remove_NonExistingAgent_ReturnsFalse() {
        await using var pool = new SubAgentPool(DefaultOptions());

        var result = await pool.Remove("nonexistent-id");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsFull_WhenCountReachesMax_ReturnsTrue() {
        var options = DefaultOptions();
        options.PoolMaxSize = 2;
        await using var pool = new SubAgentPool(options);

        await pool.Return(CreateAgent("task A"));
        pool.IsFull.Should().BeFalse();

        await pool.Return(CreateAgent("task B"));
        pool.IsFull.Should().BeTrue();
    }

    [Fact]
    public async Task DisposeAsync_DisposesAllAgents() {
        var pool = new SubAgentPool(DefaultOptions());
        var agent1 = CreateAgent("task A");
        var agent2 = CreateAgent("task B");
        await pool.Return(agent1);
        await pool.Return(agent2);

        await pool.DisposeAsync();

        pool.Count.Should().Be(0);
    }

    [Fact]
    public async Task Return_SameAgentTwice_SecondFails() {
        await using var pool = new SubAgentPool(DefaultOptions());
        var agent = CreateAgent("task A");

        await pool.Return(agent);
        var result = await pool.Return(agent);

        result.Should().BeFalse();
        pool.Count.Should().Be(1);
    }
}