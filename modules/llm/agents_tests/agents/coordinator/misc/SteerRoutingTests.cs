namespace JoinCode.Agents.Tests.Coordinator.Misc;

/// <summary>
/// steer 打断路由测试（TASK063 — @子代理邮箱/转发双链打断触达）—
/// SendMessageToAgentAsync / ForwardUserInputToAgentAsync 发送成功后必须调目标代理 RequestSteer
///（运行中实例；未注册实例 GetAgentAsync null 静默跳过不抛）；消息协议属性 InterruptAndSend 置位。
/// </summary>
public sealed class SteerRoutingTests
{
    private static (AgentServiceImpl Service, Mock<IAgentLifecycleManager> Lifecycle, Mock<IMailbox> Broker, Mock<IAgentInputForwardQueue> Forward) CreateService(Mock<IAgent>? agent = null) {
        var lifecycle = new Mock<IAgentLifecycleManager>();
        lifecycle.Setup(m => m.GetAgentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(agent?.Object);

        var broker = new Mock<IMailbox>();
        broker.Setup(b => b.SendAsync(It.IsAny<string>(), It.IsAny<CoordinatorMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var forward = new Mock<JoinCode.Abstractions.Interfaces.IAgentInputForwardQueue>();
        forward.Setup(f => f.EnqueueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = new AgentServiceImpl(
            lifecycle.Object,
            new Mock<JoinCode.Abstractions.Interfaces.IAgentDefinitionProvider>().Object,
            new Mock<JoinCode.Abstractions.Interfaces.IAgentRoleRegistry>().Object,
            new Infrastructure.Pipeline.MiddlewarePipeline<UnifiedSpawnContext>([]),
            new AgentServiceDependencies { MessageBroker = broker.Object, InputForwardQueue = forward.Object });
        return (service, lifecycle, broker, forward);
    }

    /// <summary>邮箱路径 — 发送成功后 RequestSteer 触达目标代理 + 消息带 InterruptAndSend=true。</summary>
    [Fact]
    public async Task SendMessage_RequestsSteerOnTargetAgent()
    {
        var agent = new Mock<IAgent>();
        var (service, _, broker, _) = CreateService(agent);

        var sent = await service.SendMessageToAgentAsync("agent-1", "打断去改需求");

        sent.Should().BeTrue();
        agent.Verify(a => a.RequestSteer(), Times.Once, "邮箱发送必须同时请求打断当前轮");
        broker.Verify(b => b.SendAsync("agent-1", It.Is<CoordinatorMessage>(m => m.InterruptAndSend), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>转发队列路径 — 转发后 RequestSteer 触达（双链语义一致）。</summary>
    [Fact]
    public async Task ForwardUserInput_RequestsSteerOnTargetAgent()
    {
        var agent = new Mock<IAgent>();
        var (service, _, _, _) = CreateService(agent);

        var sent = await service.ForwardUserInputToAgentAsync("agent-1", "追加输入");

        sent.Should().BeTrue();
        agent.Verify(a => a.RequestSteer(), Times.Once);
    }

    /// <summary>目标未运行 — GetAgentAsync null 静默跳过（消息仍在邮箱等下轮）。</summary>
    [Fact]
    public async Task SendMessage_AgentNotRunning_NoThrowStillDelivers()
    {
        var (service, _, broker, _) = CreateService(agent: null);

        var sent = await service.SendMessageToAgentAsync("agent-x", "消息");

        sent.Should().BeTrue();
        broker.Verify(b => b.SendAsync("agent-x", It.IsAny<CoordinatorMessage>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
