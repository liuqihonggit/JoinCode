namespace Hands.Tests.ToolHandlers;

/// <summary>
/// AgentStreamExecutionMiddleware 子代理事件转发测试 —
/// 中间件消费 RunAgentStreamAsync 的 chunk 时必须向 SubAgentEventChannel 发射：
/// AgentStarted（首个 chunk 时，携带身份）→ 活动事件（AgentId 标记）→ AgentFinished（统计收尾）。
/// 这是 GUI 多 subAgent 运行期显示的引擎侧数据源契约。
/// </summary>
public class AgentStreamExecutionMiddlewareTests
{
    private static AgentToolContext CreateContext() => new()
    {
        Description = "调研 GUI 方案",
        Prompt = "调研任务提示词",
        SubagentRole = AgentRole.Executor,
        SpawnOptions = new AgentSpawnOptions
        {
            Description = "调研 GUI 方案",
            Prompt = "调研任务提示词",
            Role = AgentRole.Executor,
            Name = "explore"
        }
    };

    private static Mock<IAgentService> CreateAgentService(params AgentStreamChunk[] chunks)
    {
        var mock = new Mock<IAgentService>();
        mock.Setup(s => s.RunAgentStreamAsync(It.IsAny<AgentSpawnOptions>(), It.IsAny<CancellationToken>()))
            .Returns(chunks.ToAsyncEnumerable());
        return mock;
    }

    private static async Task<IReadOnlyList<ChatStreamEvent>> InvokeAsync(AgentStreamChunk[] chunks)
    {
        var sut = new AgentStreamExecutionMiddleware(CreateAgentService(chunks).Object);
        var context = CreateContext();
        var channel = new SubAgentEventChannel();

        using (channel.EnterScope())
        {
            await sut.InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None);
        }

        return channel.TryDrain();
    }

    [Fact]
    public async Task Should_EmitStartedFirst_WithIdentityFromSpawnOptions()
    {
        var events = await InvokeAsync(
        [
            new AgentStreamChunk { Type = AgentStreamChunkType.Content, Content = "开始", AgentId = "ag-1" }
        ]);

        events.Should().NotBeEmpty();
        var started = events[0];
        started.Type.Should().Be(ChatStreamEventType.AgentStarted);
        started.AgentName.Should().Be("explore");
        started.AgentDescription.Should().Be("调研 GUI 方案");
    }

    [Fact]
    public async Task Should_StampAgentId_OnActivityEvents()
    {
        var events = await InvokeAsync(
        [
            new AgentStreamChunk { Type = AgentStreamChunkType.ToolCallStart, ToolName = "FileRead", ToolCallId = "c1", AgentId = "ag-2" },
            new AgentStreamChunk { Type = AgentStreamChunkType.ToolCallEnd, ToolName = "FileRead", ToolCallId = "c1", ToolResultText = "ok", AgentId = "ag-2" },
            new AgentStreamChunk { Type = AgentStreamChunkType.Content, Content = "正文", AgentId = "ag-2" }
        ]);

        var activities = events.Where(e => e.Type is not (ChatStreamEventType.AgentStarted or ChatStreamEventType.AgentFinished)).ToList();
        activities.Should().HaveCount(3);
        activities.Should().OnlyContain(e => e.IsSubAgentActivity);
        activities.Select(e => e.AgentId).Should().OnlyContain(id => id == "ag-2");
        activities[0].ToolName.Should().Be("FileRead");
    }

    [Fact]
    public async Task Should_EmitFinishedLast_WithStatistics()
    {
        var events = await InvokeAsync(
        [
            new AgentStreamChunk { Type = AgentStreamChunkType.Content, Content = "工作", AgentId = "ag-3" },
            new AgentStreamChunk { Type = AgentStreamChunkType.Complete, Content = "最终输出", ExecutionTimeMs = 42_000, Usage = new TokenUsage(10, 20), AgentId = "ag-3" }
        ]);

        var finished = events[^1];
        finished.Type.Should().Be(ChatStreamEventType.AgentFinished);
        finished.AgentSuccess.Should().BeTrue();
        finished.AgentExecutionTimeMs.Should().Be(42_000);
        finished.Content.Should().Be("最终输出");
        finished.Usage.Should().NotBeNull();
    }

    [Fact]
    public async Task OnError_Should_FinishWithFailure()
    {
        var events = await InvokeAsync(
        [
            new AgentStreamChunk { Type = AgentStreamChunkType.Error, Content = "boom", AgentId = "ag-4" }
        ]);

        var finished = events[^1];
        finished.Type.Should().Be(ChatStreamEventType.AgentFinished);
        finished.AgentSuccess.Should().BeFalse();
        finished.Content.Should().Be("boom");
    }

    [Fact]
    public async Task WithoutChannel_Should_NotThrow()
    {
        // 无 GUI 通道时（CLI 纯文本模式等）静默跳过发射，执行不受影响
        var sut = new AgentStreamExecutionMiddleware(CreateAgentService(
            new AgentStreamChunk { Type = AgentStreamChunkType.Complete, Content = "done", ExecutionTimeMs = 1, AgentId = "ag-5" }).Object);
        var context = CreateContext();

        var act = async () => await sut.InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None);

        await act.Should().NotThrowAsync();
        context.Succeeded.Should().BeTrue();
    }
}
