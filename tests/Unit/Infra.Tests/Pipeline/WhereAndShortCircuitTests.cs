namespace Infrastructure.Pipeline.Tests;

/// <summary>
/// Where 条件修饰 + ShortCircuit 短路标记 单元测试
/// </summary>
public sealed class WhereAndShortCircuitTests
{
    // === Where — 同步条件 ===

    [Fact]
    public async Task Where_PredicateTrue_ExecutesMiddleware()
    {
        var pipeline = new PipelineBuilder<TestContext>()
            .Use(new TrackingMiddleware("conditional")).Where(ctx => ctx.Enabled)
            .Build();

        var ctx = new TestContext { Enabled = true };
        await pipeline.ExecuteAsync(ctx, CancellationToken.None).ConfigureAwait(true);

        ctx.ExecutionLog.Should().Equal("conditional");
    }

    [Fact]
    public async Task Where_PredicateFalse_SkipsMiddleware()
    {
        var pipeline = new PipelineBuilder<TestContext>()
            .Use(new TrackingMiddleware("A"))
            .Use(new TrackingMiddleware("conditional")).Where(ctx => ctx.Enabled)
            .Use(new TrackingMiddleware("B"))
            .Build();

        var ctx = new TestContext { Enabled = false };
        await pipeline.ExecuteAsync(ctx, CancellationToken.None).ConfigureAwait(true);

        ctx.ExecutionLog.Should().Equal("A", "B");
    }

    [Fact]
    public async Task Where_PredicateTrue_ExecutesAndContinuesPipeline()
    {
        var pipeline = new PipelineBuilder<TestContext>()
            .Use(new TrackingMiddleware("conditional")).Where(ctx => ctx.Enabled)
            .Use(new TrackingMiddleware("after"))
            .Build();

        var ctx = new TestContext { Enabled = true };
        await pipeline.ExecuteAsync(ctx, CancellationToken.None).ConfigureAwait(true);

        ctx.ExecutionLog.Should().Equal("conditional", "after");
    }

    [Fact]
    public async Task Where_MultipleConditions_IndependentEvaluation()
    {
        var pipeline = new PipelineBuilder<TestContext>()
            .Use(new TrackingMiddleware("cond1")).Where(ctx => ctx.Enabled)
            .Use(new TrackingMiddleware("cond2")).Where(ctx => ctx.Flag)
            .Use(new TrackingMiddleware("always"))
            .Build();

        var ctx = new TestContext { Enabled = true, Flag = false };
        await pipeline.ExecuteAsync(ctx, CancellationToken.None).ConfigureAwait(true);

        ctx.ExecutionLog.Should().Equal("cond1", "always");
    }

    [Fact]
    public async Task Where_WithoutUse_ThrowsInvalidOperationException()
    {
        var builder = new PipelineBuilder<TestContext>();
        var act = () => builder.Where(ctx => ctx.Enabled);
        act.Should().Throw<InvalidOperationException>();
    }

    // === Where — 异步条件 ===

    [Fact]
    public async Task Where_AsyncPredicateTrue_ExecutesMiddleware()
    {
        var pipeline = new PipelineBuilder<TestContext>()
            .Use(new TrackingMiddleware("async-cond")).Where((ctx, ct) => new ValueTask<bool>(ctx.Enabled))
            .Build();

        var ctx = new TestContext { Enabled = true };
        await pipeline.ExecuteAsync(ctx, CancellationToken.None).ConfigureAwait(true);

        ctx.ExecutionLog.Should().Equal("async-cond");
    }

    [Fact]
    public async Task Where_AsyncPredicateFalse_SkipsMiddleware()
    {
        var pipeline = new PipelineBuilder<TestContext>()
            .Use(new TrackingMiddleware("async-cond")).Where((ctx, ct) => new ValueTask<bool>(ctx.Enabled))
            .Use(new TrackingMiddleware("after"))
            .Build();

        var ctx = new TestContext { Enabled = false };
        await pipeline.ExecuteAsync(ctx, CancellationToken.None).ConfigureAwait(true);

        ctx.ExecutionLog.Should().Equal("after");
    }

    // === Where — 短路行为 ===

