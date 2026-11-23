namespace Core.Tests.Query.IterationInput;

/// <summary>
/// TASK103 — QueryEngine 迭代入口回调与收尾待注入检查（chat 查询后端 steer 依赖点）。
/// </summary>
public sealed class QueryIterationInputTests {
    private sealed class CountingQueryService : IQueryService {
        public int Calls { get; private set; }
        public List<int> HistoryCounts { get; } = [];

        public Task<IReadOnlyList<ApiMessage>> GetApiMessageContentsAsync(
            MessageList chatHistory, ChatOptions? executionSettings = null, IChatClient? kernel = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("测试只走流式接口");

        public async IAsyncEnumerable<StreamEvent> GetStreamEventContentsAsync(
            MessageList chatHistory, ChatOptions? executionSettings = null, IChatClient? kernel = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default) {
            Calls++;
            HistoryCounts.Add(chatHistory.Count);
            await Task.Yield();
            yield return new StreamEvent { Content = $"第{Calls}次" };
        }
    }

    private static (QueryEngine Engine, CountingQueryService Service) CreateEngine() {
        var service = new CountingQueryService();
        var kernel = new Mock<IChatClient>();
        kernel.Setup(k => k.GetChatCompletionService()).Returns(service);
        var engine = new QueryEngine(
            kernel.Object,
            new Mock<IToolRegistry>().Object,
            Microsoft.Extensions.Options.Options.Create(new QueryEngineConfig()),
            serviceProvider: null,
            logger: NullLogger<QueryEngine>.Instance);
        return (engine, service);
    }

    [Fact]
    public async Task OnIterationStart_AppendsSteerBeforeRequest() {
        var (engine, service) = CreateEngine();
        var options = new QueryOptions {
            OnIterationStart = (history, _) => {
                history.AddUserMessage("STEER 输入");
                return Task.CompletedTask;
            }
        };

        await foreach (var _ in engine.QueryAsync("hello", new MessageList(), options).ConfigureAwait(false)) {
        }

        service.HistoryCounts.Should().ContainSingle().Which.Should().Be(2, "当前用户消息 + 步首注入");
    }

    [Fact]
    public async Task HasPendingIterationInput_ContinuesInsteadOfCompleting() {
        var (engine, service) = CreateEngine();
        var pendingOnce = true;
        var options = new QueryOptions {
            HasPendingIterationInput = () => {
                if (!pendingOnce)
                    return false;
                pendingOnce = false;
                return true;
            }
        };

        var chunks = new List<QueryStreamChunk>();
        await foreach (var chunk in engine.QueryAsync("hello", new MessageList(), options).ConfigureAwait(false)) {
            chunks.Add(chunk);
        }

        service.Calls.Should().Be(2, "待注入输入存在时不得收尾，应继续一轮");
        chunks.Should().Contain(c => c.Type == AgentStreamChunkType.Content && c.Content == "第1次");
        chunks.Should().Contain(c => c.Type == AgentStreamChunkType.Content && c.Content == "第2次");
        chunks.Should().Contain(c => c.Type == AgentStreamChunkType.Complete);
    }
}
