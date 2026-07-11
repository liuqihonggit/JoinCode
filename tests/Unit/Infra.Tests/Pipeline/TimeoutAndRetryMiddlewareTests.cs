namespace Infrastructure.Pipeline.Tests;

using JoinCode.Abstractions.Pipeline;
using Infrastructure.Pipeline.Middlewares;

/// <summary>
/// TimeoutMiddleware + RetryMiddleware 单元测试
/// </summary>
public sealed class TimeoutAndRetryMiddlewareTests
{
    // === TimeoutMiddleware ===

    [Fact]
    public async Task Timeout_CompletesWithinTimeout_Succeeds()
    {
        var ctx = new TestTimeoutContext { Timeout = TimeSpan.FromSeconds(5) };
        var pipeline = new PipelineBuilder<TestTimeoutContext>()
            .Use(new TimeoutMiddleware<TestTimeoutContext>())
            .Use(new TrackingMiddleware("work"))
            .Build();

        await pipeline.ExecuteAsync(ctx, CancellationToken.None).ConfigureAwait(true);

        ctx.ExecutionLog.Should().Equal("work");
        ctx.IsTimedOut.Should().BeFalse();
    }

    [Fact]
    public async Task Timeout_ExceedsTimeout_SetsIsTimedOutAndThrows()
    {
        var ctx = new TestTimeoutContext { Timeout = TimeSpan.FromMilliseconds(50) };
        var pipeline = new PipelineBuilder<TestTimeoutContext>()
            .Use(new TimeoutMiddleware<TestTimeoutContext>())
            .Use(new SlowMiddleware(TimeSpan.FromMilliseconds(500)))
            .Build();

        var act = async () => await pipeline.ExecuteAsync(ctx, CancellationToken.None).ConfigureAwait(true);

        await act.Should().ThrowAsync<TimeoutException>().ConfigureAwait(true);
        ctx.IsTimedOut.Should().BeTrue();
    }

    [Fact]
    public async Task Timeout_ExternalCancellation_DoesNotSetIsTimedOut()
    {
        var ctx = new TestTimeoutContext { Timeout = TimeSpan.FromSeconds(5) };
        var pipeline = new PipelineBuilder<TestTimeoutContext>()
            .Use(new TimeoutMiddleware<TestTimeoutContext>())
            .Use(new SlowMiddleware(TimeSpan.FromMilliseconds(500)))
            .Build();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var act = async () => await pipeline.ExecuteAsync(ctx, cts.Token).ConfigureAwait(true);

        await act.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(true);
        ctx.IsTimedOut.Should().BeFalse();
    }

    // === RetryMiddleware ===

    [Fact]
    public async Task Retry_SucceedsFirstTime_NoRetry()
    {
        var ctx = new TestRetryContext { MaxRetries = 3 };
        var pipeline = new PipelineBuilder<TestRetryContext>()
            .Use(new RetryMiddleware<TestRetryContext>())
            .Use(new TrackingMiddleware("work"))
            .Build();

        await pipeline.ExecuteAsync(ctx, CancellationToken.None).ConfigureAwait(true);

        ctx.ExecutionLog.Should().Equal("work");
        ctx.RetryCount.Should().Be(0);
        ctx.LastError.Should().BeNull();
    }

    [Fact]
    public async Task Retry_FailsThenSucceeds_RetriesCorrectTimes()
    {
        var ctx = new TestRetryContext { MaxRetries = 3 };
        var attempt = 0;
        var pipeline = new PipelineBuilder<TestRetryContext>()
            .Use(new RetryMiddleware<TestRetryContext>())
            .Use(new FailThenSucceedMiddleware(2, () => attempt++))
            .Build();

        await pipeline.ExecuteAsync(ctx, CancellationToken.None).ConfigureAwait(true);

        ctx.RetryCount.Should().Be(2);
        ctx.LastError.Should().BeNull();
    }