    [Fact]
    public async Task Where_ConditionalMiddlewareShortCircuits_SkipsRemaining()
    {
        var pipeline = new PipelineBuilder<TestContext>()
            .Use(new ShortCircuitWithLogMiddleware("cond-short")).Where(ctx => ctx.Enabled)
            .Use(new TrackingMiddleware("should-not-run"))
            .Build();

        var ctx = new TestContext { Enabled = true };
        await pipeline.ExecuteAsync(ctx, CancellationToken.None).ConfigureAwait(true);

        ctx.ExecutionLog.Should().Equal("cond-short");
        ctx.ExecutionLog.Should().NotContain("should-not-run");
    }

    // === ShortCircuit — 管道级短路 ===

    [Fact]
    public async Task WithShortCircuit_ContextShortCircuited_SkipsAllRemaining()
    {
        var pipeline = new PipelineBuilder<ShortCircuitTestContext>()
            .WithShortCircuit(ctx => ctx.IsShortCircuited)
            .Use(new ShortCircuitTriggerMiddleware())
            .Use(new TrackingMiddleware("should-not-run"))
            .Build();

        var ctx = new ShortCircuitTestContext();
        await pipeline.ExecuteAsync(ctx, CancellationToken.None).ConfigureAwait(true);

        ctx.ExecutionLog.Should().Equal("trigger");
        ctx.ExecutionLog.Should().NotContain("should-not-run");
        ctx.IsShortCircuited.Should().BeTrue();
    }

    [Fact]
    public async Task WithShortCircuit_MultipleMiddlewares_StopsAtFirstShortCircuit()
    {
        var pipeline = new PipelineBuilder<ShortCircuitTestContext>()
            .WithShortCircuit(ctx => ctx.IsShortCircuited)
            .Use(new TrackingMiddleware("A"))
            .Use(new ShortCircuitTriggerMiddleware())
            .Use(new TrackingMiddleware("B"))
            .Use(new TrackingMiddleware("C"))
            .Build();

        var ctx = new ShortCircuitTestContext();
        await pipeline.ExecuteAsync(ctx, CancellationToken.None).ConfigureAwait(true);

        ctx.ExecutionLog.Should().Equal("A", "trigger");
    }

    [Fact]
    public async Task WithShortCircuit_NoShortCircuit_ExecutesAll()
    {
        var pipeline = new PipelineBuilder<ShortCircuitTestContext>()
            .WithShortCircuit(ctx => ctx.IsShortCircuited)
            .Use(new TrackingMiddleware("A"))
            .Use(new TrackingMiddleware("B"))
            .Use(new TrackingMiddleware("C"))
            .Build();

        var ctx = new ShortCircuitTestContext();
        await pipeline.ExecuteAsync(ctx, CancellationToken.None).ConfigureAwait(true);

        ctx.ExecutionLog.Should().Equal("A", "B", "C");
        ctx.IsShortCircuited.Should().BeFalse();
    }

    [Fact]
    public async Task WithShortCircuit_PostHookStillExecutes()
    {
        var postHookInvoked = false;
        var pipeline = new PipelineBuilder<ShortCircuitTestContext>()
            .WithShortCircuit(ctx => ctx.IsShortCircuited)
            .WithPostHook((ctx, ct) => { postHookInvoked = true; return Task.CompletedTask; })
            .Use(new ShortCircuitTriggerMiddleware())
            .Use(new TrackingMiddleware("should-not-run"))
            .Build();

        var ctx = new ShortCircuitTestContext();
        await pipeline.ExecuteAsync(ctx, CancellationToken.None).ConfigureAwait(true);

        postHookInvoked.Should().BeTrue();
    }

    [Fact]
    public async Task WithShortCircuit_NullPredicate_MiddlewareNotCallingNext_SkipsRemaining()
    {
        var pipeline = new PipelineBuilder<ShortCircuitTestContext>()
            .Use(new ShortCircuitTriggerMiddleware())
            .Use(new TrackingMiddleware("after"))
            .Build();

        var ctx = new ShortCircuitTestContext();
        await pipeline.ExecuteAsync(ctx, CancellationToken.None).ConfigureAwait(true);

        ctx.ExecutionLog.Should().Equal("trigger");
        ctx.IsShortCircuited.Should().BeTrue();
    }

