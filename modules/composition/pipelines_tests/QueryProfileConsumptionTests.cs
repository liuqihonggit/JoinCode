namespace Pipelines.Tests;

/// <summary>通过生产管道注册与真实查询引擎验证 profile 的运行期消费。</summary>
public sealed class QueryProfileConsumptionTests {
    private const string Consumer = "query-pipeline";
    private const string RateLimitNode = "query-rate-limit-mw";

    /// <summary>同一引擎读到切换后的限额，拒绝的查询不进入模型；排除节点后核心仍可运行。</summary>
    [Fact]
    public async Task QueryAsync_UsesProfileLimitBeforeAndAfterSwitch() {
        var model = new CountingQueryService();
        await using var provider = CreateProvider(model, CreateBaseProfile(RateLimit(1), Node("crash-snapshot-mw", 50)),
            CreateChildProfile("higher", [new(Consumer, RateLimitNode, null, RateConfig(2))], []),
            CreateChildProfile("unlimited", [], [$"{Consumer}:{RateLimitNode}", $"{Consumer}:crash-snapshot-mw"]));
        var engine = provider.GetRequiredService<IQueryEngine>();
        var profiles = provider.GetRequiredService<IProfileManager>();

        await AssertCompletedAsync(engine);
        await Assert.ThrowsAsync<RateLimitExceededException>(() => DrainAsync(engine));
        model.Calls.Should().Be(1);

        await profiles.SwitchAsync("higher");
        await AssertCompletedAsync(engine);
        await AssertCompletedAsync(engine);
        await Assert.ThrowsAsync<RateLimitExceededException>(() => DrainAsync(engine));
        model.Calls.Should().Be(3);

        await profiles.SwitchAsync("unlimited");
        await AssertCompletedAsync(engine);
        await AssertCompletedAsync(engine);
        model.Calls.Should().Be(5);
    }