    [Fact]
    public async Task Retry_ExceedsMaxRetries_ThrowsLastError()
    {
        var ctx = new TestRetryContext { MaxRetries = 2 };
        var pipeline = new PipelineBuilder<TestRetryContext>()
            .Use(new RetryMiddleware<TestRetryContext>())
            .Use(new AlwaysFailMiddleware())
            .Build();

        var act = async () => await pipeline.ExecuteAsync(ctx, CancellationToken.None).ConfigureAwait(true);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("always fail").ConfigureAwait(true);
        ctx.RetryCount.Should().Be(2);
    }

    [Fact]
    public async Task Retry_NonRetryableException_ThrowsImmediately()
    {
        var ctx = new TestRetryContext { MaxRetries = 3, RetryableExceptionType = typeof(ArgumentException) };
        var pipeline = new PipelineBuilder<TestRetryContext>()
            .Use(new RetryMiddleware<TestRetryContext>())
            .Use(new AlwaysFailMiddleware())
            .Build();

        var act = async () => await pipeline.ExecuteAsync(ctx, CancellationToken.None).ConfigureAwait(true);

        await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(true);
        ctx.RetryCount.Should().Be(0);
    }

    // === FixedTimeoutMiddleware ===

    [Fact]
    public async Task FixedTimeout_CompletesWithinTimeout_Succeeds()
    {
        var pipeline = new PipelineBuilder<SimpleContext>()
            .Use(new FixedTimeoutMiddleware<SimpleContext>(TimeSpan.FromSeconds(5)))
            .Use(new SimpleTrackingMiddleware("work"))
            .Build();

        var ctx = new SimpleContext();
        await pipeline.ExecuteAsync(ctx, CancellationToken.None).ConfigureAwait(true);

        ctx.Log.Should().Equal("work");
    }

    [Fact]
    public async Task FixedTimeout_ExceedsTimeout_ThrowsTimeoutException()
    {
        var pipeline = new PipelineBuilder<SimpleContext>()
            .Use(new FixedTimeoutMiddleware<SimpleContext>(TimeSpan.FromMilliseconds(50)))
            .Use(new SimpleSlowMiddleware(TimeSpan.FromMilliseconds(500)))
            .Build();

        var ctx = new SimpleContext();
        var act = async () => await pipeline.ExecuteAsync(ctx, CancellationToken.None).ConfigureAwait(true);

        await act.Should().ThrowAsync<TimeoutException>().ConfigureAwait(true);
    }

    // === FixedRetryMiddleware ===

    [Fact]
    public async Task FixedRetry_SucceedsFirstTime_NoRetry()
    {
        var pipeline = new PipelineBuilder<SimpleContext>()
            .Use(new FixedRetryMiddleware<SimpleContext>(3))
            .Use(new SimpleTrackingMiddleware("work"))
            .Build();

        var ctx = new SimpleContext();
        await pipeline.ExecuteAsync(ctx, CancellationToken.None).ConfigureAwait(true);

        ctx.Log.Should().Equal("work");
    }

    [Fact]
    public async Task FixedRetry_FailsThenSucceeds_Retries()
    {
        var attempt = 0;
        var pipeline = new PipelineBuilder<SimpleContext>()
            .Use(new FixedRetryMiddleware<SimpleContext>(3, ex => ex is InvalidOperationException))
            .Use(new SimpleFailThenSucceedMiddleware(2, () => attempt++))
            .Build();

        var ctx = new SimpleContext();
        await pipeline.ExecuteAsync(ctx, CancellationToken.None).ConfigureAwait(true);

        ctx.Log.Should().Contain("success");
    }

    [Fact]
    public async Task FixedRetry_NonRetryableException_ThrowsImmediately()
    {
        var pipeline = new PipelineBuilder<SimpleContext>()
            .Use(new FixedRetryMiddleware<SimpleContext>(3, ex => ex is ArgumentException))
            .Use(new SimpleAlwaysFailMiddleware())
            .Build();

        var ctx = new SimpleContext();
        var act = async () => await pipeline.ExecuteAsync(ctx, CancellationToken.None).ConfigureAwait(true);

        await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(true);
    }

    // === 测试辅助类 ===

