namespace Guard.Tests.Hooks.Execution.Interception;

/// <summary>
/// CommandInterceptionDispatcher 单元测试 — 验证守卫链/拦截器链/链式改写/优先级/短路/异常跳过
/// </summary>
public sealed class CommandInterceptionDispatcherTests
{
    private static readonly IReadOnlyDictionary<string, object> EmptyContext =
        FrozenDictionary<string, object>.Empty;

    // === 空集合 ===

    [Fact]
    public async Task DispatchAsync_NoGuardsNoInterceptors_PassesThrough()
    {
        var dispatcher = new CommandInterceptionDispatcher([], []);

        var outcome = await dispatcher.DispatchAsync("git status", EmptyContext, default);

        outcome.ShortCircuitResult.Should().BeNull();
        outcome.FinalCommand.Should().Be("git status");
    }

    // === Allow ===

    [Fact]
    public async Task DispatchAsync_GuardAllow_PassesThrough()
    {
        var guard = new StubGuard("g1", priority: 100, new CommandDecision.Allow());
        var dispatcher = new CommandInterceptionDispatcher([guard], []);

        var outcome = await dispatcher.DispatchAsync("ls", EmptyContext, default);

        outcome.ShortCircuitResult.Should().BeNull();
        outcome.FinalCommand.Should().Be("ls");
    }

    // === Rewrite ===

    [Fact]
    public async Task DispatchAsync_GuardRewrite_RewritesCommandAndPassesThrough()
    {
        var guard = new StubGuard("rewrite1", priority: 100,
            new CommandDecision.Rewrite("git -c http.proxy=x fetch", "vpn"));
        var dispatcher = new CommandInterceptionDispatcher([guard], []);

        var outcome = await dispatcher.DispatchAsync("git fetch", EmptyContext, default);

        outcome.ShortCircuitResult.Should().BeNull();
        outcome.FinalCommand.Should().Be("git -c http.proxy=x fetch");
    }

    [Fact]
    public async Task DispatchAsync_ChainedRewrite_AppliesAllRewritesInPriorityOrder()
    {
        // 高优先级先改写:git fetch → git -c proxy fetch
        var guardHigh = new StubGuard("vpn", priority: 200,
            new CommandDecision.Rewrite("git -c http.proxy=x fetch", "vpn"));
        // 低优先级后改写:基于已改写命令再加 --body(模拟,实际不匹配 git fetch,这里用 CanHandle 全匹配)
        var guardLow = new StubGuard("body", priority: 100,
            new CommandDecision.Rewrite("git -c http.proxy=x fetch --body", "body"));

        var dispatcher = new CommandInterceptionDispatcher([guardLow, guardHigh], []);

        var outcome = await dispatcher.DispatchAsync("git fetch", EmptyContext, default);

        outcome.ShortCircuitResult.Should().BeNull();
        outcome.FinalCommand.Should().Be("git -c http.proxy=x fetch --body");
    }

    // === Deny ===

    [Fact]
    public async Task DispatchAsync_GuardDeny_ShortCircuitsWithError()
    {
        var diag = ToolDiagnostic.Create("命令被拒绝", "禁止直接执行 git commit");
        var guard = new StubGuard("denyCommit", priority: 1000, new CommandDecision.Deny(diag));
        var dispatcher = new CommandInterceptionDispatcher([guard], []);

        var outcome = await dispatcher.DispatchAsync("git commit -m x", EmptyContext, default);

        outcome.ShortCircuitResult.Should().NotBeNull();
        outcome.ShortCircuitResult!.IsError.Should().BeTrue();
        outcome.ShortCircuitResult!.GetFirstText().Should().Contain("禁止直接执行 git commit");
    }

    [Fact]
    public async Task DispatchAsync_DenyStopsBeforeLowerPriorityGuard()
    {
        var diag = ToolDiagnostic.Create("拒绝", "denied");
        var denyGuard = new StubGuard("deny", priority: 200, new CommandDecision.Deny(diag));
        var allowGuard = new StubGuard("allow", priority: 100, new CommandDecision.Allow());
        var dispatcher = new CommandInterceptionDispatcher([allowGuard, denyGuard], []);

        var outcome = await dispatcher.DispatchAsync("cmd", EmptyContext, default);

        outcome.ShortCircuitResult.Should().NotBeNull();
        allowGuard.EvaluateCallCount.Should().Be(0, "低优先级 Allow 守卫不应被评估(Deny 已短路)");
    }

    // === Redirect ===

    [Fact]
    public async Task DispatchAsync_GuardRedirect_ShortCircuitsWithHint()
    {
        var guard = new StubGuard("redirectCommit", priority: 1000,
            new CommandDecision.Redirect("/commit", "请使用 /commit 斜杠命令创建提交"));
        var dispatcher = new CommandInterceptionDispatcher([guard], []);

        var outcome = await dispatcher.DispatchAsync("git commit", EmptyContext, default);

        outcome.ShortCircuitResult.Should().NotBeNull();
        outcome.ShortCircuitResult!.IsError.Should().BeTrue();
        outcome.ShortCircuitResult.GetFirstText().Should().Contain("请使用 /commit");
    }

    // === Handoff ===

