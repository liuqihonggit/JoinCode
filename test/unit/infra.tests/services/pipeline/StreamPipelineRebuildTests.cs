namespace Infrastructure.Pipeline.Tests;

/// <summary>
/// StreamMiddlewarePipeline Rebuild 单元测试 — DSG050 管道动态重建
/// </summary>
public sealed class StreamPipelineRebuildTests {
    [Fact]
    public async Task Rebuild_ReplacesMiddlewareChain() {
        var pipeline = new StreamMiddlewarePipeline<StreamCtx, string>([new StreamTrack("A")]);
        var events1 = await CollectAsync(pipeline).ConfigureAwait(true);
        events1.Should().Equal("A");

        pipeline.Rebuild([new StreamTrack("B"), new StreamTrack("C")]);
        var events2 = await CollectAsync(pipeline).ConfigureAwait(true);
        events2.Should().Equal("B", "C");
    }

    [Fact]
    public async Task Rebuild_EmptyMiddlewareList_ProducesNoOpPipeline() {
        var pipeline = new StreamMiddlewarePipeline<StreamCtx, string>([new StreamTrack("A")]);
        var events1 = await CollectAsync(pipeline).ConfigureAwait(true);
        events1.Should().Equal("A");

        pipeline.Rebuild([]);
        var events2 = await CollectAsync(pipeline).ConfigureAwait(true);
        events2.Should().BeEmpty();
    }

    [Fact]
    public async Task Rebuild_PreservesShortCircuitPredicate() {
        var pipeline = new StreamMiddlewarePipeline<StreamCtx, string>(
            [new StreamTrack("A"), new StreamTrack("B")],
            shortCircuitPredicate: ctx => ctx.Skip);
        var ctx1 = new StreamCtx();
        var events1 = await CollectAsync(pipeline, ctx1).ConfigureAwait(true);
        events1.Should().Equal("A", "B");

        pipeline.Rebuild([new StreamTrack("C"), new StreamTrack("D")]);
        var ctx2 = new StreamCtx { Skip = true };
        var events2 = await CollectAsync(pipeline, ctx2).ConfigureAwait(true);
        events2.Should().BeEmpty();
    }

    private static async Task<List<string>> CollectAsync(
        StreamMiddlewarePipeline<StreamCtx, string> pipeline,
        StreamCtx? ctx = null,
        CancellationToken ct = default) {
        var events = new List<string>();
        await foreach (var evt in pipeline.ExecuteAsync(ctx ?? new StreamCtx(), ct).ConfigureAwait(true)) {
            events.Add(evt);
        }
        return events;
    }

    private sealed class StreamCtx {
        public bool Skip { get; set; }
    }

    private sealed class StreamTrack(string label) : IStreamMiddleware<StreamCtx, string> {
        public ErrorBehavior OnError => ErrorBehavior.Continue;

        public async IAsyncEnumerable<string> InvokeAsync(
            StreamCtx context,
            StreamMiddlewareDelegate<StreamCtx, string> next,
            [EnumeratorCancellation] CancellationToken ct) {
            yield return label;
            await foreach (var evt in next(context, ct).ConfigureAwait(true)) {
                yield return evt;
            }
        }
    }
}
