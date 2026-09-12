namespace Hands.Tests.ToolHandlers;

/// <summary>
/// AgentForkMiddleware 子代理事件测试 —
/// subagent_type 为空时走 fork 后台短路路径，此前 GUI 完全看不到该类子代理。
/// 现契约：启动时立即向 SubAgentEventChannel 发射 AgentStarted（携带 forkId/任务描述），
/// 并把通道引用传入 ForkOptions 供后台完成时发射 AgentFinished。
/// </summary>
public class AgentForkMiddlewareTests
{
    [Fact]
    public async Task ForkPath_ShouldEmitAgentStarted_AndPassChannelToOptions()
    {
        JoinCode.Abstractions.Interfaces.ForkOptions? capturedOptions = null;
        var forkManager = new Mock<IForkSubAgentManager>();
        forkManager.Setup(f => f.ForkAsync(It.IsAny<ForkOptions>(), It.IsAny<CancellationToken>()))
            .Callback<ForkOptions, CancellationToken>((o, _) => capturedOptions = o)
            .ReturnsAsync(new ForkResult { ForkId = "fork-1", State = ForkState.Running });

        var sut = new AgentForkMiddleware(
            new SubAgentContextAccessor(),
            forkManager.Object);

        var context = new AgentToolContext
        {
            Description = "继承上下文",
            Prompt = "fork 任务提示词"
        };
        var channel = new JoinCode.Abstractions.LLM.Chat.SubAgentEventChannel();

        IReadOnlyList<ChatStreamEvent> drained;
        using (channel.EnterScope())
        {
            await sut.InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None);
            drained = channel.TryDrain();
        }

        // 启动事件即刻发射 — GUI 运行面板立刻出现该 fork 行
        var started = drained.FirstOrDefault(e => e.Type == ChatStreamEventType.AgentStarted);
        started.Should().NotBeNull("fork 启动必须发射 AgentStarted");
        started!.AgentId.Should().Be("fork-1");
        started.AgentDescription.Should().Be("fork 任务提示词");

        // 通道引用传入 options — 供后台完成发射终态
        capturedOptions.Should().NotBeNull();
        capturedOptions!.EventChannel.Should().BeSameAs(channel);
        capturedOptions.RunInBackground.Should().BeTrue();

        // 短路语义保持：Result 已设置且不调用 next（此处 next 若被调用会抛出）
        context.Result.Should().NotBeNull();
    }

    [Fact]
    public async Task NonForkPath_WhenSubagentTypeProvided_ShouldNotEmit()
    {
        var forkManager = new Mock<IForkSubAgentManager>();
        var sut = new AgentForkMiddleware(
            new SubAgentContextAccessor(),
            forkManager.Object);

        var context = new AgentToolContext
        {
            Description = "d",
            Prompt = "p",
            SubagentType = "executor:search"
        };
        var channel = new JoinCode.Abstractions.LLM.Chat.SubAgentEventChannel();
        var nextCalled = false;

        using (channel.EnterScope())
        {
            await sut.InvokeAsync(context, (_, _) => { nextCalled = true; return Task.CompletedTask; }, CancellationToken.None);
        }

        nextCalled.Should().BeTrue("显式类型应走后续流式中间件");
        channel.TryDrain().Should().BeEmpty();
    }
}
