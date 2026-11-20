namespace Core.Tests.Query.Parallel;

/// <summary>
/// ADR 0160 — QueryEngine 并发安全工具合批并行：同批并行执行、结果按调用顺序回写、取消补写 aborted。
/// </summary>
public sealed class QueryEngineParallelToolExecutionTests {
    /// <summary>首轮返回工具调用，后续轮返回收尾文本 — 避免引擎反复执行同一批工具直到上限。</summary>
    private sealed class ToolThenDoneQueryService : IQueryService {
        private readonly StreamEvent _toolResponse;
        private int _calls;

        public ToolThenDoneQueryService(StreamEvent toolResponse) => _toolResponse = toolResponse;

        public Task<IReadOnlyList<ApiMessage>> GetApiMessageContentsAsync(
            MessageList chatHistory, ChatOptions? executionSettings = null, IChatClient? kernel = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("测试不走非流式接口");

        public async IAsyncEnumerable<StreamEvent> GetStreamEventContentsAsync(
            MessageList chatHistory, ChatOptions? executionSettings = null, IChatClient? kernel = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default) {
            _calls++;
            await Task.Yield();
            yield return _calls == 1 ? _toolResponse : new StreamEvent { Content = "完成" };
        }
    }

    private sealed class ClassifierOnlyProvider(IToolConcurrencyClassifier classifier) : IServiceProvider {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(IToolConcurrencyClassifier) ? classifier : null;
    }

    private static StreamEvent TwoToolCalls(string firstName, string secondName) {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) {
            writer.WriteStartArray();
            WriteToolCall(writer, "call-1", firstName);
            WriteToolCall(writer, "call-2", secondName);
            writer.WriteEndArray();
        }

        using var document = JsonDocument.Parse(stream.ToArray());
        return new StreamEvent {
            Content = "执行两个工具",
            Metadata = new Dictionary<string, JsonElement> {
                ["AllToolCalls"] = document.RootElement.Clone()
            }
        };
    }

    private static void WriteToolCall(Utf8JsonWriter writer, string id, string name) {
        writer.WriteStartObject();
        writer.WriteString("Id", id);
        writer.WriteString("Name", name);
        writer.WriteString("Arguments", "{}");
        writer.WriteEndObject();
    }

