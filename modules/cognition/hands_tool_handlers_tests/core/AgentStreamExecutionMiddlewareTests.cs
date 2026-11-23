// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
namespace Hands.Tests.ToolHandlers;

/// <summary>
/// AgentStreamExecutionMiddleware 子代理事件转发测试 —
/// 中间件消费 RunAgentStreamAsync 的 chunk 时必须向 SubAgentEventChannel 发射：
/// AgentStarted（首个 chunk 时，携带身份）→ 活动事件（AgentId 标记）→ AgentFinished（统计收尾）。
/// 这是 GUI 多 subAgent 运行期显示的引擎侧数据源契约。
/// </summary>
public class AgentStreamExecutionMiddlewareTests {
    private static AgentToolContext CreateContext() => new() {
        Description = "调研 GUI 方案",
        Prompt = "调研任务提示词",
        SubagentRole = AgentRole.Executor,
        SpawnOptions = new AgentSpawnOptions {
            Description = "调研 GUI 方案",
            Prompt = "调研任务提示词",
            Role = AgentRole.Executor,
            Name = "explore"
        }
    };

    private static Mock<IAgentService> CreateAgentService(params AgentStreamChunk[] chunks) {
        var mock = new Mock<IAgentService>();
        mock.Setup(s => s.RunAgentStreamAsync(It.IsAny<AgentSpawnOptions>(), It.IsAny<CancellationToken>()))
            .Returns(chunks.ToAsyncEnumerable());
        return mock;
    }

    private static async Task<IReadOnlyList<ChatStreamEvent>> InvokeAsync(AgentStreamChunk[] chunks) {
        await using var sut = new AgentStreamExecutionMiddleware(CreateAgentService(chunks).Object);
        var context = CreateContext();
        var channel = new SubAgentEventChannel();

        using (channel.EnterScope()) {
            await sut.InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None);
        }

