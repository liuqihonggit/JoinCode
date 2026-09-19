namespace Sync.Tests.Agents.Coordinator.Liveness;

/// <summary>
/// SubAgentActivator 单元测试 — 验证激活动作注入催促提示+恢复执行（ADR 0106 L3）
/// </summary>
public sealed class SubAgentActivatorTests {
    private static AgentBase CreateAgent(string task = "test task") {
        var queryEngineMock = new Mock<IQueryEngine>();
        return new AgentBase(task, null, queryEngineMock.Object, null);
    }

    [Fact]
    public async Task ActivateAsync_AgentNotFound_ReturnsAgentNotFound() {
        var lifecycleMock = new Mock<IAgentLifecycleManager>();
        lifecycleMock
            .Setup(x => x.GetAgentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IAgent?)null);
        var activator = new SubAgentActivator(lifecycleMock.Object);

        var result = await activator.ActivateAsync("nonexistent-id");

        result.Success.Should().BeFalse();
        result.Reason.Should().Contain("不存在");
    }

    [Fact]
    public async Task ActivateAsync_AgentRunning_InjectsPromptAndSucceeds() {
        var agent = CreateAgent("test task");
        agent.Status = TaskExecutionStatus.Running;
        var lifecycleMock = new Mock<IAgentLifecycleManager>();
        lifecycleMock
            .Setup(x => x.GetAgentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(agent);
        var activator = new SubAgentActivator(lifecycleMock.Object);

        var result = await activator.ActivateAsync(agent.ObjectId.UniqueId, idleSeconds: 45);

        result.Success.Should().BeTrue();
        agent.ChatHistory.Should().Contain(m => m.Content!.Contains("45s") && m.Role == MessageRole.System);
        lifecycleMock.Verify(x => x.ResumeAgentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ActivateAsync_AgentPaused_InjectsPromptAndResumes() {
        var agent = CreateAgent("test task");
        agent.Status = TaskExecutionStatus.Paused;
        var lifecycleMock = new Mock<IAgentLifecycleManager>();
        lifecycleMock
            .Setup(x => x.GetAgentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(agent);
        var activator = new SubAgentActivator(lifecycleMock.Object);

        var result = await activator.ActivateAsync(agent.ObjectId.UniqueId);

        result.Success.Should().BeTrue();
        agent.ChatHistory.Should().Contain(m => m.Role == MessageRole.System);
        lifecycleMock.Verify(x => x.ResumeAgentAsync(agent.ObjectId.UniqueId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ActivateAsync_TouchesEntity_LastActivityAtUpdated() {
        var agent = CreateAgent("test task");
        agent.Status = TaskExecutionStatus.Running;
        var before = agent.LastActivityAt;
        var lifecycleMock = new Mock<IAgentLifecycleManager>();
        lifecycleMock
            .Setup(x => x.GetAgentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(agent);
        var activator = new SubAgentActivator(lifecycleMock.Object);

        await Task.Delay(10);
        await activator.ActivateAsync(agent.ObjectId.UniqueId);

        agent.LastActivityAt.Should().BeAfter(before);
    }

    [Fact]
    public async Task ActivateAsync_DefaultIdleSeconds_Uses30() {
        var agent = CreateAgent("test task");
        agent.Status = TaskExecutionStatus.Running;
        var lifecycleMock = new Mock<IAgentLifecycleManager>();
        lifecycleMock
            .Setup(x => x.GetAgentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(agent);
        var activator = new SubAgentActivator(lifecycleMock.Object);

        await activator.ActivateAsync(agent.ObjectId.UniqueId);

        agent.ChatHistory.Should().Contain(m => m.Content!.Contains("30s"));
    }
}