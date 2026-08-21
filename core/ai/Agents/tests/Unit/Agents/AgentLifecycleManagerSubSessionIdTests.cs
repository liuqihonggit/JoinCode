namespace Core.Agents.Tests.Unit.Agents;

using JoinCode.Abstractions.Interfaces;
using JoinCode.Abstractions.LLM.Execution;

/// <summary>
/// AgentLifecycleManager 子会话 ID 派生测试
/// 验证 SpawnSubAgentAsync 传入 parentSessionId 时,生成的 agent.ObjectId.UniqueId 是 {parentSessionId}-sub-{NN} 格式
/// </summary>
public sealed class AgentLifecycleManagerSubSessionIdTests
{
    [Fact]
    public async Task SpawnSubAgentAsync_WithParentSessionId_GeneratesDerivedSubSessionId()
    {
        var queryEngineMock = new Mock<IQueryEngine>();
        queryEngineMock
            .Setup(x => x.QueryAsync(It.IsAny<string>(), It.IsAny<MessageList>(), It.IsAny<QueryOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(Array.Empty<QueryStreamChunk>().ToAsyncEnumerable());

        var stateMachine = new AgentStateMachine();
        var manager = new AgentLifecycleManager(queryEngineMock.Object, stateMachine);

        const string parentSessionId = "20260822-1512-myproject-w2";

        var agent = await manager.SpawnSubAgentAsync("test task", null, default, parentSessionId).ConfigureAwait(true);

        agent.ObjectId.UniqueId.Should().StartWith($"{parentSessionId}-sub-");
        agent.ObjectId.UniqueId.Should().MatchRegex(@"^20260822-1512-myproject-w2-sub-\d{2}$");
    }

    [Fact]
    public async Task SpawnSubAgentAsync_WithoutParentSessionId_GeneratesDefaultGuidStyleId()
    {
        var queryEngineMock = new Mock<IQueryEngine>();
        queryEngineMock
            .Setup(x => x.QueryAsync(It.IsAny<string>(), It.IsAny<MessageList>(), It.IsAny<QueryOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(Array.Empty<QueryStreamChunk>().ToAsyncEnumerable());

        var stateMachine = new AgentStateMachine();
        var manager = new AgentLifecycleManager(queryEngineMock.Object, stateMachine);

        var agent = await manager.SpawnSubAgentAsync("test task").ConfigureAwait(true);

        agent.ObjectId.UniqueId.Should().StartWith("agent-");
        agent.ObjectId.UniqueId.Should().NotContain("-sub-");
    }

    [Fact]
    public async Task SpawnSubAgentAsync_MultipleSpawnWithSameParent_IncrementingCounter()
    {
        var queryEngineMock = new Mock<IQueryEngine>();
        queryEngineMock
            .Setup(x => x.QueryAsync(It.IsAny<string>(), It.IsAny<MessageList>(), It.IsAny<QueryOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(Array.Empty<QueryStreamChunk>().ToAsyncEnumerable());

        var stateMachine = new AgentStateMachine();
        var manager = new AgentLifecycleManager(queryEngineMock.Object, stateMachine);

        const string parentSessionId = "20260822-1512-myproject-w2";

        var agent1 = await manager.SpawnSubAgentAsync("task1", null, default, parentSessionId).ConfigureAwait(true);
        var agent2 = await manager.SpawnSubAgentAsync("task2", null, default, parentSessionId).ConfigureAwait(true);

        agent1.ObjectId.UniqueId.Should().NotBe(agent2.ObjectId.UniqueId, "两次 spawn 应生成不同 ID");
        agent2.ObjectId.UniqueId.Should().StartWith($"{parentSessionId}-sub-");
    }
}
