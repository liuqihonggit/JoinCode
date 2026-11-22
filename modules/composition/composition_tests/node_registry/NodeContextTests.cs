namespace JoinCode.Composition.Tests3.NodeRegistry;

public sealed class NodeContextTests {
    [Fact]
    public async Task UnloadAsync_ExecutesCleanupsInReverseOrder() {
        var order = new List<string>();
        await using var ctx = new NodeContext(new ServiceCollection().BuildServiceProvider());

        ctx.RegisterEffect(1, () => order.Add("cleanup-1"));
        ctx.RegisterEffect(2, () => order.Add("cleanup-2"));
        ctx.RegisterEffect(3, () => order.Add("cleanup-3"));

        await ctx.UnloadAsync();

        Assert.Equal(["cleanup-3", "cleanup-2", "cleanup-1"], order);
    }

    [Fact]
    public async Task UnloadAsync_AsyncCleanupsExecuteInReverseOrder() {
        var order = new List<string>();
        await using var ctx = new NodeContext(new ServiceCollection().BuildServiceProvider());

        ctx.RegisterEffect(1, () => { order.Add("async-1"); return ValueTask.CompletedTask; });
        ctx.RegisterEffect(2, () => { order.Add("async-2"); return ValueTask.CompletedTask; });

        await ctx.UnloadAsync();

        Assert.Equal(["async-2", "async-1"], order);
    }

    [Fact]
    public async Task UnloadAsync_IsIdempotent() {
        var count = 0;
        await using var ctx = new NodeContext(new ServiceCollection().BuildServiceProvider());

        ctx.RegisterEffect(1, () => count++);
        await ctx.UnloadAsync();
        await ctx.UnloadAsync();

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task RegisterEffect_AfterUnload_ThrowsObjectDisposedException() {
        await using var ctx = new NodeContext(new ServiceCollection().BuildServiceProvider());
        await ctx.UnloadAsync();

        Assert.Throws<ObjectDisposedException>(() => ctx.RegisterEffect(1, () => { }));
    }

    [Fact]
    public async Task UnloadAsync_CleanupException_DoesNotPropagate() {
        await using var ctx = new NodeContext(new ServiceCollection().BuildServiceProvider());

        ctx.RegisterEffect(1, () => throw new InvalidOperationException("boom"));
        ctx.RegisterEffect(2, () => { });

        await ctx.UnloadAsync();
    }

    [Fact]
    public async Task UnloadAsync_CleanupException_ReportsViaCallback() {
        Exception? caught = null;
        await using var ctx = new NodeContext(
            new ServiceCollection().BuildServiceProvider(),
            (ex, _) => caught = ex);

        ctx.RegisterEffect(1, () => throw new InvalidOperationException("boom"));

        await ctx.UnloadAsync();

        Assert.NotNull(caught);
        Assert.IsType<InvalidOperationException>(caught);
        if (caught is InvalidOperationException ex) {
            Assert.Equal("boom", ex.Message);
        }
    }

    [Fact]
    public async Task UnloadAsync_ContinuesAfterCleanupException() {
        var executed = new List<int>();
        await using var ctx = new NodeContext(new ServiceCollection().BuildServiceProvider());

        ctx.RegisterEffect(1, () => executed.Add(1));
        ctx.RegisterEffect(2, () => throw new InvalidOperationException("boom"));
        ctx.RegisterEffect(3, () => executed.Add(3));

        await ctx.UnloadAsync();

        Assert.Equal([3, 1], executed);
    }

    [Fact]
    public async Task RegisterEffect_ReturnsValue() {
        await using var ctx = new NodeContext(new ServiceCollection().BuildServiceProvider());

        var result = ctx.RegisterEffect("hello", () => { });

        Assert.Equal("hello", result);
    }

    [Fact]
    public async Task GetService_ResolvesFromServiceProvider() {
        var services = new ServiceCollection();
        services.AddSingleton<TestService>();
        await using var ctx = new NodeContext(services.BuildServiceProvider());

        var service = ctx.GetService<TestService>();

        Assert.NotNull(service);
    }

    [Fact]
    public async Task DisposeAsync_EquivalentToUnloadAsync() {
        var executed = false;
        await using var ctx = new NodeContext(new ServiceCollection().BuildServiceProvider());

        ctx.RegisterEffect(1, () => executed = true);
        await ctx.DisposeAsync();

        Assert.True(executed);
    }

    [Fact]
    public async Task RegisteredCount_TracksRegisteredEffects() {
        await using var ctx = new NodeContext(new ServiceCollection().BuildServiceProvider());

        ctx.RegisterEffect(1, () => { });
        ctx.RegisterEffect(2, () => { });

        Assert.Equal(2, ctx.RegisteredCount);
    }

    private sealed class TestService;
}
