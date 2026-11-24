namespace Core.Tests.Query.Gate;

/// <summary>
/// TASK102/TASK132 轮次闸门 — QueryEngine 收尾拦截集成测试（假 IQueryService 驱动完整循环）。
/// </summary>
public sealed class QueryEngineRoundGateTests {
    /// <summary>可编程响应序列的假查询服务 — 每次 LLM 调用消费一批事件。</summary>
    private sealed class SequenceQueryService : IQueryService {
        private readonly Queue<StreamEvent[]> _responses = new();

        public void Enqueue(params StreamEvent[] events) => _responses.Enqueue(events);

        public Task<IReadOnlyList<ApiMessage>> GetApiMessageContentsAsync(
            MessageList chatHistory, ChatOptions? executionSettings = null, IChatClient? kernel = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("测试不走非流式接口");

        public async IAsyncEnumerable<StreamEvent> GetStreamEventContentsAsync(
            MessageList chatHistory, ChatOptions? executionSettings = null, IChatClient? kernel = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default) {
            var batch = _responses.Count > 0 ? _responses.Dequeue() : [];
            foreach (var evt in batch) {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return evt;
            }
        }
    }

    private static StreamEvent Text(string content) => new() { Content = content };

    /// <summary>闸门工具参数 DTO — 用序列化器生成 JSON，禁止手写拼接。</summary>
    private sealed record GateArguments {
        [JsonPropertyName("choice")] public int Choice { get; init; }

        [JsonPropertyName("verdict")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Verdict { get; init; }
    }

    /// <summary>构造一次闸门工具调用 — 参数用序列化器编码，避免手写转义。</summary>
    private static StreamEvent GateCall(int choice, string? verdict = null) {
        var argumentsJson = JsonSerializer.Serialize(new GateArguments { Choice = choice, Verdict = verdict });
        var toolCallsJson = JsonSerializer.Serialize(new[] {
            new Dictionary<string, string> {
                ["Id"] = "call-gate",
                ["Name"] = "round_gate",
                ["Arguments"] = argumentsJson,
            }
        });

        using var document = JsonDocument.Parse(toolCallsJson);
        return new StreamEvent {
            Metadata = new Dictionary<string, JsonElement> {
                ["AllToolCalls"] = document.RootElement.Clone()
            }
        };
    }

    private static (QueryEngine Engine, SequenceQueryService QueryService, Mock<IToolRegistry> Registry) CreateEngine() {
        var queryService = new SequenceQueryService();
        var kernel = new Mock<IChatClient>();
        kernel.Setup(k => k.GetChatCompletionService()).Returns(queryService);

        var registry = new Mock<IToolRegistry>();
        registry.Setup(r => r.ExecuteToolAsync(
                It.IsAny<string>(), It.IsAny<Dictionary<string, JsonElement>>(), It.IsAny<CancellationToken>(), It.IsAny<ToolProgressCallback?>()))
            .ReturnsAsync(new ToolResult {
                Content = [new ToolContent { Type = ToolContentType.Text, Text = "闸门已记录" }]
            });

        var engine = new QueryEngine(
            kernel.Object,
            registry.Object,
            Microsoft.Extensions.Options.Options.Create(new QueryEngineConfig()),
            serviceProvider: null,
            logger: NullLogger<QueryEngine>.Instance);

        return (engine, queryService, registry);
    }

    private static async Task<List<QueryStreamChunk>> RunAsync(
        QueryEngine engine, MessageList history, QueryOptions options, CancellationToken ct = default) {
        var chunks = new List<QueryStreamChunk>();
        await foreach (var chunk in engine.QueryAsync("继续任务", history, options, ct).ConfigureAwait(false)) {
            chunks.Add(chunk);
        }

        return chunks;
    }

    [Fact]
    public async Task StopWithoutGate_IsBlocked_ThenSelfAcceptAndDoneAllowCompletion() {
        using var env = EnvVarScope.Set(JccEnvVar.RoundGateMaxReject.ToValue(), "6");
        var (engine, queryService, registry) = CreateEngine();
        var history = new MessageList();

        queryService.Enqueue(Text("我先停一下"));
        queryService.Enqueue(GateCall(3, verdict: "pass"));
        queryService.Enqueue(GateCall(5));
        queryService.Enqueue(Text("收尾总结"));

        var chunks = await RunAsync(engine, history, new QueryOptions { RequireRoundGate = true }).ConfigureAwait(true);

        chunks.Should().Contain(c => c.Type == AgentStreamChunkType.Complete, "3 自验通过后选 5 完成应正常收尾");
        history.Should().Contain(m => m.Role == MessageRole.User && m.Content != null && m.Content.Contains("收尾被拦截"),
            "首次收尾应被拦截并回灌阻断消息");
        registry.Verify(r => r.ExecuteToolAsync(
            "round_gate", It.IsAny<Dictionary<string, JsonElement>>(), It.IsAny<CancellationToken>(), It.IsAny<ToolProgressCallback?>()), Times.Exactly(2));
    }

