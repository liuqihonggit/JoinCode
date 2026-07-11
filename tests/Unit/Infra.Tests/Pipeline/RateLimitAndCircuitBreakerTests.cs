namespace Infrastructure.Pipeline.Tests;

using JoinCode.Abstractions.Pipeline;
using Infrastructure.Pipeline.Middlewares;

public sealed class RateLimitAndCircuitBreakerTests
{
    // === FixedRateLimitMiddleware ===

    [Fact]
    public async Task RateLimit_WithinLimit_Succeeds()
    {
        var pipeline = new PipelineBuilder<SimpleContext>()
            .Use(new FixedRateLimitMiddleware<SimpleContext>(3, TimeSpan.FromSeconds(10)))
            .Use(new SimpleTrackingMiddleware("work"))
            .Build();

        var ctx = new SimpleContext();
        await pipeline.ExecuteAsync(ctx, CancellationToken.None).ConfigureAwait(true);

        ctx.Log.Should().Equal("work");
    }

    [Fact]
    public async Task RateLimit_ExceedsLimit_ThrowsRateLimitExceededException()
    {
        var pipeline = new PipelineBuilder<SimpleContext>()
            .Use(new FixedRateLimitMiddleware<SimpleContext>(2, TimeSpan.FromSeconds(10)))
            .Use(new SimpleTrackingMiddleware("work"))
            .Build();

        await pipeline.ExecuteAsync(new SimpleContext(), CancellationToken.None).ConfigureAwait(true);
        await pipeline.ExecuteAsync(new SimpleContext(), CancellationToken.None).ConfigureAwait(true);

        var act = async () => await pipeline.ExecuteAsync(new SimpleContext(), CancellationToken.None).ConfigureAwait(true);

        await act.Should().ThrowAsync<RateLimitExceededException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task RateLimit_WindowResets_AllowsNewRequests()
    {
        var pipeline = new PipelineBuilder<SimpleContext>()
            .Use(new FixedRateLimitMiddleware<SimpleContext>(1, TimeSpan.FromMilliseconds(100)))
            .Use(new SimpleTrackingMiddleware("work"))
            .Build();

        await pipeline.ExecuteAsync(new SimpleContext(), CancellationToken.None).ConfigureAwait(true);

        var act = async () => await pipeline.ExecuteAsync(new SimpleContext(), CancellationToken.None).ConfigureAwait(true);
        await act.Should().ThrowAsync<RateLimitExceededException>().ConfigureAwait(true);

        await Task.Delay(150).ConfigureAwait(true);

        var ctx = new SimpleContext();
        await pipeline.ExecuteAsync(ctx, CancellationToken.None).ConfigureAwait(true);
        ctx.Log.Should().Equal("work");
    }

    [Fact]
    public async Task RateLimit_LimitOne_SingleRequestOnly()
    {
        var pipeline = new PipelineBuilder<SimpleContext>()
            .Use(new FixedRateLimitMiddleware<SimpleContext>(1, TimeSpan.FromSeconds(10)))
            .Use(new SimpleTrackingMiddleware("work"))
            .Build();

        await pipeline.ExecuteAsync(new SimpleContext(), CancellationToken.None).ConfigureAwait(true);

        var act = async () => await pipeline.ExecuteAsync(new SimpleContext(), CancellationToken.None).ConfigureAwait(true);
        await act.Should().ThrowAsync<RateLimitExceededException>().ConfigureAwait(true);
    }

    // === FixedCircuitBreakerMiddleware ===

    [Fact]
    public async Task CircuitBreaker_BelowThreshold_Succeeds()
    {
        var pipeline = new PipelineBuilder<SimpleContext>()
            .Use(new FixedCircuitBreakerMiddleware<SimpleContext>(3, TimeSpan.FromSeconds(10)))
            .Use(new SimpleTrackingMiddleware("work"))
            .Build();

        var ctx = new SimpleContext();
        await pipeline.ExecuteAsync(ctx, CancellationToken.None).ConfigureAwait(true);

        ctx.Log.Should().Equal("work");
    }

    [Fact]
    public async Task CircuitBreaker_ReachesThreshold_ThrowsCircuitBreakerOpenException()
    {
        var pipeline = new PipelineBuilder<SimpleContext>()
            .Use(new FixedCircuitBreakerMiddleware<SimpleContext>(2, TimeSpan.FromSeconds(10)))
            .Use(new SimpleAlwaysFailMiddleware())
            .Build();

        var act1 = async () => await pipeline.ExecuteAsync(new SimpleContext(), CancellationToken.None).ConfigureAwait(true);
        await act1.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(true);

        var act2 = async () => await pipeline.ExecuteAsync(new SimpleContext(), CancellationToken.None).ConfigureAwait(true);
        await act2.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(true);

        var act3 = async () => await pipeline.ExecuteAsync(new SimpleContext(), CancellationToken.None).ConfigureAwait(true);
        await act3.Should().ThrowAsync<CircuitBreakerOpenException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task CircuitBreaker_SuccessResetsFailureCount()
    {
        var pipeline = new PipelineBuilder<SimpleContext>()
            .Use(new FixedCircuitBreakerMiddleware<SimpleContext>(3, TimeSpan.FromSeconds(10)))
            .Use(new SimpleFailThenSucceedMiddleware(1, () => { }))
            .Build();

        var act = async () => await pipeline.ExecuteAsync(new SimpleContext(), CancellationToken.None).ConfigureAwait(true);
        await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(true);

        await pipeline.ExecuteAsync(new SimpleContext(), CancellationToken.None).ConfigureAwait(true);

        var act2 = async () => await pipeline.ExecuteAsync(new SimpleContext(), CancellationToken.None).ConfigureAwait(true);
        await act2.Should().NotThrowAsync<CircuitBreakerOpenException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task CircuitBreaker_CooldownExpires_AllowsRetry()
    {
        var pipeline = new PipelineBuilder<SimpleContext>()
            .Use(new FixedCircuitBreakerMiddleware<SimpleContext>(1, TimeSpan.FromMilliseconds(100)))
            .Use(new SimpleAlwaysFailMiddleware())
            .Build();

        var act1 = async () => await pipeline.ExecuteAsync(new SimpleContext(), CancellationToken.None).ConfigureAwait(true);
        await act1.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(true);

        var act2 = async () => await pipeline.ExecuteAsync(new SimpleContext(), CancellationToken.None).ConfigureAwait(true);
        await act2.Should().ThrowAsync<CircuitBreakerOpenException>().ConfigureAwait(true);

        await Task.Delay(150).ConfigureAwait(true);

        var act3 = async () => await pipeline.ExecuteAsync(new SimpleContext(), CancellationToken.None).ConfigureAwait(true);
        await act3.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task CircuitBreaker_OperationCanceledException_DoesNotCountAsFailure()
    {
        var pipeline = new PipelineBuilder<SimpleContext>()
            .Use(new FixedCircuitBreakerMiddleware<SimpleContext>(1, TimeSpan.FromSeconds(10)))
            .Use(new SimpleCancelMiddleware())
            .Build();

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync().ConfigureAwait(true);

        var act = async () => await pipeline.ExecuteAsync(new SimpleContext(), cts.Token).ConfigureAwait(true);
        await act.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(true);

        var ctx = new SimpleContext();
        var workPipeline = new PipelineBuilder<SimpleContext>()
            .Use(new FixedCircuitBreakerMiddleware<SimpleContext>(1, TimeSpan.FromSeconds(10)))
            .Use(new SimpleTrackingMiddleware("work"))
            .Build();

        await workPipeline.ExecuteAsync(ctx, CancellationToken.None).ConfigureAwait(true);
        ctx.Log.Should().Equal("work");
    }

    // === 测试辅助类 ===

    private sealed class SimpleContext
    {
        public List<string> Log { get; } = [];
    }

    private sealed class SimpleTrackingMiddleware(string label) : IMiddleware<SimpleContext>
    {
        public ErrorBehavior OnError => ErrorBehavior.Continue;

        public async Task InvokeAsync(SimpleContext context, MiddlewareDelegate<SimpleContext> next, CancellationToken ct)
        {
            context.Log.Add(label);
            await next(context, ct).ConfigureAwait(true);
        }
    }

    private sealed class SimpleAlwaysFailMiddleware : IMiddleware<SimpleContext>
    {
        public ErrorBehavior OnError => ErrorBehavior.Propagate;

        public Task InvokeAsync(SimpleContext context, MiddlewareDelegate<SimpleContext> next, CancellationToken ct)
            => throw new InvalidOperationException("always fail");
    }

    private sealed class SimpleFailThenSucceedMiddleware(int failCount, Action onAttempt) : IMiddleware<SimpleContext>
    {
        private int _attempts;
        public ErrorBehavior OnError => ErrorBehavior.Propagate;

        public async Task InvokeAsync(SimpleContext context, MiddlewareDelegate<SimpleContext> next, CancellationToken ct)
        {
            _attempts++;
            onAttempt();
            if (_attempts <= failCount)
                throw new InvalidOperationException($"fail attempt {_attempts}");

            context.Log.Add("success");
            await next(context, ct).ConfigureAwait(true);
        }
    }

    private sealed class SimpleCancelMiddleware : IMiddleware<SimpleContext>
    {
        public ErrorBehavior OnError => ErrorBehavior.Propagate;

        public Task InvokeAsync(SimpleContext context, MiddlewareDelegate<SimpleContext> next, CancellationToken ct)
            => Task.FromCanceled(ct);
    }
}
