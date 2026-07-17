namespace Infrastructure.Pipeline.Tests;

using JoinCode.Abstractions.Pipeline;
using Infrastructure.Pipeline.Middlewares;

public sealed class StreamRateLimitAndCircuitBreakerTests
{
    [Fact]
    public async Task StreamRateLimit_WithinLimit_Succeeds()
    {
        var pipeline = new StreamPipelineBuilder<StreamTestContext, string>()
            .Use(new FixedStreamRateLimitMiddleware<StreamTestContext, string>(3, TimeSpan.FromSeconds(10)))
            .Use(new StreamTrackingMiddleware("work"))
            .Build();

        var ctx = new StreamTestContext();
        var events = new List<string>();
        await foreach (var evt in pipeline.ExecuteAsync(ctx, CancellationToken.None).ConfigureAwait(true))
        {
            events.Add(evt);
        }

        events.Should().Equal("work");
    }

    [Fact]
    public async Task StreamRateLimit_ExceedsLimit_ThrowsRateLimitExceededException()
    {
        var pipeline = new StreamPipelineBuilder<StreamTestContext, string>()
            .Use(new FixedStreamRateLimitMiddleware<StreamTestContext, string>(2, TimeSpan.FromSeconds(10)))
            .Use(new StreamTrackingMiddleware("work"))
            .Build();

        await CollectEventsAsync(pipeline).ConfigureAwait(true);
        await CollectEventsAsync(pipeline).ConfigureAwait(true);

        var act = async () => await CollectEventsAsync(pipeline).ConfigureAwait(true);
        await act.Should().ThrowAsync<RateLimitExceededException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task StreamRateLimit_WindowResets_AllowsNewRequests()
    {
        var pipeline = new StreamPipelineBuilder<StreamTestContext, string>()
            .Use(new FixedStreamRateLimitMiddleware<StreamTestContext, string>(1, TimeSpan.FromMilliseconds(100)))
            .Use(new StreamTrackingMiddleware("work"))
            .Build();

        await CollectEventsAsync(pipeline).ConfigureAwait(true);

        var act = async () => await CollectEventsAsync(pipeline).ConfigureAwait(true);
        await act.Should().ThrowAsync<RateLimitExceededException>().ConfigureAwait(true);

        await Task.Delay(150).ConfigureAwait(true);

        var events = await CollectEventsAsync(pipeline).ConfigureAwait(true);
        events.Should().Equal("work");
    }

    [Fact]
    public async Task StreamCircuitBreaker_BelowThreshold_Succeeds()
    {
        var pipeline = new StreamPipelineBuilder<StreamTestContext, string>()
            .Use(new FixedStreamCircuitBreakerMiddleware<StreamTestContext, string>(3, TimeSpan.FromSeconds(10)))
            .Use(new StreamTrackingMiddleware("work"))
            .Build();

        var events = await CollectEventsAsync(pipeline).ConfigureAwait(true);
        events.Should().Equal("work");
    }

    [Fact]
    public async Task StreamCircuitBreaker_ReachesThreshold_ThrowsCircuitBreakerOpenException()
    {
        var pipeline = new StreamPipelineBuilder<StreamTestContext, string>()
            .Use(new FixedStreamCircuitBreakerMiddleware<StreamTestContext, string>(2, TimeSpan.FromSeconds(10)))
            .Use(new StreamAlwaysFailMiddleware())
            .Build();

        var act1 = async () => await CollectEventsAsync(pipeline).ConfigureAwait(true);
        await act1.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(true);

        var act2 = async () => await CollectEventsAsync(pipeline).ConfigureAwait(true);
        await act2.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(true);

        var act3 = async () => await CollectEventsAsync(pipeline).ConfigureAwait(true);
        await act3.Should().ThrowAsync<CircuitBreakerOpenException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task StreamCircuitBreaker_SuccessResetsFailureCount()
    {
        var failPipeline = new StreamPipelineBuilder<StreamTestContext, string>()
            .Use(new FixedStreamCircuitBreakerMiddleware<StreamTestContext, string>(3, TimeSpan.FromSeconds(10)))
            .Use(new StreamAlwaysFailMiddleware())
            .Build();

        var act = async () => await CollectEventsAsync(failPipeline).ConfigureAwait(true);
        await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(true);

        var successPipeline = new StreamPipelineBuilder<StreamTestContext, string>()
            .Use(new FixedStreamCircuitBreakerMiddleware<StreamTestContext, string>(3, TimeSpan.FromSeconds(10)))
            .Use(new StreamTrackingMiddleware("work"))
            .Build();

        var events = await CollectEventsAsync(successPipeline).ConfigureAwait(true);
        events.Should().Equal("work");
    }

    [Fact]
    public async Task StreamCircuitBreaker_CooldownExpires_AllowsRetry()
    {
        var pipeline = new StreamPipelineBuilder<StreamTestContext, string>()
            .Use(new FixedStreamCircuitBreakerMiddleware<StreamTestContext, string>(1, TimeSpan.FromMilliseconds(100)))
            .Use(new StreamAlwaysFailMiddleware())
            .Build();

        var act1 = async () => await CollectEventsAsync(pipeline).ConfigureAwait(true);
        await act1.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(true);

        var act2 = async () => await CollectEventsAsync(pipeline).ConfigureAwait(true);
        await act2.Should().ThrowAsync<CircuitBreakerOpenException>().ConfigureAwait(true);

        await Task.Delay(150).ConfigureAwait(true);

        var act3 = async () => await CollectEventsAsync(pipeline).ConfigureAwait(true);
        await act3.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(true);
    }

    private static async Task<List<string>> CollectEventsAsync(
        StreamMiddlewarePipeline<StreamTestContext, string> pipeline,
        CancellationToken ct = default)
    {
        var events = new List<string>();
        await foreach (var evt in pipeline.ExecuteAsync(new StreamTestContext(), ct).ConfigureAwait(true))
        {
            events.Add(evt);
        }

        return events;
    }

    private sealed class StreamTestContext
    {
        public List<string> Log { get; } = [];
    }

    private sealed class StreamTrackingMiddleware(string label) : IStreamMiddleware<StreamTestContext, string>
    {
        public ErrorBehavior OnError => ErrorBehavior.Continue;

        public async IAsyncEnumerable<string> InvokeAsync(
            StreamTestContext context,
            StreamMiddlewareDelegate<StreamTestContext, string> next,
            [EnumeratorCancellation] CancellationToken ct)
        {
            context.Log.Add(label);
            yield return label;
            await foreach (var evt in next(context, ct).ConfigureAwait(false))
            {
                yield return evt;
            }
        }
    }

    private sealed class StreamAlwaysFailMiddleware : IStreamMiddleware<StreamTestContext, string>
    {

        public IAsyncEnumerable<string> InvokeAsync(
            StreamTestContext context,
            StreamMiddlewareDelegate<StreamTestContext, string> next,
            CancellationToken ct)
            => throw new InvalidOperationException("always fail");
    }
}
