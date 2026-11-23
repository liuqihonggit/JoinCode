namespace Core.Tests.Query;

/// <summary>锁定查询输出的事件式挂起、边产边读和终止行为。</summary>
public sealed class QueryEngineOutputStreamTests {
    /// <summary>生产者挂起时禁止查询消费者向同步上下文重复投递空轮询。</summary>
    [Fact]
    public async Task QueryAsync_EmptyOutputSuspendsWithoutPostingAndStreamsInOrder() {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var last = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var engine = CreateEngine(new TestMiddleware(async (context, ct) => {
            await first.Task.WaitAsync(ct).ConfigureAwait(true);
            Publish(context, "first");
            await last.Task.WaitAsync(ct).ConfigureAwait(true);
            Publish(context, "last");
        }));
        await using var chunks = engine.QueryAsync("stream", new MessageList(), timeout.Token).GetAsyncEnumerator();
        var synchronizationContext = new CountingSynchronizationContext();
        var pending = StartRead(chunks, synchronizationContext);
        var postsBeforeFirst = synchronizationContext.PostCount;
        first.SetResult();
        Assert.True(await pending.WaitAsync(timeout.Token));
        Assert.Equal("first", chunks.Current.Content);
        last.SetResult();
        Assert.True(await chunks.MoveNextAsync().AsTask().WaitAsync(timeout.Token));
        Assert.Equal("last", chunks.Current.Content);
        Assert.False(await chunks.MoveNextAsync().AsTask().WaitAsync(timeout.Token));
        Assert.Equal(0, postsBeforeFirst);
    }

    /// <summary>管道故障须唤醒读者，保留原异常。</summary>
    [Fact]
    public async Task QueryAsync_PipelineFaultReachesWaitingReader() {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failure = new InvalidOperationException("pipeline failed");
        await using var engine = CreateEngine(new TestMiddleware(async (_, ct) => {
            await release.Task.WaitAsync(ct).ConfigureAwait(true);
            throw failure;
        }));
        await using var chunks = engine.QueryAsync("fault", new MessageList(), timeout.Token).GetAsyncEnumerator();
        var pending = chunks.MoveNextAsync().AsTask();
        release.SetResult();
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => pending.WaitAsync(timeout.Token)));
    }

    /// <summary>调用者提前停止枚举时取消生产者并等待其收尾。</summary>
    [Fact]
    public async Task QueryAsync_EarlyDisposalCancelsAndJoinsProducer() {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var producerExited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var engine = CreateEngine(new TestMiddleware(async (context, ct) => {
            Publish(context, "first");
            try {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct).ConfigureAwait(true);
            } finally {
                producerExited.SetResult();
            }
        }));
        await foreach (var _ in engine.QueryAsync("stop", new MessageList(), timeout.Token)) {
            break;
        }
        Assert.True(producerExited.Task.IsCompletedSuccessfully);
        Assert.False(timeout.IsCancellationRequested);
    }

    /// <summary>让旧版 Task.Yield 的第一次投递可观测，无需按时间比较 CPU。</summary>
    private static Task<bool> StartRead(IAsyncEnumerator<QueryStreamChunk> reader, SynchronizationContext target) {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(target);
        try {
            return reader.MoveNextAsync().AsTask();
        } finally {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    /// <summary>使用真实 QueryEngine，只替换生产者中间件。</summary>
    private static QueryEngine CreateEngine(IQueryMiddleware middleware) => new(
        Mock.Of<IChatClient>(), Mock.Of<IToolRegistry>(), Options.Create(new QueryEngineConfig()),
        serviceProvider: new MiddlewareProvider(middleware));

    /// <summary>发布供断言的普通内容块。</summary>
    private static void Publish(QueryMiddlewareContext context, string text) =>
        context.PublishOutput(new QueryStreamChunk { Type = AgentStreamChunkType.Content, Content = text });

    private sealed class CountingSynchronizationContext : SynchronizationContext {
        private int _posts;
        public int PostCount => Volatile.Read(ref _posts);

        /// <inheritdoc />
        public override void Post(SendOrPostCallback callback, object? state) {
            Interlocked.Increment(ref _posts);
            base.Post(callback, state);
        }
    }

    private sealed class MiddlewareProvider(IQueryMiddleware middleware) : IServiceProvider {
        /// <inheritdoc />
        public object? GetService(Type serviceType) =>
            serviceType == typeof(IEnumerable<IQueryMiddleware>) ? new[] { middleware } : null;
    }

    private sealed class TestMiddleware(Func<QueryMiddlewareContext, CancellationToken, Task> execute) : IQueryMiddleware {
        /// <inheritdoc />
        public ErrorBehavior OnError => ErrorBehavior.Propagate;

        /// <inheritdoc />
        public Task InvokeAsync(QueryMiddlewareContext context, MiddlewareDelegate<QueryMiddlewareContext> next, CancellationToken ct) =>
            execute(context, ct);
    }
}
