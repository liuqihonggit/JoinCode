namespace Composition.Tests.Commands;

/// <summary>
/// /btw 隔离 fork 测试（对齐 TS runSideQuestion→runForkedAgent 语义）—
/// 主历史副本做前缀 + 独立 AgentBase 单轮执行（MaxIterations=1）+ 不写回主上下文
///（ContextManager 不注入天然隔离）；修复背景：原 ChatService 共享管道受主对话会话态
/// 影响偶发空响应。IQueryEngine 假体直证 fork 请求消息形态。
/// </summary>
public sealed class BtwCommandTests {
    /// <summary>假 IQueryEngine — 捕获收到的 chatHistory，返回固定单 content chunk。</summary>
    private sealed class CaptureQueryEngine : IQueryEngine {
        public MessageList? CapturedHistory { get; private set; }
        public string CapturedPrompt { get; private set; } = string.Empty;

        public Task<string> ExecuteQueryAsync(string query, CancellationToken cancellationToken = default)
            => Task.FromResult("侧边回答");

        public async IAsyncEnumerable<QueryStreamChunk> QueryAsync(
            string userInput, MessageList chatHistory,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
            CapturedPrompt = userInput;
            CapturedHistory = chatHistory;
            await Task.Yield();
            yield return new QueryStreamChunk { Type = AgentStreamChunkType.Content, Content = "这是侧边回答。" };
        }

        public IAsyncEnumerable<QueryStreamChunk> QueryAsync(
            string userInput, MessageList chatHistory, QueryOptions? options, CancellationToken cancellationToken = default)
            => QueryAsync(userInput, chatHistory, cancellationToken);

        public JoinCode.Abstractions.LLM.IQueryService GetChatCompletionService()
            => throw new NotSupportedException("测试假体不提供查询服务");

        public JoinCode.Abstractions.LLM.IChatClient GetKernel()
            => throw new NotSupportedException("测试假体不提供 kernel");
    }

    private static ChatCommandContext CreateContext(IQueryEngine queryEngine, string arguments = "你好") {
        var services = new ServiceCollection();
        services.AddSingleton(queryEngine);
        var commandServices = new CommandServices {
            ChatService = Mock.Of<IChatService>(),
            CodeService = Mock.Of<ICodeService>(),
            PlanService = Mock.Of<IPlanService>(),
            FileSystem = TestFileSystem.Current,
            ServiceProvider = services.BuildServiceProvider(),
        };
        return new ChatCommandContext {
            Arguments = arguments,
            CancellationToken = CancellationToken.None,
            Services = new CommandServiceProvider(commandServices),
        };
    }

    /// <summary>fork 调用 — 返回 QueryEngine 内容 chunk 聚合文本。</summary>
    [Fact]
    public async Task RunSideQuestion_AggregatesContentChunks() {
        var engine = new CaptureQueryEngine();
        var context = CreateContext(engine);

        var response = await BtwCommand.RunSideQuestionAsync(context, "你好");

        response.Should().Be("这是侧边回答。");
    }

    /// <summary>fork 消息形态 — 历史含 system-reminder 包裹的问题（对齐 TS wrappedQuestion）。</summary>
    [Fact]
    public async Task RunSideQuestion_WrapsQuestionWithReminder() {
        var engine = new CaptureQueryEngine();
        var context = CreateContext(engine);

        await BtwCommand.RunSideQuestionAsync(context, "什么是 SOLID?");

        engine.CapturedHistory.Should().NotBeNull();
        Assert.NotNull(engine.CapturedHistory);
        var history = engine.CapturedHistory;
        history.Count.Should().Be(1);
        var last = history[history.Count - 1];
        last.Role.Should().Be(MessageRole.User);
        last.Content.Should().Contain("侧边问题");
        last.Content.Should().Contain("什么是 SOLID?");
        last.Content.Should().Contain("没有任何可用工具");
    }

    /// <summary>空参 — 用法提示不调引擎（输出捕获经 Console.SetOut，不经 TerminalHelper 门面 —
    /// 测试项目无 cli 域引用，SetOut 与命令侧 TerminalHelper.WriteLine 底层同为 Console.Out）。</summary>
    [Fact]
    public async Task Execute_EmptyQuestion_ShowsUsageWithoutEngineCall() {
        var engine = new CaptureQueryEngine();
        var context = CreateContext(engine, arguments: "");
        var output = new System.Text.StringBuilder();
        var originalOut = System.Console.Out;
        await using var writer = new StringWriter(output);
        System.Console.SetOut(writer);
        try {
            var cmd = new BtwCommand();
            await cmd.ExecuteAsync(context);
        } finally {
            System.Console.SetOut(originalOut);
        }

        engine.CapturedHistory.Should().BeNull("空参不应触发 fork 调用");
        output.ToString().Should().Contain("用法: /btw");
    }
}