    /// <summary>停止 hook 回灌继续执行核心迭代，不重复扣查询级限额。</summary>
    [Fact]
    public async Task QueryAsync_ProfileStopHookCanResumeTheCoreWithinOneQuery() {
        var model = new CountingQueryService();
        var stop = new Mock<IQueryStopHookManager>();
        stop.SetupSequence(m => m.ExecuteStopHooksAsync("profile-session", "query-complete", It.IsAny<CancellationToken>()))
            .ReturnsAsync(StopHookResult.Block("请继续收尾"))
            .ReturnsAsync(StopHookResult.Stop());
        await using var provider = CreateProvider(model, CreateBaseProfile(RateLimit(1), Node("stop-hook-mw", 800)),
            stop: stop.Object, config: new QueryEngineConfig { MaxStopHookReplyRounds = 1 });
        var engine = provider.GetRequiredService<IQueryEngine>();
        var history = new MessageList();
        var chunks = await DrainAsync(engine, history, new QueryOptions { SessionId = "profile-session" });

        chunks.Should().ContainSingle(chunk => chunk.Type == AgentStreamChunkType.Complete);
        history.Should().Contain(message => message.Content == "请继续收尾");
        model.Calls.Should().Be(2);
        stop.Verify(m => m.ExecuteStopHooksAsync("profile-session", "query-complete", It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    /// <summary>切换装配不改变工具调用上限：一次工具执行后终止，不再向模型发请求。</summary>
    [Fact]
    public async Task QueryAsync_ProfilePipelinePreservesToolCallLimit() {
        const string toolName = "test_lookup";
        var model = new CountingQueryService(new StreamEvent {
            Metadata = new Dictionary<string, System.Text.Json.JsonElement> {
                ["AllToolCalls"] = ToolCallEntry.ToToolCallsJson([new() { Id = "call-1", Name = toolName }])
            }
        });
        var tools = new Mock<IToolRegistry>();
        tools.Setup(t => t.ExecuteToolAsync(toolName, It.IsAny<Dictionary<string, System.Text.Json.JsonElement>>(),
                It.IsAny<CancellationToken>(), null))
            .ReturnsAsync(new ToolResult { Content = [] });
        await using var provider = CreateProvider(model, CreateBaseProfile(RateLimit(1)),
            config: new QueryEngineConfig { MaxToolCallIterations = 1 }, tools: tools.Object);

        var chunks = await DrainAsync(provider.GetRequiredService<IQueryEngine>());

        chunks.Should().ContainSingle(chunk => chunk.Type == AgentStreamChunkType.ToolCallEnd && chunk.ToolName == toolName);
        chunks.Should().ContainSingle(chunk => chunk.Type == AgentStreamChunkType.Error && chunk.StopReason == "error_max_turns");
        model.Calls.Should().Be(1);
        tools.Verify(t => t.ExecuteToolAsync(toolName, It.IsAny<Dictionary<string, System.Text.Json.JsonElement>>(),
            It.IsAny<CancellationToken>(), null), Times.Once);
    }

    /// <summary>生产配置保留原先只通过 DI 进入运行链的两个节点；它们都有实例工厂。</summary>
    [Fact]
    public void BaseProfile_DeclaresBudgetGuardAndMessageDump() {
        var profile = new NodeProfileResolver(NodeProfileLoader.LoadFromResources()).Resolve("base");
        var nodes = profile.Consumers[Consumer].Nodes.Select(node => node.Id);
        nodes.Should().Contain("pre-request-budget-guard-mw").And.Contain("message-dump-mw");
        var factories = DataDrivenPipelineComposition.BuildNodeFactories().ToDictionary();
        factories.Should().ContainKeys($"{Consumer}:pre-request-budget-guard-mw", $"{Consumer}:message-dump-mw");
    }

    /// <summary>注册生产管道，并仅替换外部模型、存储和 profile 数据。</summary>
    private static ServiceProvider CreateProvider(CountingQueryService model, NodeProfile initial,
        NodeProfile? higher = null, NodeProfile? unlimited = null, IQueryStopHookManager? stop = null,
        QueryEngineConfig? config = null, IToolRegistry? tools = null) {
        var profiles = new[] { initial, higher, unlimited }.OfType<NodeProfile>();
        var resolver = new NodeProfileResolver(profiles.ToFrozenDictionary(p => p.Name, StringComparer.Ordinal));
        var kernel = new Mock<IChatClient>();
        kernel.Setup(client => client.GetChatCompletionService()).Returns(model);

        var services = new ServiceCollection();
        services.AddDataDrivenPipelines(initial.Name);
        services.AddSingleton<IProfileManager>(new ProfileManager(resolver, resolver.Resolve(initial.Name)));
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(kernel.Object);
        services.AddSingleton(tools ?? Mock.Of<IToolRegistry>());
        services.AddSingleton(Mock.Of<ICrashSnapshotStore>());
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(config ?? new QueryEngineConfig()));
        services.AddSingleton<IQueryEngine, Core.Query.QueryEngine>();
        services.AddSingleton(_ => new Core.Query.StopHookMiddleware(stop));
        return services.BuildServiceProvider();
    }

    /// <summary>创建只含查询消费点的最小 base profile。</summary>
    private static NodeProfile CreateBaseProfile(params NodeEntryConfig[] nodes) => new("base", null,
        new[] { new ConsumerConfig(Consumer, nodes) }.ToFrozenDictionary(c => c.ConsumerId, StringComparer.Ordinal),
        FrozenSet<string>.Empty, [], FrozenDictionary<string, ConsumerConfig>.Empty);

    /// <summary>创建可覆盖参数或排除节点的子 profile。</summary>
    private static NodeProfile CreateChildProfile(string name, NodeProfileOverride[] overrides, string[] excludes) => new(
        name, "base", FrozenDictionary<string, ConsumerConfig>.Empty, excludes.ToFrozenSet(StringComparer.Ordinal),
        overrides, FrozenDictionary<string, ConsumerConfig>.Empty);

    /// <summary>限流窗口足够长，不依赖等待或精确计时。</summary>
    private static FrozenDictionary<string, string> RateConfig(int max) => new Dictionary<string, string>(StringComparer.Ordinal) {
        ["max-requests"] = max.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["window-seconds"] = "3600"
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>创建一个限流节点。</summary>
    private static NodeEntryConfig RateLimit(int max) => new(RateLimitNode, 100, null, RateConfig(max));

    /// <summary>创建一个不带参数的节点。</summary>
    private static NodeEntryConfig Node(string id, int priority) => new(id, priority, null, FrozenDictionary<string, string>.Empty);

    /// <summary>验证核心查询的内容和完成事件都到达调用方。</summary>
    private static async Task AssertCompletedAsync(IQueryEngine engine) {
        var chunks = await DrainAsync(engine);
        chunks.Should().ContainSingle(chunk => chunk.Type == AgentStreamChunkType.Content && chunk.Content == "完成");
        chunks.Should().ContainSingle(chunk => chunk.Type == AgentStreamChunkType.Complete);
    }

    /// <summary>完整消费流，避免把仅创建枚举器误判为运行了查询。</summary>
    private static async Task<List<QueryStreamChunk>> DrainAsync(IQueryEngine engine, MessageList? history = null,
        QueryOptions? options = null) {
        var chunks = new List<QueryStreamChunk>();
        await foreach (var chunk in engine.QueryAsync("hello", history ?? new MessageList(), options)) {
            chunks.Add(chunk);
        }
        return chunks;
    }

    /// <summary>不访问网络的模型，记录实际进入模型的次数。</summary>
    private sealed class CountingQueryService(StreamEvent? response = null) : IQueryService {
        public int Calls { get; private set; }

        /// <inheritdoc />
        public Task<IReadOnlyList<ApiMessage>> GetApiMessageContentsAsync(MessageList chatHistory,
            ChatOptions? executionSettings = null, IChatClient? kernel = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("测试只走流式查询");

        /// <inheritdoc />
        public async IAsyncEnumerable<StreamEvent> GetStreamEventContentsAsync(MessageList chatHistory,
            ChatOptions? executionSettings = null, IChatClient? kernel = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default) {
            Calls++;
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            yield return response ?? new StreamEvent { Content = "完成" };
        }
    }
}