    [Fact]
    public async Task DoneWithoutSelfAcceptance_IsBlockedByVerificationChain() {
        using var env = EnvVarScope.Set(JccEnvVar.RoundGateMaxReject.ToValue(), "2");
        var (engine, queryService, _) = CreateEngine();
        var history = new MessageList();

        queryService.Enqueue(Text("我先停一下"));
        queryService.Enqueue(GateCall(5));
        queryService.Enqueue(Text("完成收尾"));

        var chunks = await RunAsync(engine, history, new QueryOptions { RequireRoundGate = true }).ConfigureAwait(true);

        chunks.Should().Contain(c => c.Type == AgentStreamChunkType.Complete);
        history.Should().Contain(m => m.Content != null && m.Content.Contains("5 完成被拒"),
            "产出必须先过 3 自验才能选 5 完成");
    }

    [Fact]
    public async Task GateDisabled_StopsImmediately() {
        var (engine, queryService, registry) = CreateEngine();
        var history = new MessageList();

        queryService.Enqueue(Text("普通回答"));

        var chunks = await RunAsync(engine, history, new QueryOptions { RequireRoundGate = false }).ConfigureAwait(true);

        chunks.Should().Contain(c => c.Type == AgentStreamChunkType.Complete);
        history.Should().NotContain(m => m.Content != null && m.Content.Contains("收尾被拦截"));
        registry.Verify(r => r.ExecuteToolAsync(
            It.IsAny<string>(), It.IsAny<Dictionary<string, JsonElement>>(), It.IsAny<CancellationToken>(), It.IsAny<ToolProgressCallback?>()), Times.Never);
    }

    [Fact]
    public async Task ErroredGateCall_DoesNotMutateGateState() {
        using var env = EnvVarScope.Set(JccEnvVar.RoundGateMaxReject.ToValue(), "2");
        var (engine, queryService, registry) = CreateEngine();
        registry.Setup(r => r.ExecuteToolAsync(
                It.IsAny<string>(), It.IsAny<Dictionary<string, JsonElement>>(), It.IsAny<CancellationToken>(), It.IsAny<ToolProgressCallback?>()))
            .ReturnsAsync(new ToolResult {
                IsError = true,
                Content = [new ToolContent { Type = ToolContentType.Text, Text = "round_gate 参数错误: verdict=未提供" }]
            });
        var history = new MessageList();

        queryService.Enqueue(Text("第一次想停"));
        queryService.Enqueue(GateCall(3));
        queryService.Enqueue(Text("第二次想停"));

        var chunks = await RunAsync(engine, history, new QueryOptions { RequireRoundGate = true }).ConfigureAwait(true);

        chunks.Should().Contain(c => c.Type == AgentStreamChunkType.Complete);
        history.Should().Contain(m => m.Content != null && m.Content.Contains("还没有调用"),
            "报错的闸门调用不算一次有效选择，状态机必须保持不变");
        history.Should().NotContain(m => m.Content != null && m.Content.Contains("是中间态"),
            "若把报错调用记成 choice=3，收尾会被误判成中间态");
    }

    [Fact]
    public async Task RejectionLimitReached_ForcesCompletionWithWarning() {
        using var env = EnvVarScope.Set(JccEnvVar.RoundGateMaxReject.ToValue(), "1");
        var (engine, queryService, _) = CreateEngine();
        var history = new MessageList();

        queryService.Enqueue(Text("第一次想停"));
        queryService.Enqueue(Text("第二次想停"));

        var chunks = await RunAsync(engine, history, new QueryOptions { RequireRoundGate = true }).ConfigureAwait(true);

        chunks.Should().Contain(c => c.Type == AgentStreamChunkType.Complete);
        chunks.Should().Contain(c => c.Content != null && c.Content.Contains("强制结束"), "达到上限应上屏警告并放行");
        history.Count(m => m.Content != null && m.Content.Contains("收尾被拦截")).Should().Be(1);
    }

    [Fact]
    public async Task GateToolDeniedInOptions_SkipsEnforcement() {
        var (engine, queryService, registry) = CreateEngine();
        var history = new MessageList();

        queryService.Enqueue(Text("普通回答"));

        var chunks = await RunAsync(engine, history, new QueryOptions {
            RequireRoundGate = true,
            DeniedTools = ["round_gate"]
        }).ConfigureAwait(true);

        chunks.Should().Contain(c => c.Type == AgentStreamChunkType.Complete);
        history.Should().NotContain(m => m.Content != null && m.Content.Contains("收尾被拦截"));
        registry.Verify(r => r.ExecuteToolAsync(
            It.IsAny<string>(), It.IsAny<Dictionary<string, JsonElement>>(), It.IsAny<CancellationToken>(), It.IsAny<ToolProgressCallback?>()), Times.Never);
    }
}