    private static Mock<IToolConcurrencyClassifier> SafeClassifier() {
        var classifier = new Mock<IToolConcurrencyClassifier>();
        classifier.Setup(c => c.IsConcurrencySafeAsync(
                It.IsAny<string>(), It.IsAny<Dictionary<string, JsonElement>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        return classifier;
    }

    private static QueryEngine CreateEngine(
        IQueryService queryService, Mock<IToolRegistry> registry, IToolConcurrencyClassifier classifier, Mock<IChatClient>? kernel = null) {
        kernel ??= new Mock<IChatClient>();
        kernel.Setup(k => k.GetChatCompletionService()).Returns(queryService);
        return new QueryEngine(
            kernel.Object,
            registry.Object,
            Microsoft.Extensions.Options.Options.Create(new QueryEngineConfig()),
            serviceProvider: new ClassifierOnlyProvider(classifier),
            logger: NullLogger<QueryEngine>.Instance);
    }

    [Fact]
    public async Task ConcurrencySafeCalls_InSameBatch_RunConcurrently() {
        var queryService = new ToolThenDoneQueryService(TwoToolCalls("read_a", "read_b"));

        var arrivedA = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrivedB = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var registry = new Mock<IToolRegistry>();
        registry.Setup(r => r.ExecuteToolAsync(
                It.IsAny<string>(), It.IsAny<Dictionary<string, JsonElement>>(), It.IsAny<CancellationToken>(), It.IsAny<ToolProgressCallback?>()))
            .Returns(async (string name, Dictionary<string, JsonElement>? _, CancellationToken ct, ToolProgressCallback? _) => {
                var (ownArrival, otherArrival) = name == "read_a" ? (arrivedA, arrivedB) : (arrivedB, arrivedA);
                ownArrival.TrySetResult(true);
                var winner = await Task.WhenAny(otherArrival.Task, Task.Delay(TimeSpan.FromSeconds(2), ct)).ConfigureAwait(true);
                var met = winner == otherArrival.Task;
                return new ToolResult {
                    Content = [new ToolContent { Type = ToolContentType.Text, Text = met ? $"{name}-ok" : $"{name}-timeout" }],
                    IsError = !met
                };
            });

        await using var engine = CreateEngine(queryService, registry, SafeClassifier().Object);
        var history = new MessageList();
        var toolResults = new List<ToolResult>();
        var startOrder = new List<string>();
        var endOrder = new List<string>();

        await foreach (var chunk in engine.QueryAsync("执行两个只读工具", history, new QueryOptions()).ConfigureAwait(true)) {
            if (chunk.Type == AgentStreamChunkType.ToolCallStart && chunk.ToolName is { } startName) startOrder.Add(startName);
            if (chunk.Type == AgentStreamChunkType.ToolCallEnd && chunk.ToolName is { } endName) {
                endOrder.Add(endName);
                if (chunk.ToolResult is { } result) toolResults.Add(result);
            }
        }

        toolResults.Should().HaveCount(2);
        toolResults.Should().OnlyContain(r => !r.IsError, "并发安全工具必须同批启动，串行执行会让互等屏障超时");
        startOrder.Should().Equal(["read_a", "read_b"]);
        endOrder.Should().Equal(["read_a", "read_b"]);
        history.Where(m => m.Role == MessageRole.Tool).Select(m => m.Content)
            .Should().Equal(["read_a-ok", "read_b-ok"], "历史回写必须按原调用顺序，而非完成顺序");
    }

    [Fact]
    public async Task ParallelBatch_HistoriesFollowCallOrder_EvenWhenCompletionReversed() {
        var queryService = new ToolThenDoneQueryService(TwoToolCalls("read_a", "read_b"));

        var secondCompleted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var registry = new Mock<IToolRegistry>();
        registry.Setup(r => r.ExecuteToolAsync(
                It.IsAny<string>(), It.IsAny<Dictionary<string, JsonElement>>(), It.IsAny<CancellationToken>(), It.IsAny<ToolProgressCallback?>()))
            .Returns(async (string name, Dictionary<string, JsonElement>? _, CancellationToken ct, ToolProgressCallback? _) => {
                if (name == "read_a") {
                    var winner = await Task.WhenAny(secondCompleted.Task, Task.Delay(TimeSpan.FromSeconds(2), ct)).ConfigureAwait(true);
                    if (winner != secondCompleted.Task) {
                        return new ToolResult {
                            Content = [new ToolContent { Type = ToolContentType.Text, Text = "read_a-timeout" }],
                            IsError = true
                        };
                    }
                } else {
                    secondCompleted.TrySetResult(true);
                }
                return new ToolResult {
                    Content = [new ToolContent { Type = ToolContentType.Text, Text = $"{name}-ok" }],
                    IsError = false
                };
            });

        await using var engine = CreateEngine(queryService, registry, SafeClassifier().Object);
        var history = new MessageList();

        await foreach (var _ in engine.QueryAsync("执行两个只读工具", history, new QueryOptions()).ConfigureAwait(true)) {
        }

        history.Where(m => m.Role == MessageRole.Tool).Select(m => m.Content)
            .Should().Equal(["read_a-ok", "read_b-ok"], "先完成的 read_b 不得插队，历史顺序以调用顺序为准");
    }

    [Fact]
    public async Task CancellationDuringParallelBatch_WritesAbortedResultsForAllPendingCalls() {
        using var cts = new CancellationTokenSource();
        var queryService = new ToolThenDoneQueryService(TwoToolCalls("read_a", "read_b"));

        var registry = new Mock<IToolRegistry>();
        registry.Setup(r => r.ExecuteToolAsync(
                It.IsAny<string>(), It.IsAny<Dictionary<string, JsonElement>>(), It.IsAny<CancellationToken>(), It.IsAny<ToolProgressCallback?>()))
            .Returns(() => {
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            });

        await using var engine = CreateEngine(queryService, registry, SafeClassifier().Object);
        var history = new MessageList();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => {
            await foreach (var _ in engine.QueryAsync("执行两个只读工具", history, new QueryOptions(), cts.Token).ConfigureAwait(true)) {
            }
        }).ConfigureAwait(true);

        var toolResults = history.Where(m => m.Role == MessageRole.Tool).ToList();
        toolResults.Should().HaveCount(2, "并行批次取消时所有未完成调用都要补写 aborted");
        toolResults.Should().OnlyContain(m => m.Content == "(aborted)");
        history.Count(m => m.Role == MessageRole.Assistant).Should().Be(2);
    }
}
