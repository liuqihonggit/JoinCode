namespace Core.Tests.Query.Cancellation;

/// <summary>
/// TASK103 — 工具执行中取消时补写 (aborted) 结果（对齐 chat 循环 WriteAbortedToolResultsAsync），
/// 保证 assistant 工具调用消息后始终有对应 tool 结果，历史对下一次请求仍合法。
/// </summary>
public sealed class QueryEngineCancellationTests {
    private sealed class OneShotQueryService : IQueryService {
        private readonly StreamEvent _response;

        public OneShotQueryService(StreamEvent response) => _response = response;

        public Task<IReadOnlyList<ApiMessage>> GetApiMessageContentsAsync(
            MessageList chatHistory, ChatOptions? executionSettings = null, IChatClient? kernel = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("测试不走非流式接口");

        public async IAsyncEnumerable<StreamEvent> GetStreamEventContentsAsync(
            MessageList chatHistory, ChatOptions? executionSettings = null, IChatClient? kernel = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default) {
            await Task.Yield();
            yield return _response;
        }
    }

    private static StreamEvent TwoToolCalls() {
        using var document = JsonDocument.Parse(
            """[{"Id":"call-1","Name":"echo","Arguments":"{}"},{"Id":"call-2","Name":"echo","Arguments":"{}"}]""");
        return new StreamEvent {
            Content = "执行两个工具",
            Metadata = new Dictionary<string, JsonElement> {
                ["AllToolCalls"] = document.RootElement.Clone()
            }
        };
    }

    [Fact]
    public async Task CancellationDuringToolExecution_WritesAbortedResultsForPendingCalls() {
        using var cts = new CancellationTokenSource();
        var queryService = new OneShotQueryService(TwoToolCalls());
        var kernel = new Mock<IChatClient>();
        kernel.Setup(k => k.GetChatCompletionService()).Returns(queryService);

        var registry = new Mock<IToolRegistry>();
        registry.Setup(r => r.ExecuteToolAsync(
                It.IsAny<string>(), It.IsAny<Dictionary<string, JsonElement>>(), It.IsAny<CancellationToken>(), It.IsAny<ToolProgressCallback?>()))
            .Returns(() => {
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            });

        await using var engine = new QueryEngine(
            kernel.Object,
            registry.Object,
            Microsoft.Extensions.Options.Options.Create(new QueryEngineConfig()),
            serviceProvider: null,
            logger: NullLogger<QueryEngine>.Instance);
        var history = new MessageList();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => {
            await foreach (var _ in engine.QueryAsync("执行工具", history, new QueryOptions(), cts.Token).ConfigureAwait(false)) {
            }
        }).ConfigureAwait(true);

        var toolResults = history.Where(m => m.Role == MessageRole.Tool).ToList();
        toolResults.Should().HaveCount(2, "取消时未执行的工具调用应补写 aborted 结果，保持历史合法");
        toolResults.Should().OnlyContain(m => m.Content == "(aborted)");
        history.Count(m => m.Role == MessageRole.Assistant).Should().Be(2,
            "每条 aborted 工具结果前应有对应的 assistant 工具调用消息");
    }
}
