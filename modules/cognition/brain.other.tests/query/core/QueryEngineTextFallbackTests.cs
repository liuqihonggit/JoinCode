namespace Core.Tests.Query.Fallback;

/// <summary>
/// TASK103 S6 — QueryEngine 文本工具调用 fallback 解析与异常输出干预
/// （chat 循环能力迁入 query 后端后，避免两套循环行为漂移）。
/// </summary>
public sealed class QueryEngineTextFallbackTests {
    /// <summary>可编程响应序列的假查询服务 — 每次 LLM 调用消费一批事件。</summary>
    private sealed class SequenceQueryService : IQueryService {
        private readonly Queue<StreamEvent[]> _responses = new();

        public int Calls { get; private set; }

        public void Enqueue(params StreamEvent[] events) => _responses.Enqueue(events);

        public Task<IReadOnlyList<ApiMessage>> GetApiMessageContentsAsync(
            MessageList chatHistory, ChatOptions? executionSettings = null, IChatClient? kernel = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("测试不走非流式接口");

        public async IAsyncEnumerable<StreamEvent> GetStreamEventContentsAsync(
            MessageList chatHistory, ChatOptions? executionSettings = null, IChatClient? kernel = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default) {
            Calls++;
            var batch = _responses.Count > 0 ? _responses.Dequeue() : [];
            foreach (var evt in batch) {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return evt;
            }
        }
    }

    /// <summary>委托驱动的假 fallback 解析器。</summary>
    private sealed class StubParser : ITextToolCallFallbackParser {
        private readonly Func<string, TextToolCallFallbackResult?> _parse;

        public StubParser(Func<string, TextToolCallFallbackResult?> parse) => _parse = parse;

        public TextToolCallFallbackResult? TryParse(ReadOnlySpan<char> content) => _parse(content.ToString());
    }

    private static StreamEvent Text(string content) => new() { Content = content };

    private static (QueryEngine Engine, SequenceQueryService QueryService, Mock<IToolRegistry> Registry) CreateEngine(
        ITextToolCallFallbackParser? parser) {
        var queryService = new SequenceQueryService();
        var kernel = new Mock<IChatClient>();
        kernel.Setup(k => k.GetChatCompletionService()).Returns(queryService);

        var registry = new Mock<IToolRegistry>();
        registry.Setup(r => r.ExecuteToolAsync(
                It.IsAny<string>(), It.IsAny<Dictionary<string, JsonElement>>(), It.IsAny<CancellationToken>(), It.IsAny<ToolProgressCallback?>()))
            .ReturnsAsync(new ToolResult {
                Content = [new ToolContent { Type = ToolContentType.Text, Text = "工具已执行" }]
            });

        IServiceProvider? provider = null;
        if (parser is not null) {
            var providerMock = new Mock<IServiceProvider>();
            providerMock.Setup(p => p.GetService(typeof(ITextToolCallFallbackParser))).Returns(parser);
            provider = providerMock.Object;
        }

        var engine = new QueryEngine(
            kernel.Object,
            registry.Object,
            Microsoft.Extensions.Options.Options.Create(new QueryEngineConfig()),
            serviceProvider: provider,
            logger: NullLogger<QueryEngine>.Instance);

        return (engine, queryService, registry);
    }

    private static async Task<List<QueryStreamChunk>> RunAsync(QueryEngine engine, MessageList history, QueryOptions options) {
        var chunks = new List<QueryStreamChunk>();
        await foreach (var chunk in engine.QueryAsync("继续任务", history, options).ConfigureAwait(false)) {
            chunks.Add(chunk);
        }

        return chunks;
    }

    [Fact]
    public async Task TextFallback_ParsedToolCall_ExecutesTool_AndKeepsRemainingText() {
        var parser = new StubParser(content => content.Contains("<antml:", StringComparison.Ordinal)
            ? new TextToolCallFallbackResult {
                ToolCalls = [new TextToolCallFallbackCall("echo", """{"text":"hi"}""", "call-x")],
                RemainingText = "先说明"
            }
            : null);
        var (engine, queryService, registry) = CreateEngine(parser);
        var history = new MessageList();

        queryService.Enqueue(Text("""先说明<antml:invoke name="echo"/>"""));
        queryService.Enqueue(Text("完成"));

        var chunks = await RunAsync(engine, history, new QueryOptions()).ConfigureAwait(true);

        queryService.Calls.Should().Be(2, "fallback 解析成功后应执行工具并继续下一轮");
        registry.Verify(r => r.ExecuteToolAsync(
            "echo", It.IsAny<Dictionary<string, JsonElement>>(), It.IsAny<CancellationToken>(), It.IsAny<ToolProgressCallback?>()), Times.Once);
        history.Should().Contain(m => m.Role == MessageRole.Assistant && m.Content == "先说明",
            "assistant 消息只保留工具调用块之外的纯文本");
        history.Should().Contain(m => m.Role == MessageRole.Tool && m.Content != null && m.Content.Contains("工具已执行"));
        chunks.Should().Contain(c => c.Type == AgentStreamChunkType.Complete && c.Content == "完成");
    }

    [Fact]
    public async Task MalformedXml_InjectsCorrectionPrompt_ThenContinues() {
        var parser = new StubParser(_ => null);
        var (engine, queryService, _) = CreateEngine(parser);
        var history = new MessageList();

        queryService.Enqueue(Text("""请调用<antml:invoke name="echo">"""));
        queryService.Enqueue(Text("好，现在正常回答"));

        var chunks = await RunAsync(engine, history, new QueryOptions()).ConfigureAwait(true);

        queryService.Calls.Should().Be(2, "可疑 XML 标签未解析时应注入纠正提示并继续一轮");
        history.Should().Contain(m => m.Role == MessageRole.User && m.Content != null && m.Content.Contains("异常的 XML 工具调用标签"),
            "纠正提示应以 user 消息回灌（解释原因 + 正确做法）");
        chunks.Should().Contain(c => c.Type == AgentStreamChunkType.Complete && c.Content == "好，现在正常回答");
    }

    [Fact]
    public async Task MalformedXml_ExceedsMaxConsecutive_ForcesCompletionWithWarning() {
        var parser = new StubParser(_ => null);
        var (engine, queryService, _) = CreateEngine(parser);
        var history = new MessageList();

        for (var i = 0; i < 4; i++)
            queryService.Enqueue(Text("""<antml:invoke name="echo">"""));

        var chunks = await RunAsync(engine, history, new QueryOptions()).ConfigureAwait(true);

        queryService.Calls.Should().Be(4, "3 次纠正后第 4 次异常应强制结束");
        chunks.Should().Contain(c => c.Type == AgentStreamChunkType.Content && c.Content != null && c.Content.Contains("连续3次输出异常"),
            "超限应上屏警告");
        chunks.Should().Contain(c => c.Type == AgentStreamChunkType.Complete);
        history.Count(m => m.Role == MessageRole.User && m.Content != null && m.Content.Contains("异常的 XML 工具调用标签"))
            .Should().Be(3, "仅前 3 次注入纠正提示");
    }
}
