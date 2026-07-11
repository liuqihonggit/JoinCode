namespace Infrastructure.Pipeline.Tests;

/// <summary>
/// MiddlewarePipeline 单元测试 — 验证管道构建、注册顺序执行、异常捕获、短路
/// </summary>
public sealed class MiddlewarePipelineTests
{
    // === 管道构建 ===

    [Fact]
    public async Task ExecuteAsync_NoMiddlewares_CompletesSuccessfully()
    {
        var pipeline = new MiddlewarePipeline<TestContext>([]);
        var ctx = new TestContext();

        await pipeline.ExecuteAsync(ctx, CancellationToken.None).ConfigureAwait(true);

        ctx.ExecutionLog.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_SingleMiddleware_ExecutesInRegistrationOrder()
    {
        var pipeline = new MiddlewarePipeline<TestContext>([new TrackingMiddleware("A")]);
        var ctx = new TestContext();

        await pipeline.ExecuteAsync(ctx, CancellationToken.None).ConfigureAwait(true);

        ctx.ExecutionLog.Should().Equal("A");
    }

    [Fact]
    public async Task ExecuteAsync_MultipleMiddlewares_ExecutesInRegistrationOrder()
    {
        var pipeline = new MiddlewarePipeline<TestContext>([
            new TrackingMiddleware("A"),
            new TrackingMiddleware("B"),
            new TrackingMiddleware("C"),
        ]);
        var ctx = new TestContext();

        await pipeline.ExecuteAsync(ctx, CancellationToken.None).ConfigureAwait(true);

        ctx.ExecutionLog.Should().Equal("A", "B", "C");
    }

    // === 异常捕获 ===

    [Fact]
    public async Task ExecuteAsync_OnErrorContinue_CatchesAndContinues()
    {
        var errors = new List<Exception>();
        var pipeline = new MiddlewarePipeline<TestContext>(
            [
                new ThrowingMiddleware("A", ErrorBehavior.Continue),
                new TrackingMiddleware("B"),
            ],
            onError: (_, ex) => errors.Add(ex));
        var ctx = new TestContext();

        await pipeline.ExecuteAsync(ctx, CancellationToken.None).ConfigureAwait(true);

        ctx.ExecutionLog.Should().Equal("B");
        errors.Should().HaveCount(1);
        errors[0].Message.Should().Be("A failed");
    }

    [Fact]
    public async Task ExecuteAsync_OnErrorPropagate_ThrowsAndStops()
    {
        var pipeline = new MiddlewarePipeline<TestContext>(
            [
                new ThrowingMiddleware("A", ErrorBehavior.Propagate),
                new TrackingMiddleware("B"),
            ]);
        var ctx = new TestContext();

        var act = async () => await pipeline.ExecuteAsync(ctx, CancellationToken.None).ConfigureAwait(true);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("A failed").ConfigureAwait(true);
        ctx.ExecutionLog.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_MultipleContinueErrors_CatchesAll()
    {
        var errors = new List<Exception>();
        var pipeline = new MiddlewarePipeline<TestContext>(
            [
                new ThrowingMiddleware("A", ErrorBehavior.Continue),
                new ThrowingMiddleware("B", ErrorBehavior.Continue),
                new TrackingMiddleware("C"),
            ],
            onError: (_, ex) => errors.Add(ex));
        var ctx = new TestContext();

        await pipeline.ExecuteAsync(ctx, CancellationToken.None).ConfigureAwait(true);

        ctx.ExecutionLog.Should().Equal("C");
        errors.Should().HaveCount(2);
    }

    [Fact]
    public async Task ExecuteAsync_OnErrorNull_PropagatesEvenIfContinue()
    {
        var pipeline = new MiddlewarePipeline<TestContext>(
            [
                new ThrowingMiddleware("A", ErrorBehavior.Continue),
                new TrackingMiddleware("B"),
            ],
            onError: null);
        var ctx = new TestContext();

        var act = async () => await pipeline.ExecuteAsync(ctx, CancellationToken.None).ConfigureAwait(true);

        await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(true);
    }

    // === 短路 ===

    [Fact]
    public async Task ExecuteAsync_ShortCircuit_SkipsRemaining()
    {
        var pipeline = new MiddlewarePipeline<TestContext>([
            new ShortCircuitMiddleware(),
            new TrackingMiddleware("should-not-run"),
        ]);
        var ctx = new TestContext();

        await pipeline.ExecuteAsync(ctx, CancellationToken.None).ConfigureAwait(true);

        ctx.ExecutionLog.Should().NotContain("should-not-run");
    }

    // === 上下文传递 ===

    [Fact]
    public async Task ExecuteAsync_ContextSharedAcrossMiddlewares()
    {
        var pipeline = new MiddlewarePipeline<TestContext>([
            new ContextWriterMiddleware("key1", "value1"),
            new ContextWriterMiddleware("key2", "value2"),
            new ContextReaderMiddleware("key1", "key2"),
        ]);
        var ctx = new TestContext();

        await pipeline.ExecuteAsync(ctx, CancellationToken.None).ConfigureAwait(true);

        ctx.ExecutionLog.Should().Contain("key1=value1");
        ctx.ExecutionLog.Should().Contain("key2=value2");
    }

    // === 测试辅助类 ===

    private sealed class TestContext
    {
        public List<string> ExecutionLog { get; } = [];
        public Dictionary<string, string> Bag { get; } = [];
    }

    private sealed class TrackingMiddleware(string label) : IMiddleware<TestContext>
    {
        public ErrorBehavior OnError => ErrorBehavior.Continue;

        public async Task InvokeAsync(TestContext context, MiddlewareDelegate<TestContext> next, CancellationToken ct)
        {
            context.ExecutionLog.Add(label);
            await next(context, ct).ConfigureAwait(true);
        }
    }

    private sealed class ThrowingMiddleware(string label, ErrorBehavior errorBehavior) : IMiddleware<TestContext>
    {
        public ErrorBehavior OnError => errorBehavior;

        public Task InvokeAsync(TestContext context, MiddlewareDelegate<TestContext> next, CancellationToken ct)
            => throw new InvalidOperationException($"{label} failed");
    }

    private sealed class ShortCircuitMiddleware : IMiddleware<TestContext>
    {
        public ErrorBehavior OnError => ErrorBehavior.Propagate;

        public Task InvokeAsync(TestContext context, MiddlewareDelegate<TestContext> next, CancellationToken ct)
        {
            context.ExecutionLog.Add("short-circuit");
            return Task.CompletedTask;
        }
    }

    private sealed class ContextWriterMiddleware(string key, string value) : IMiddleware<TestContext>
    {
        public ErrorBehavior OnError => ErrorBehavior.Continue;

        public async Task InvokeAsync(TestContext context, MiddlewareDelegate<TestContext> next, CancellationToken ct)
        {
            context.Bag[key] = value;
            await next(context, ct).ConfigureAwait(true);
        }
    }

    private sealed class ContextReaderMiddleware(string key1, string key2) : IMiddleware<TestContext>
    {
        public ErrorBehavior OnError => ErrorBehavior.Continue;

        public async Task InvokeAsync(TestContext context, MiddlewareDelegate<TestContext> next, CancellationToken ct)
        {
            context.ExecutionLog.Add($"{key1}={context.Bag[key1]}");
            context.ExecutionLog.Add($"{key2}={context.Bag[key2]}");
            await next(context, ct).ConfigureAwait(true);
        }
    }
}