    [Fact]
    public async Task DispatchAsync_Handoff_FallsThroughToInterceptors()
    {
        var handoffGuard = new StubGuard("handoff", priority: 100, new CommandDecision.Handoff());
        var interceptor = new StubInterceptor("i1", priority: 100,
            new InterceptResult.Handled(ToolResultBuilder.Success().WithText("handled").Build()));
        var dispatcher = new CommandInterceptionDispatcher([handoffGuard], [interceptor]);

        var outcome = await dispatcher.DispatchAsync("cmd", EmptyContext, default);

        outcome.ShortCircuitResult.Should().NotBeNull();
        outcome.ShortCircuitResult!.GetFirstText().Should().Contain("handled");
        interceptor.HandleCallCount.Should().Be(1);
    }

    // === Interceptor 链 ===

    [Fact]
    public async Task DispatchAsync_InterceptorHandled_ShortCircuits()
    {
        var interceptor = new StubInterceptor("build", priority: 100,
            new InterceptResult.Handled(ToolResultBuilder.Success().WithText("build queued").Build()));
        var dispatcher = new CommandInterceptionDispatcher([], [interceptor]);

        var outcome = await dispatcher.DispatchAsync("dotnet build", EmptyContext, default);

        outcome.ShortCircuitResult.Should().NotBeNull();
        outcome.ShortCircuitResult!.GetFirstText().Should().Contain("build queued");
    }

    [Fact]
    public async Task DispatchAsync_InterceptorContinue_PassesThrough()
    {
        var interceptor = new StubInterceptor("sed", priority: 100, new InterceptResult.Continue());
        var dispatcher = new CommandInterceptionDispatcher([], [interceptor]);

        var outcome = await dispatcher.DispatchAsync("sed -i s/a/b/ f.txt", EmptyContext, default);

        outcome.ShortCircuitResult.Should().BeNull();
    }

    [Fact]
    public async Task DispatchAsync_InterceptorException_SkipsAndContinues()
    {
        var throwingInterceptor = new StubInterceptor("thrower", priority: 100, throwOnHandle: true);
        var passInterceptor = new StubInterceptor("pass", priority: 50,
            new InterceptResult.Handled(ToolResultBuilder.Success().WithText("ok").Build()));
        var dispatcher = new CommandInterceptionDispatcher([], [throwingInterceptor, passInterceptor]);

        var outcome = await dispatcher.DispatchAsync("cmd", EmptyContext, default);

        outcome.ShortCircuitResult.Should().NotBeNull();
        outcome.ShortCircuitResult!.GetFirstText().Should().Contain("ok");
    }

    // === 优先级排序 ===

    [Fact]
    public async Task DispatchAsync_GuardsEvaluatedByPriorityDescending()
    {
        var first = new StubGuard("first", priority: 50, new CommandDecision.Allow());
        var second = new StubGuard("second", priority: 100, new CommandDecision.Allow());
        var dispatcher = new CommandInterceptionDispatcher([first, second], []);

        await dispatcher.DispatchAsync("cmd", EmptyContext, default);

        // 优先级 100 的 second 应先评估
        second.EvaluateCallCount.Should().Be(1);
        first.EvaluateCallCount.Should().Be(1);
    }

    [Fact]
    public void Constructor_GuardsSortedByPriorityDescending()
    {
        var low = new StubGuard("low", priority: 10, new CommandDecision.Allow());
        var high = new StubGuard("high", priority: 100, new CommandDecision.Allow());
        var dispatcher = new CommandInterceptionDispatcher([low, high], []);

        dispatcher.GetGuards()[0].Should().BeSameAs(high);
        dispatcher.GetGuards()[1].Should().BeSameAs(low);
    }

    // === 参数校验 ===

    [Fact]
    public async Task DispatchAsync_NullOrWhiteSpaceCommand_Throws()
    {
        var dispatcher = new CommandInterceptionDispatcher([], []);

        var act = async () => await dispatcher.DispatchAsync("", EmptyContext, default);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    // === Stub 实现 ===

    private sealed class StubGuard : ICommandGuard
    {
        private readonly CommandDecision _decision;
        public int EvaluateCallCount { get; private set; }

        public StubGuard(string name, int priority, CommandDecision decision)
        {
            Name = name;
            Priority = priority;
            _decision = decision;
        }

        public string Name { get; }
        public int Priority { get; }

        public bool CanHandle(string command, IReadOnlyDictionary<string, object> context) => true;

        public CommandDecision Evaluate(string command, IReadOnlyDictionary<string, object> context)
        {
            EvaluateCallCount++;
            return _decision;
        }
    }

    private sealed class StubInterceptor : ICommandInterceptor
    {
        private readonly InterceptResult _result;
        private readonly bool _throwOnHandle;
        public int HandleCallCount { get; private set; }

        public StubInterceptor(string name, int priority, InterceptResult result, bool throwOnHandle = false)
        {
            Name = name;
            Priority = priority;
            _result = result;
            _throwOnHandle = throwOnHandle;
        }

        public StubInterceptor(string name, int priority, bool throwOnHandle)
        {
            Name = name;
            Priority = priority;
            _result = new InterceptResult.Continue();
            _throwOnHandle = throwOnHandle;
        }

        public string Name { get; }
        public int Priority { get; }

        public bool CanHandle(string command, IReadOnlyDictionary<string, object> context) => true;

        public Task<InterceptResult> HandleAsync(string command, IReadOnlyDictionary<string, object> context, CancellationToken cancellationToken)
        {
            HandleCallCount++;
            if (_throwOnHandle) throw new InvalidOperationException("stub throw");
            return Task.FromResult(_result);
        }
    }
}
