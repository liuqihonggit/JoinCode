namespace Host.Tests.ChatCommands;

public sealed class BtwCommandTests {
    [Fact]
    public void Name_Should_Be_btw() {
        var cmd = new BtwCommand();
        cmd.Name.Should().Be("btw");
    }

    [Fact]
    public void Description_Should_Not_Be_Empty() {
        var cmd = new BtwCommand();
        cmd.Description.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Usage_Should_Start_With_Slash() {
        var cmd = new BtwCommand();
        cmd.Usage.Should().StartWith("/btw");
    }

    [Fact]
    public void IsHidden_Should_Be_False() {
        var cmd = new BtwCommand();
        cmd.IsHidden.Should().BeFalse();
    }

    [Fact]
    public void Aliases_Should_Be_Empty() {
        var cmd = new BtwCommand();
        cmd.Aliases.Should().BeEmpty();
    }

    [Fact]
    public void ArgumentHint_Should_Not_Be_Empty() {
        var cmd = new BtwCommand();
        cmd.ArgumentHint.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Execute_WithEmptyArgs_Should_Show_Usage() {
        var cmd = new BtwCommand();
        var context = new ChatCommandContext {
            Arguments = "",
            CancellationToken = CancellationToken.None,
            Services = new CommandServiceProvider(new CommandServices {
                ChatService = Mock.Of<IChatService>(),
                CodeService = Mock.Of<ICodeService>(),
                PlanService = Mock.Of<IPlanService>(),
                FileSystem = TestFileSystem.Current,
            }),
        };

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WithWhitespaceArgs_Should_Show_Usage() {
        var cmd = new BtwCommand();
        var context = new ChatCommandContext {
            Arguments = "   ",
            CancellationToken = CancellationToken.None,
            Services = new CommandServiceProvider(new CommandServices {
                ChatService = Mock.Of<IChatService>(),
                CodeService = Mock.Of<ICodeService>(),
                PlanService = Mock.Of<IPlanService>(),
                FileSystem = TestFileSystem.Current,
            }),
        };

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WithQuestion_Should_InvokeForkQueryEngine() {
        var engine = new CaptureQueryEngine("侧边回答文本");
        var services = new ServiceCollection();
        services.AddSingleton<IQueryEngine>(engine);
        var cmd = new BtwCommand();
        var context = new ChatCommandContext {
            Arguments = "test question",
            CancellationToken = CancellationToken.None,
            Services = new CommandServiceProvider(new CommandServices {
                ChatService = Mock.Of<IChatService>(),
                CodeService = Mock.Of<ICodeService>(),
                PlanService = Mock.Of<IPlanService>(),
                FileSystem = TestFileSystem.Current,
                ServiceProvider = services.BuildServiceProvider(),
            }),
        };

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
        engine.QueryCallCount.Should().Be(1, "fork 路径走 IQueryEngine（对齐基准 runForkedAgent），不再走 ChatService.SendMessageAsync");
        engine.CapturedPrompt.Should().Contain("test question");
    }

    /// <summary>fork 异常路径 — QueryEngine 抛异常时 HandleError 吞掉，命令仍 Continue 不中断主对话流。</summary>
    [Fact]
    public async Task Execute_WhenQueryEngineThrows_Should_Return_Continue() {
        var cmd = new BtwCommand();
        var engine = new CaptureQueryEngine(throwOnQuery: true);
        var services = new ServiceCollection();
        services.AddSingleton<IQueryEngine>(engine);
        var context = new ChatCommandContext {
            Arguments = "test question",
            CancellationToken = CancellationToken.None,
            Services = new CommandServiceProvider(new CommandServices {
                ChatService = Mock.Of<IChatService>(),
                CodeService = Mock.Of<ICodeService>(),
                PlanService = Mock.Of<IPlanService>(),
                FileSystem = TestFileSystem.Current,
                ServiceProvider = services.BuildServiceProvider(),
            }),
        };

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
    }

    /// <summary>捕获型 QueryEngine 假体 — fork 调用计数与 prompt 捕获（IQueryEngine 最小面）。</summary>
    private sealed class CaptureQueryEngine(string? fixedResponse = null, bool throwOnQuery = false) : IQueryEngine {
        public int QueryCallCount { get; private set; }
        public string CapturedPrompt { get; private set; } = string.Empty;

        public Task<string> ExecuteQueryAsync(string query, CancellationToken cancellationToken = default)
            => Task.FromResult(fixedResponse ?? string.Empty);

        public async IAsyncEnumerable<QueryStreamChunk> QueryAsync(
            string userInput, MessageList chatHistory,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
            QueryCallCount++;
            CapturedPrompt = userInput;
            if (throwOnQuery) {
                throw new InvalidOperationException("fork query error");
            }

            await Task.Yield();
            yield return new QueryStreamChunk { Type = AgentStreamChunkType.Content, Content = fixedResponse ?? string.Empty };
        }

        public IAsyncEnumerable<QueryStreamChunk> QueryAsync(
            string userInput, MessageList chatHistory, QueryOptions? options, CancellationToken cancellationToken = default)
            => QueryAsync(userInput, chatHistory, cancellationToken);

        public JoinCode.Abstractions.LLM.IQueryService GetChatCompletionService()
            => throw new NotSupportedException("测试假体不提供查询服务");

        public JoinCode.Abstractions.LLM.IChatClient GetKernel()
            => throw new NotSupportedException("测试假体不提供 kernel");
    }
}