        return channel.TryDrain();
    }

    [Fact]
    public async Task Should_EmitStartedFirst_WithIdentityFromSpawnOptions() {
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
    public async Task Should_StampAgentId_OnActivityEvents() {
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
    public async Task Should_EmitFinishedLast_WithStatistics() {
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
    public async Task OnError_Should_FinishWithFailure() {
        var events = await InvokeAsync(
        [
            new AgentStreamChunk { Type = AgentStreamChunkType.Error, Content = "boom", AgentId = "ag-4" }
        ]);

        var finished = events[^1];
        finished.Type.Should().Be(ChatStreamEventType.AgentFinished);
        finished.AgentSuccess.Should().BeFalse();
        finished.Content.Should().Be("boom");
    }

    /// <summary>
    /// RunAgentStreamAsync 抛异常时（如 LLM 500 错误），仍须发射配对的 agent_started/agent_finished 事件。
    /// 修复前：异常直接传播，agent_started/agent_finished 从未发射，GUI 多 subAgent 运行期丢失生命周期边界。
    /// </summary>
    [Fact]
    public async Task OnException_Should_EmitStartedAndFinishedWithFailure() {
        var mock = new Mock<IAgentService>();
        mock.Setup(s => s.RunAgentStreamAsync(It.IsAny<AgentSpawnOptions>(), It.IsAny<CancellationToken>()))
            .Returns(new ThrowingAsyncEnumerable<AgentStreamChunk>(new InvalidOperationException("LLM 500")));
        await using var sut = new AgentStreamExecutionMiddleware(mock.Object);
        var context = CreateContext();
        var channel = new SubAgentEventChannel();

        using (channel.EnterScope()) {
            await sut.InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None);
        }

        var events = channel.TryDrain();
        events.Should().HaveCount(2);
        events[0].Type.Should().Be(ChatStreamEventType.AgentStarted);
        events[1].Type.Should().Be(ChatStreamEventType.AgentFinished);
        events[1].AgentSuccess.Should().BeFalse();
        events[1].Content.Should().Be("LLM 500");
        context.Succeeded.Should().BeFalse();
        context.ErrorMessage.Should().Be("LLM 500");
        context.AgentId.Should().NotBeNull();
    }

    /// <summary>
    /// 异常发生在首个 chunk 之后时，agentId 已从 chunk 获取，AgentFinished 应携带该 agentId。
    /// </summary>
    [Fact]
    public async Task OnException_AfterFirstChunk_Should_EmitFinishedWithExistingAgentId() {
        var mock = new Mock<IAgentService>();
        mock.Setup(s => s.RunAgentStreamAsync(It.IsAny<AgentSpawnOptions>(), It.IsAny<CancellationToken>()))
            .Returns(new ChunksThenThrowAsyncEnumerable(
                new AgentStreamChunk { Type = AgentStreamChunkType.Content, Content = "部分输出", AgentId = "ag-partial" },
                new InvalidOperationException("mid-stream failure")));
        await using var sut = new AgentStreamExecutionMiddleware(mock.Object);
        var context = CreateContext();
        var channel = new SubAgentEventChannel();

        using (channel.EnterScope()) {
            await sut.InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None);
        }

        var events = channel.TryDrain();
        events[0].Type.Should().Be(ChatStreamEventType.AgentStarted);
        events[0].AgentId.Should().Be("ag-partial");
        events[^1].Type.Should().Be(ChatStreamEventType.AgentFinished);
        events[^1].AgentId.Should().Be("ag-partial");
        events[^1].AgentSuccess.Should().BeFalse();
        context.AgentId.Should().Be("ag-partial");
    }

    /// <summary>
    /// 异常时无 GUI 通道（CLI 纯文本模式）— 不应抛异常，应正常返回失败结果。
    /// </summary>
    [Fact]
    public async Task OnException_WithoutChannel_Should_NotThrow() {
        var mock = new Mock<IAgentService>();
        mock.Setup(s => s.RunAgentStreamAsync(It.IsAny<AgentSpawnOptions>(), It.IsAny<CancellationToken>()))
            .Returns(new ThrowingAsyncEnumerable<AgentStreamChunk>(new InvalidOperationException("no channel")));
        await using var sut = new AgentStreamExecutionMiddleware(mock.Object);
        var context = CreateContext();

        var act = async () => await sut.InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None);

        await act.Should().NotThrowAsync();
        context.Succeeded.Should().BeFalse();
        context.ErrorMessage.Should().Be("no channel");
    }

    private sealed class ThrowingAsyncEnumerable<T> : IAsyncEnumerable<T> {
        private readonly Exception _ex;
        public ThrowingAsyncEnumerable(Exception ex) => _ex = ex;
        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) => new ThrowingEnumerator<T>(_ex);
    }

    private sealed class ThrowingEnumerator<T> : IAsyncEnumerator<T> {
        private readonly Exception _ex;
        public ThrowingEnumerator(Exception ex) => _ex = ex;
        public T Current => throw new InvalidOperationException("No current");
        public ValueTask<bool> MoveNextAsync() => throw _ex;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ChunksThenThrowAsyncEnumerable : IAsyncEnumerable<AgentStreamChunk> {
        private readonly AgentStreamChunk _first;
        private readonly Exception _ex;
        public ChunksThenThrowAsyncEnumerable(AgentStreamChunk first, Exception ex) { _first = first; _ex = ex; }
        public IAsyncEnumerator<AgentStreamChunk> GetAsyncEnumerator(CancellationToken cancellationToken = default) => new ChunksThenThrowEnumerator(_first, _ex);
    }

    private sealed class ChunksThenThrowEnumerator : IAsyncEnumerator<AgentStreamChunk> {
        private readonly AgentStreamChunk _first;
        private readonly Exception _ex;
        private int _state;
        public ChunksThenThrowEnumerator(AgentStreamChunk first, Exception ex) { _first = first; _ex = ex; _state = 0; }
        public AgentStreamChunk Current => _first;
        public ValueTask<bool> MoveNextAsync() {
            if (_state == 0) { _state = 1; return new(true); }
            throw _ex;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task WithoutChannel_Should_NotThrow() {
        // 无 GUI 通道时（CLI 纯文本模式等）静默跳过发射，执行不受影响
        await using var sut = new AgentStreamExecutionMiddleware(CreateAgentService(
            new AgentStreamChunk { Type = AgentStreamChunkType.Complete, Content = "done", ExecutionTimeMs = 1, AgentId = "ag-5" }).Object);
        var context = CreateContext();

        var act = async () => await sut.InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None);

        await act.Should().NotThrowAsync();
        context.Succeeded.Should().BeTrue();
    }

    // === BuildChunkEvent 纯计算测试 ===

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public void BuildChunkEvent_Content_ReturnsContentEventWithAgentId() {
        var chunk = new AgentStreamChunk { Type = AgentStreamChunkType.Content, Content = "hello", AgentId = "ag-1" };
        var evt = AgentStreamExecutionMiddleware.BuildChunkEvent(chunk, "ag-1");
        evt.Should().NotBeNull();
        evt!.Type.Should().Be(ChatStreamEventType.Content);
        evt.Content.Should().Be("hello");
        evt.AgentId.Should().Be("ag-1");
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public void BuildChunkEvent_ThinkingStart_WithContent_ReturnsThinkingEvent() {
        var chunk = new AgentStreamChunk { Type = AgentStreamChunkType.ThinkingStart, ThinkingContent = "思考中", AgentId = "ag-2" };
        var evt = AgentStreamExecutionMiddleware.BuildChunkEvent(chunk, "ag-2");
        evt.Should().NotBeNull();
        evt!.Type.Should().Be(ChatStreamEventType.Thinking);
        evt.ThinkingContent.Should().Be("思考中");
        evt.AgentId.Should().Be("ag-2");
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public void BuildChunkEvent_Thinking_WithContent_ReturnsThinkingEvent() {
        var chunk = new AgentStreamChunk { Type = AgentStreamChunkType.Thinking, ThinkingContent = "继续思考", AgentId = "ag-3" };
        var evt = AgentStreamExecutionMiddleware.BuildChunkEvent(chunk, "ag-3");
        evt.Should().NotBeNull();
        evt!.Type.Should().Be(ChatStreamEventType.Thinking);
        evt.ThinkingContent.Should().Be("继续思考");
    }

    [Fact]
    public void BuildChunkEvent_Thinking_WithEmptyContent_ReturnsNull() {
        var chunk = new AgentStreamChunk { Type = AgentStreamChunkType.Thinking, ThinkingContent = "", Content = "", AgentId = "ag-4" };
        var evt = AgentStreamExecutionMiddleware.BuildChunkEvent(chunk, "ag-4");
        evt.Should().BeNull();
    }

    [Fact]
    public void BuildChunkEvent_ThinkingStart_WithNullContent_ReturnsNull() {
        var chunk = new AgentStreamChunk { Type = AgentStreamChunkType.ThinkingStart, ThinkingContent = null, Content = null, AgentId = "ag-5" };
        var evt = AgentStreamExecutionMiddleware.BuildChunkEvent(chunk, "ag-5");
        evt.Should().BeNull();
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public void BuildChunkEvent_Thinking_WithOnlyContent_FallsBackToContent() {
        // ThinkingContent 为 null 但 Content 非空时,ThinkingContent 用 Content 填充
        var chunk = new AgentStreamChunk { Type = AgentStreamChunkType.Thinking, ThinkingContent = null, Content = "fallback", AgentId = "ag-6" };
        var evt = AgentStreamExecutionMiddleware.BuildChunkEvent(chunk, "ag-6");
        evt.Should().NotBeNull();
        evt!.ThinkingContent.Should().Be("fallback");
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public void BuildChunkEvent_ToolCallStart_ReturnsToolCallStartEvent() {
        var chunk = new AgentStreamChunk {
            Type = AgentStreamChunkType.ToolCallStart,
            ToolName = "FileRead",
            ToolCallId = "call-1",
            ToolArguments = "{\"path\":\"/tmp\"}",
            AgentId = "ag-7"
        };
        var evt = AgentStreamExecutionMiddleware.BuildChunkEvent(chunk, "ag-7");
        evt.Should().NotBeNull();
        evt!.Type.Should().Be(ChatStreamEventType.ToolCallStart);
        evt.ToolName.Should().Be("FileRead");
        evt.ToolCallId.Should().Be("call-1");
        evt.ToolArguments.Should().Be("{\"path\":\"/tmp\"}");
        evt.AgentId.Should().Be("ag-7");
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public void BuildChunkEvent_ToolCallEnd_ReturnsToolCallEndEvent() {
        var chunk = new AgentStreamChunk {
            Type = AgentStreamChunkType.ToolCallEnd,
            ToolName = "FileEdit",
            ToolCallId = "call-2",
            ToolResultText = "ok",
            IsToolError = false,
            StructuredPatch = null,
            AgentId = "ag-8"
        };
        var evt = AgentStreamExecutionMiddleware.BuildChunkEvent(chunk, "ag-8");
        evt.Should().NotBeNull();
        evt!.Type.Should().Be(ChatStreamEventType.ToolCallEnd);
        evt.ToolName.Should().Be("FileEdit");
        evt.ToolCallId.Should().Be("call-2");
        evt.ToolResultText.Should().Be("ok");
        evt.IsToolError.Should().BeFalse();
        evt.StructuredPatch.Should().BeNull();
        evt.AgentId.Should().Be("ag-8");
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public void BuildChunkEvent_ToolCallEnd_PreservesStructuredPatch() {
        // StructuredPatch 引用应原样传递（同引用,不拷贝）
        var patch = new StructuredPatchHunk[] { new() { OldStart = 0, OldLines = 0, NewStart = 0, NewLines = 0 } };
        var chunk = new AgentStreamChunk {
            Type = AgentStreamChunkType.ToolCallEnd,
            ToolName = "FileEdit",
            StructuredPatch = patch,
            AgentId = "ag-8b"
        };
        var evt = AgentStreamExecutionMiddleware.BuildChunkEvent(chunk, "ag-8b");
        evt.Should().NotBeNull();
        evt!.StructuredPatch.Should().BeSameAs(patch);
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public void BuildChunkEvent_ToolCallEnd_WithError_PreservesErrorFlag() {
        var chunk = new AgentStreamChunk {
            Type = AgentStreamChunkType.ToolCallEnd,
            ToolName = "Bash",
            ToolCallId = "call-3",
            ToolResultText = "failed",
            IsToolError = true,
            AgentId = "ag-9"
        };
        var evt = AgentStreamExecutionMiddleware.BuildChunkEvent(chunk, "ag-9");
        evt.Should().NotBeNull();
        evt!.IsToolError.Should().BeTrue();
        evt.ToolResultText.Should().Be("failed");
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public void BuildChunkEvent_ToolProgress_ReturnsToolProgressEvent() {
        var chunk = new AgentStreamChunk {
            Type = AgentStreamChunkType.ToolProgress,
            ToolName = "WebSearch",
            ToolCallId = "call-4",
            ProgressType = "query_update",
            ProgressMessage = "正在搜索...",
            AgentId = "ag-10"
        };
        var evt = AgentStreamExecutionMiddleware.BuildChunkEvent(chunk, "ag-10");
        evt.Should().NotBeNull();
        evt!.Type.Should().Be(ChatStreamEventType.ToolProgress);
        evt.ToolName.Should().Be("WebSearch");
        evt.ToolCallId.Should().Be("call-4");
        evt.ProgressType.Should().Be("query_update");
        evt.ProgressMessage.Should().Be("正在搜索...");
        evt.AgentId.Should().Be("ag-10");
    }

    [Fact]
    public void BuildChunkEvent_Complete_ReturnsNull() {
        // Complete 块由主流程处理状态(ExecutionTimeMs/finalOutput/finalUsage),不发射事件
        var chunk = new AgentStreamChunk { Type = AgentStreamChunkType.Complete, Content = "最终输出", ExecutionTimeMs = 100, AgentId = "ag-11" };
        var evt = AgentStreamExecutionMiddleware.BuildChunkEvent(chunk, "ag-11");
        evt.Should().BeNull();
    }

    [Fact]
    public void BuildChunkEvent_Error_ReturnsNull() {
        // Error 块由主流程处理状态(succeeded/errorMessage),不发射事件
        var chunk = new AgentStreamChunk { Type = AgentStreamChunkType.Error, Content = "boom", AgentId = "ag-12" };
        var evt = AgentStreamExecutionMiddleware.BuildChunkEvent(chunk, "ag-12");
        evt.Should().BeNull();
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public void BuildChunkEvent_AllEventTypes_StampAgentId() {
        // 所有事件类型都应携带 agentId 参数（非 chunk.AgentId）,因为 agentId 在首块后由主流程填充
        const string expectedAgentId = "resolved-id";
        var contentChunk = new AgentStreamChunk { Type = AgentStreamChunkType.Content, Content = "x", AgentId = "raw-1" };
        var toolStartChunk = new AgentStreamChunk { Type = AgentStreamChunkType.ToolCallStart, ToolName = "T", AgentId = "raw-2" };
        var toolEndChunk = new AgentStreamChunk { Type = AgentStreamChunkType.ToolCallEnd, ToolName = "T", AgentId = "raw-3" };
        var toolProgChunk = new AgentStreamChunk { Type = AgentStreamChunkType.ToolProgress, ToolName = "T", AgentId = "raw-4" };

        AgentStreamExecutionMiddleware.BuildChunkEvent(contentChunk, expectedAgentId)!.AgentId.Should().Be(expectedAgentId);
        AgentStreamExecutionMiddleware.BuildChunkEvent(toolStartChunk, expectedAgentId)!.AgentId.Should().Be(expectedAgentId);
        AgentStreamExecutionMiddleware.BuildChunkEvent(toolEndChunk, expectedAgentId)!.AgentId.Should().Be(expectedAgentId);
        AgentStreamExecutionMiddleware.BuildChunkEvent(toolProgChunk, expectedAgentId)!.AgentId.Should().Be(expectedAgentId);
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public void BuildChunkEvent_NullAgentId_PreservesNull() {
        // agentId 参数为 null 时（首块前理论上不会调用,但方法应容忍）,事件 AgentId 为 null
        var chunk = new AgentStreamChunk { Type = AgentStreamChunkType.Content, Content = "x", AgentId = "ag-13" };
        var evt = AgentStreamExecutionMiddleware.BuildChunkEvent(chunk, null);
        evt.Should().NotBeNull();
        evt!.AgentId.Should().BeNull();
    }
}