    private sealed class TestTimeoutContext : ITimeoutContext
    {
        public List<string> ExecutionLog { get; } = [];
        public bool Failed { get; set; }
        public string? ErrorMessage { get; set; }
        public void Fail(string message) { Failed = true; ErrorMessage = message; }
        public TimeSpan Timeout { get; init; }
        public bool IsTimedOut { get; set; }
    }

    private sealed class TestRetryContext : IRetryContext
    {
        public List<string> ExecutionLog { get; } = [];
        public bool Failed { get; set; }
        public string? ErrorMessage { get; set; }
        public void Fail(string message) { Failed = true; ErrorMessage = message; }
        public int MaxRetries { get; init; } = 3;
        public int RetryCount { get; set; }
        public Exception? LastError { get; set; }
        public Type? RetryableExceptionType { get; init; } = typeof(InvalidOperationException);
        public bool IsRetryable(Exception ex) => RetryableExceptionType?.IsInstanceOfType(ex) == true;
    }

    private sealed class TrackingMiddleware(string label) : IMiddleware<TestTimeoutContext>, IMiddleware<TestRetryContext>
    {
        public ErrorBehavior OnError => ErrorBehavior.Continue;

        public async Task InvokeAsync(TestTimeoutContext context, MiddlewareDelegate<TestTimeoutContext> next, CancellationToken ct)
        {
            context.ExecutionLog.Add(label);
            await next(context, ct).ConfigureAwait(true);
        }

        public async Task InvokeAsync(TestRetryContext context, MiddlewareDelegate<TestRetryContext> next, CancellationToken ct)
        {
            context.ExecutionLog.Add(label);
            await next(context, ct).ConfigureAwait(true);
        }
    }

    private sealed class SlowMiddleware(TimeSpan delay) : IMiddleware<TestTimeoutContext>
    {
        public ErrorBehavior OnError => ErrorBehavior.Propagate;

        public async Task InvokeAsync(TestTimeoutContext context, MiddlewareDelegate<TestTimeoutContext> next, CancellationToken ct)
        {
            await Task.Delay(delay, ct).ConfigureAwait(true);
            context.ExecutionLog.Add("slow-work");
        }
    }

    private sealed class FailThenSucceedMiddleware(int failCount, Action onAttempt) : IMiddleware<TestRetryContext>
    {
        private int _attempts;
        public ErrorBehavior OnError => ErrorBehavior.Propagate;

        public async Task InvokeAsync(TestRetryContext context, MiddlewareDelegate<TestRetryContext> next, CancellationToken ct)
        {
            _attempts++;
            onAttempt();
            if (_attempts <= failCount)
                throw new InvalidOperationException($"fail attempt {_attempts}");

            context.ExecutionLog.Add("success");
            await next(context, ct).ConfigureAwait(true);
        }
    }

    private sealed class AlwaysFailMiddleware : IMiddleware<TestRetryContext>
    {
        public ErrorBehavior OnError => ErrorBehavior.Propagate;

        public Task InvokeAsync(TestRetryContext context, MiddlewareDelegate<TestRetryContext> next, CancellationToken ct)
            => throw new InvalidOperationException("always fail");
    }

    // === Fixed 版本辅助类（无需接口约束） ===

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

    private sealed class SimpleSlowMiddleware(TimeSpan delay) : IMiddleware<SimpleContext>
    {
        public ErrorBehavior OnError => ErrorBehavior.Propagate;

        public async Task InvokeAsync(SimpleContext context, MiddlewareDelegate<SimpleContext> next, CancellationToken ct)
        {
            await Task.Delay(delay, ct).ConfigureAwait(true);
            context.Log.Add("slow-work");
        }
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

    private sealed class SimpleAlwaysFailMiddleware : IMiddleware<SimpleContext>
    {
        public ErrorBehavior OnError => ErrorBehavior.Propagate;

        public Task InvokeAsync(SimpleContext context, MiddlewareDelegate<SimpleContext> next, CancellationToken ct)
            => throw new InvalidOperationException("always fail");
    }
}