    // === Where + ShortCircuit 组合 ===

    [Fact]
    public async Task Where_WithShortCircuit_ConditionalMiddlewareTriggersShortCircuit()
    {
        var pipeline = new PipelineBuilder<ShortCircuitTestContext>()
            .WithShortCircuit(ctx => ctx.IsShortCircuited)
            .Use(new ShortCircuitTriggerMiddleware()).Where(ctx => ctx.Enabled)
            .Use(new TrackingMiddleware("should-not-run"))
            .Build();

        var ctx = new ShortCircuitTestContext { Enabled = true };
        await pipeline.ExecuteAsync(ctx, CancellationToken.None).ConfigureAwait(true);

        ctx.ExecutionLog.Should().Equal("trigger");
        ctx.IsShortCircuited.Should().BeTrue();
    }

    [Fact]
    public async Task Where_ConditionFalse_WithShortCircuit_DoesNotAffectPipeline()
    {
        var pipeline = new PipelineBuilder<ShortCircuitTestContext>()
            .WithShortCircuit(ctx => ctx.IsShortCircuited)
            .Use(new ShortCircuitTriggerMiddleware()).Where(ctx => ctx.Enabled)
            .Use(new TrackingMiddleware("after"))
            .Build();

        var ctx = new ShortCircuitTestContext { Enabled = false };
        await pipeline.ExecuteAsync(ctx, CancellationToken.None).ConfigureAwait(true);

        ctx.ExecutionLog.Should().Equal("after");
        ctx.IsShortCircuited.Should().BeFalse();
    }

    // === ShortCircuitableContext 基类 ===

    [Fact]
    public void ShortCircuitableContext_DefaultNotShortCircuited()
    {
        var ctx = new TestShortCircuitableContext();
        ctx.IsShortCircuited.Should().BeFalse();
    }

    [Fact]
    public void ShortCircuitableContext_ShortCircuit_SetsFlag()
    {
        var ctx = new TestShortCircuitableContext();
        ctx.ShortCircuit();
        ctx.IsShortCircuited.Should().BeTrue();
    }

    // === 测试辅助类 ===

    private sealed class TestContext
    {
        public List<string> ExecutionLog { get; } = [];
        public bool Enabled { get; set; }
        public bool Flag { get; set; }
    }

    private sealed class ShortCircuitTestContext : IShortCircuitableContext
    {
        public List<string> ExecutionLog { get; } = [];
        public bool Enabled { get; set; }
        public bool IsShortCircuited { get; private set; }
        public void ShortCircuit() => IsShortCircuited = true;
    }

    private sealed class TestShortCircuitableContext : ShortCircuitableContext;

    private sealed class TrackingMiddleware(string label) : IMiddleware<TestContext>, IMiddleware<ShortCircuitTestContext>
    {
        public ErrorBehavior OnError => ErrorBehavior.Continue;

        public async Task InvokeAsync(TestContext context, MiddlewareDelegate<TestContext> next, CancellationToken ct)
        {
            context.ExecutionLog.Add(label);
            await next(context, ct).ConfigureAwait(true);
        }

        public async Task InvokeAsync(ShortCircuitTestContext context, MiddlewareDelegate<ShortCircuitTestContext> next, CancellationToken ct)
        {
            context.ExecutionLog.Add(label);
            await next(context, ct).ConfigureAwait(true);
        }
    }

    private sealed class ShortCircuitWithLogMiddleware(string label) : IMiddleware<TestContext>
    {
        public ErrorBehavior OnError => ErrorBehavior.Propagate;

        public Task InvokeAsync(TestContext context, MiddlewareDelegate<TestContext> next, CancellationToken ct)
        {
            context.ExecutionLog.Add(label);
            return Task.CompletedTask;
        }
    }

    private sealed class ShortCircuitTriggerMiddleware : IMiddleware<ShortCircuitTestContext>
    {
        public ErrorBehavior OnError => ErrorBehavior.Propagate;

        public Task InvokeAsync(ShortCircuitTestContext context, MiddlewareDelegate<ShortCircuitTestContext> next, CancellationToken ct)
        {
            context.ExecutionLog.Add("trigger");
            context.ShortCircuit();
            return Task.CompletedTask;
        }
    }
}
