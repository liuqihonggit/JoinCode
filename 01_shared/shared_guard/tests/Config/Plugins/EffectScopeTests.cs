namespace Core.Tests.Plugins;

public sealed class EffectScopeTests
{
    private static void DisposeSync(EffectScope scope)
        => scope.DisposeAsync().GetAwaiter().GetResult();

    [Fact]
    public void Add_AppliesImmediately()
    {
        var scope = new EffectScope();
        var applied = false;
        scope.Add(() => applied = true, () => { });
        Assert.True(applied);
        Assert.Equal(1, scope.RegisteredCount);
        DisposeSync(scope);
    }

    [Fact]
    public void Dispose_RevertsInReverseOrder()
    {
        var scope = new EffectScope();
        var log = new List<int>();
        scope.Add(() => log.Add(1), () => log.Add(-1), "first");
        scope.Add(() => log.Add(2), () => log.Add(-2), "second");
        scope.Add(() => log.Add(3), () => log.Add(-3), "third");
        DisposeSync(scope);
        Assert.Equal(new[] { 1, 2, 3, -3, -2, -1 }, log);
    }

    [Fact]
    public async Task DisposeAsync_AsyncBeforeSync()
    {
        var scope = new EffectScope();
        var log = new List<string>();
        scope.Add(() => log.Add("sync-apply"), () => log.Add("sync-revert"));
        scope.AddAsync(new AsyncDisposable(() => log.Add("async-revert")));
        await scope.DisposeAsync();
        Assert.Equal(new[] { "sync-apply", "async-revert", "sync-revert" }, log);
    }

    [Fact]
    public void OnRevertFailed_InvokedOnRevertException()
    {
        Exception? caught = null;
        string? caughtDesc = null;
        var scope = new EffectScope((ex, desc) => { caught = ex; caughtDesc = desc; });
        scope.Add(() => { }, () => throw new InvalidOperationException("revert failed"), "test-desc");
        DisposeSync(scope);
        Assert.NotNull(caught);
        Assert.Equal("revert failed", caught!.Message);
        Assert.Equal("test-desc", caughtDesc);
    }

    [Fact]
    public void Add_ThrowsIfDisposed()
    {
        var scope = new EffectScope();
        DisposeSync(scope);
        Assert.Throws<ObjectDisposedException>(() => scope.Add(() => { }, () => { }));
    }

    [Fact]
    public void Add_NullRevert_Throws()
    {
        var scope = new EffectScope();
        Assert.Throws<ArgumentNullException>(() => scope.Add(() => { }, null!));
        DisposeSync(scope);
    }

    [Fact]
    public void Dispose_Idempotent()
    {
        var scope = new EffectScope();
        var count = 0;
        scope.Add(() => { }, () => count++);
        DisposeSync(scope);
        DisposeSync(scope);
        Assert.Equal(1, count);
    }

    private sealed class AsyncDisposable : IAsyncDisposable
    {
        private readonly Action _onDispose;
        public AsyncDisposable(Action onDispose) => _onDispose = onDispose;
        public ValueTask DisposeAsync() { _onDispose(); return ValueTask.CompletedTask; }
    }
}
