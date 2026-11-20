namespace Core.Tests.Query.Execution;

/// <summary>
/// TASK103 — 聊天执行设置透传到 QueryEngine：覆盖项生效，model 覆盖不丢失延迟工具等字段。
/// </summary>
public sealed class QueryExecutionOverridesTests {
    private sealed class CapturingQueryService : IQueryService {
        public ChatOptions? LastSettings { get; private set; }

        public Task<IReadOnlyList<ApiMessage>> GetApiMessageContentsAsync(
            MessageList chatHistory, ChatOptions? executionSettings = null, IChatClient? kernel = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("测试只走流式接口");

        public async IAsyncEnumerable<StreamEvent> GetStreamEventContentsAsync(
            MessageList chatHistory, ChatOptions? executionSettings = null, IChatClient? kernel = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default) {
            LastSettings = executionSettings;
            await Task.Yield();
            yield return new StreamEvent { Content = "ok" };
        }
    }

    private static (QueryEngine Engine, CapturingQueryService Service) CreateEngine() {
        var service = new CapturingQueryService();
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

    private static async Task RunAsync(QueryEngine engine, QueryOptions options) {
        await foreach (var _ in engine.QueryAsync("hi", new MessageList(), options).ConfigureAwait(false)) {
        }
    }

    [Fact]
    public async Task ExecutionOverrides_FlowIntoChatOptions() {
        var (engine, service) = CreateEngine();
        DeferredToolInfo[] deferred = [new("mcp_a", "延迟工具")];
        var overrides = new ChatOptions {
            Temperature = 0.11f,
            MaxTokens = 1234,
            TopP = 0.5f,
            ToolChoice = ToolChoice.AutoInvoke,
            PromptCacheKey = "cache-key",
            ThinkingEnabled = true,
            DeferredTools = deferred
        };

        await RunAsync(engine, new QueryOptions { ExecutionOverrides = overrides }).ConfigureAwait(true);

        if (service.LastSettings is not { } settings)
            throw new InvalidOperationException("查询引擎未收到执行设置");
        settings.Temperature.Should().Be(0.11f);
        settings.MaxTokens.Should().Be(1234);
        settings.TopP.Should().Be(0.5f);
        settings.PromptCacheKey.Should().Be("cache-key");
        settings.ThinkingEnabled.Should().BeTrue();
        settings.DeferredTools.Should().BeSameAs(deferred);
    }

    [Fact]
    public async Task ModelOverride_KeepsOverridesFields() {
        var (engine, service) = CreateEngine();
        var overrides = new ChatOptions {
            Temperature = 0.3f,
            ToolChoice = ToolChoice.AutoInvoke,
            PromptCacheKey = "cache-key",
            DeferredTools = [new DeferredToolInfo("mcp_b")]
        };

        await RunAsync(engine, new QueryOptions {
            ExecutionOverrides = overrides,
            ModelId = "gpt-x"
        }).ConfigureAwait(true);

        if (service.LastSettings is not { } settings)
            throw new InvalidOperationException("查询引擎未收到执行设置");
        settings.Temperature.Should().Be(0.3f);
        settings.PromptCacheKey.Should().Be("cache-key");
        settings.DeferredTools.Should().ContainSingle(t => t.Name == "mcp_b");
        settings.FastMode.Should().BeTrue();
        settings.FastModelId.Should().Be("gpt-x");
        settings.ExtensionData["model"].GetString().Should().Be("gpt-x");
    }
}
