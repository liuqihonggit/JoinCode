namespace Core.Tests.Plugins;

public sealed class EventDispatcherTests
{
    private static Func<T, CancellationToken, Task> Handler<T>(Action<T> action)
    {
        return (arg, _) =>
        {
            action(arg);
            return Task.CompletedTask;
        };
    }

    private static Func<T, CancellationToken, Task<bool>> BailHandler<T>(Func<T, bool> shouldBail)
    {
        return (arg, _) => Task.FromResult(shouldBail(arg));
    }

    [Fact]
    public async Task EmitAsync_FireAndForget_DoesNotAwaitOrder()
    {
        var order = new List<int>();
        var handlers = new Func<string, CancellationToken, Task>[]
        {
            Handler<string>(_ => order.Add(1)),
            Handler<string>(_ => order.Add(2)),
            Handler<string>(_ => order.Add(3)),
        };
        await EventDispatcher.EmitAsync(handlers, "x", default);
        Assert.Equal(new[] { 1, 2, 3 }, order);
    }

    [Fact]
    public async Task EmitAsync_EmptyHandlers_Completes()
    {
        var handlers = Array.Empty<Func<string, CancellationToken, Task>>();
        await EventDispatcher.EmitAsync(handlers, "x", default);
    }

    [Fact]
    public async Task ParallelAsync_AllHandlersComplete()
    {
        var results = new ConcurrentBag<int>();
        var handlers = new Func<string, CancellationToken, Task>[]
        {
            async (_, _) => { await Task.Delay(10); results.Add(1); },
            async (_, _) => { await Task.Delay(5); results.Add(2); },
            async (_, _) => { results.Add(3); },
        };
        await EventDispatcher.ParallelAsync(handlers, "x", default);
        Assert.Equal(3, results.Count);
    }

    [Fact]
    public async Task SerialAsync_ExecutedInOrder()
    {
        var order = new List<int>();
        var handlers = new Func<string, CancellationToken, Task>[]
        {
            async (_, _) => { await Task.Delay(15); order.Add(1); },
            async (_, _) => { await Task.Delay(5); order.Add(2); },
            async (_, _) => { order.Add(3); },
        };
        await EventDispatcher.SerialAsync(handlers, "x", default);
        Assert.Equal(new[] { 1, 2, 3 }, order);
    }

    [Fact]
    public async Task BailAsync_FirstBailStops()
    {
        var called = new List<int>();
        var handlers = new Func<string, CancellationToken, Task<bool>>[]
        {
            BailHandler<string>(_ => { called.Add(1); return false; }),
            BailHandler<string>(_ => { called.Add(2); return true; }),
            BailHandler<string>(_ => { called.Add(3); return false; }),
        };
        var bailed = await EventDispatcher.BailAsync(handlers, "x", default);
        Assert.True(bailed);
        Assert.Equal(new[] { 1, 2 }, called);
    }

    [Fact]
    public async Task BailAsync_NoBail_ReturnsFalse()
    {
        var handlers = new Func<string, CancellationToken, Task<bool>>[]
        {
            BailHandler<string>(_ => false),
            BailHandler<string>(_ => false),
        };
        var bailed = await EventDispatcher.BailAsync(handlers, "x", default);
        Assert.False(bailed);
    }

    [Fact]
    public async Task BailAsync_EmptyHandlers_ReturnsFalse()
    {
        var handlers = Array.Empty<Func<string, CancellationToken, Task<bool>>>();
        var bailed = await EventDispatcher.BailAsync(handlers, "x", default);
        Assert.False(bailed);
    }

    [Fact]
    public async Task WaterfallAsync_ChainsNextCalls()
    {
        var calls = new List<int>();
        var handlers = new Func<int, Func<CancellationToken, Task<int>>, CancellationToken, Task<int>>[]
        {
            (arg, next, _) => { calls.Add(1); return next(default); },
            (arg, next, _) => { calls.Add(2); return next(default); },
            (arg, next, _) => { calls.Add(3); return next(default); },
        };
        var result = await EventDispatcher.WaterfallAsync(handlers, 42, default);
        Assert.Equal(42, result);
        Assert.Equal(new[] { 1, 2, 3 }, calls);
    }

    [Fact]
    public async Task WaterfallAsync_SkipNext_ShortCircuits()
    {
        var calls = new List<int>();
        var handlers = new Func<int, Func<CancellationToken, Task<int>>, CancellationToken, Task<int>>[]
        {
            (arg, next, _) => { calls.Add(1); return Task.FromResult(99); },
            (arg, next, _) => { calls.Add(2); return next(default); },
        };
        var result = await EventDispatcher.WaterfallAsync(handlers, 42, default);
        Assert.Equal(99, result);
        Assert.Equal(new[] { 1 }, calls);
    }

    [Fact]
    public async Task WaterfallAsync_EmptyHandlers_ReturnsInitial()
    {
        var handlers = Array.Empty<Func<int, Func<CancellationToken, Task<int>>, CancellationToken, Task<int>>>();
        var result = await EventDispatcher.WaterfallAsync(handlers, 42, default);
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task WaterfallAsync_LastHandlerNextReturnsInitial()
    {
        var handlers = new Func<int, Func<CancellationToken, Task<int>>, CancellationToken, Task<int>>[]
        {
            (arg, next, _) => next(default),
        };
        var result = await EventDispatcher.WaterfallAsync(handlers, 42, default);
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task WaterfallAsync_TransformsValueThroughChain()
    {
        var handlers = new Func<int, Func<CancellationToken, Task<int>>, CancellationToken, Task<int>>[]
        {
            async (arg, next, _) => { var n = await next(default); return n + 100; },
            async (arg, next, _) => { var n = await next(default); return n + 10; },
            async (arg, next, _) => { var n = await next(default); return n + 1; },
        };
        var result = await EventDispatcher.WaterfallAsync(handlers, 0, default);
        Assert.Equal(111, result);
    }

    [Fact]
    public async Task DispatchAsync_EmitMode_Works()
    {
        var called = false;
        var handlers = new[] { Handler<string>(_ => called = true) };
        await EventDispatcher.DispatchAsync(EventDispatchMode.Emit, handlers, "x", default);
        Assert.True(called);
    }

    [Fact]
    public async Task DispatchAsync_ParallelMode_Works()
    {
        var count = 0;
        var handlers = new[]
        {
            Handler<string>(_ => Interlocked.Increment(ref count)),
            Handler<string>(_ => Interlocked.Increment(ref count)),
        };
        await EventDispatcher.DispatchAsync(EventDispatchMode.Parallel, handlers, "x", default);
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task DispatchAsync_SerialMode_Works()
    {
        var order = new List<int>();
        var handlers = new[]
        {
            Handler<string>(_ => order.Add(1)),
            Handler<string>(_ => order.Add(2)),
        };
        await EventDispatcher.DispatchAsync(EventDispatchMode.Serial, handlers, "x", default);
        Assert.Equal(new[] { 1, 2 }, order);
    }

    [Fact]
    public async Task DispatchAsync_BailMode_Throws()
    {
        var handlers = Array.Empty<Func<string, CancellationToken, Task>>();
        var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => EventDispatcher.DispatchAsync(EventDispatchMode.Bail, handlers, "x", default));
        Assert.Contains("[INF-EVENT-DISPATCH]", ex.Message);
    }
}
