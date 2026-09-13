namespace JoinCode.Abs.Tests.LLM.Chat;

/// <summary>
/// SubAgentEventChannel 测试 — AsyncLocal 环境通道的作用域/读写/完成语义。
/// 模式对齐 SubAgentContext.Current（AsyncLocal 环境态），供 QueryLoop 与 Agent 中间件跨层桥接。
/// </summary>
public class SubAgentEventChannelTests
{
    [Fact]
    public void EnterScope_SetsCurrent_AndRestoreRestoresPrevious()
    {
        SubAgentEventChannel.Current.Should().BeNull();

        var outer = new SubAgentEventChannel();
        var inner = new SubAgentEventChannel();

        using (outer.EnterScope())
        {
            SubAgentEventChannel.Current.Should().BeSameAs(outer);

            using (inner.EnterScope())
            {
                SubAgentEventChannel.Current.Should().BeSameAs(inner);
            }

            SubAgentEventChannel.Current.Should().BeSameAs(outer);
        }

        SubAgentEventChannel.Current.Should().BeNull();
    }

    [Fact]
    public async Task TryEmit_TryRead_ShouldPreserveOrder()
    {
        using var scope = new SubAgentEventChannel().EnterScope();
        var channel = SubAgentEventChannel.Current!;

        channel.Emit(ChatStreamEvent.AgentStarted("a1", "explore", "d", "r"));
        channel.Emit(new ChatStreamEvent { Type = ChatStreamEventType.ToolCallStart, ToolName = "FileRead", AgentId = "a1" });
        channel.Emit(ChatStreamEvent.AgentFinished("a1", success: true));

        // AsyncLocal 跨 await 流动 — 模拟真实管道中的异步排空
        await Task.Yield();

        channel.TryRead(out var e0).Should().BeTrue();
        e0.Type.Should().Be(ChatStreamEventType.AgentStarted);
        channel.TryRead(out var e1).Should().BeTrue();
        e1.Type.Should().Be(ChatStreamEventType.ToolCallStart);
        channel.TryRead(out var e2).Should().BeTrue();
        e2.Type.Should().Be(ChatStreamEventType.AgentFinished);
        channel.TryRead(out _).Should().BeFalse();
    }

    [Fact]
    public void Emit_WithoutScope_ShouldNotThrow()
    {
        // 无作用域时静默丢弃 — CLI/测试环境无 GUI 显示也不得崩溃
        var act = () => new SubAgentEventChannel().Emit(ChatStreamEvent.AgentStarted("a2"));
        act.Should().NotThrow();
    }

    [Fact]
    public void TryDrain_ShouldReturnAllAndClear()
    {
        using var scope = new SubAgentEventChannel().EnterScope();
        var channel = SubAgentEventChannel.Current!;
        channel.Emit(ChatStreamEvent.AgentStarted("a3"));
        channel.Emit(ChatStreamEvent.AgentFinished("a3", success: false));

        var drained = channel.TryDrain();

        drained.Should().HaveCount(2);
        drained[0].Type.Should().Be(ChatStreamEventType.AgentStarted);
        drained[1].AgentSuccess.Should().BeFalse();
        channel.TryDrain().Should().BeEmpty();
    }
}
