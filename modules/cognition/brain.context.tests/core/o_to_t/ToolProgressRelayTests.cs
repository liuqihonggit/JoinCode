namespace Core.Context;

/// <summary>
/// 工具进度中继测试（TASK065 P3 — Bash 工具流式进度引擎侧接线）—
/// ToolProgressRelay.Build：通道在场闭包 Emit ChatStreamEvent.ToolProgress（toolName/toolCallId
/// 构造期捕获 — ToolProgressData 不携带工具名）；无通道返回 null（CLI 纯文本等场景 gateway
/// 走无进度路径行为零变化）；闭包直持通道引用 — ThreadPool 回调（ShellExecutionMiddleware 进度
/// Timer）发射不依赖 AsyncLocal 回读，跨线程可靠。
/// </summary>
public sealed class ToolProgressRelayTests
{
    /// <summary>无通道 — Build 返回 null（调用方直传 gateway 第四参，gateway 判 null 跳过进度路径）。</summary>
    [Fact]
    public void Build_WithoutChannel_ReturnsNull()
    {
        Assert.Null(ToolProgressRelay.Build("Bash"));
    }

    /// <summary>通道在场 — 回调 Emit ToolProgress 事件（toolName/toolCallId 构造期捕获 +
    /// progressType/message 透传 — 字段全量校验）。</summary>
    [Fact]
    public void Build_WithChannel_EmitsToolProgressEvent()
    {
        var channel = new SubAgentEventChannel();
        using (channel.EnterScope())
        {
            var relay = ToolProgressRelay.Build("Bash", "call-1");
            if (relay is not { } emit)
            {
                throw new Xunit.Sdk.XunitException("通道在场时 Build 应返回非空回调");
            }

            emit(new ToolProgressData {
                ProgressType = "bash_progress",
                ToolUseId = "bash_progress-0",
                Message = "line1\nline2",
            });
        }

        var evt = Assert.Single(channel.TryDrain());
        Assert.Equal(ChatStreamEventType.ToolProgress, evt.Type);
        Assert.Equal("Bash", evt.ToolName);
        Assert.Equal("call-1", evt.ToolCallId);
        Assert.Equal("bash_progress", evt.ProgressType);
        Assert.Equal("line1\nline2", evt.ProgressMessage);
    }

    /// <summary>null message — 空串投影（消费侧事件形状恒定）。</summary>
    [Fact]
    public void Build_NullMessage_ProjectsEmptyString()
    {
        var channel = new SubAgentEventChannel();
        using (channel.EnterScope())
        {
            var relay = ToolProgressRelay.Build("Bash");
            if (relay is not { } emit) {
                throw new Xunit.Sdk.XunitException("通道在场时 Build 应返回非空回调");
            }

            emit(new ToolProgressData { ProgressType = "bash_progress", ToolUseId = "u" });
        }

        var evt = Assert.Single(channel.TryDrain());
        Assert.Equal(string.Empty, evt.ProgressMessage);
    }

    /// <summary>ThreadPool 调用（ShellExecutionMiddleware 进度 Timer 语义）— 闭包直持通道引用，
    /// 跨线程发射可靠（不依赖 AsyncLocal 回读 — ExecutionContext 捕获链无关）。</summary>
    [Fact]
    public async Task Relay_FromThreadPoolStillEmits()
    {
        var channel = new SubAgentEventChannel();
        using (channel.EnterScope())
        {
            var relay = ToolProgressRelay.Build("Bash", "call-1");

            if (relay is not { } emit) {
                throw new Xunit.Sdk.XunitException("通道在场时 Build 应返回非空回调");
            }

            await Task.Run(() => emit(new ToolProgressData {
                ProgressType = "bash_progress",
                ToolUseId = "u",
                Message = "pooled output",
            }));
        }

        var evt = Assert.Single(channel.TryDrain());
        Assert.Equal(ChatStreamEventType.ToolProgress, evt.Type);
        Assert.Equal("pooled output", evt.ProgressMessage);
    }

    /// <summary>通道完成后 Emit 静默丢弃 — 迟到进度不泄漏（通道既有语义，relay 调用不抛）。</summary>
    [Fact]
    public void Relay_AfterChannelComplete_SilentlyDropped()
    {
        var channel = new SubAgentEventChannel();
        ToolProgressCallback? relay;
        using (channel.EnterScope())
        {
            relay = ToolProgressRelay.Build("Bash");
        }

        channel.Complete();

        if (relay is { } emit) {
            emit(new ToolProgressData { ProgressType = "bash_progress", ToolUseId = "u", Message = "late" });
        }
        Assert.Empty(channel.TryDrain());
    }
}
