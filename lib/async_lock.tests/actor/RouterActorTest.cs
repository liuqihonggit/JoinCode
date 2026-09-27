namespace Core.Utils;

/// <summary>
/// RouterActor 单元测试 — 验证路由策略、Worker 管理、消息分发。
/// </summary>
public class RouterActorTest {
    /// <summary>验证轮询策略在多消息下均匀分配</summary>
    [Fact]
    public void RoundRobinStrategy_DistributesEvenly() {
        var strategy = new RoundRobinStrategy<string>();
        var count = 3;
        var results = new int[10];
        for (var i = 0; i < 10; i++)
            results[i] = strategy.Select(count, $"msg-{i}");

        results[0].Should().Be(1);
        results[1].Should().Be(2);
        results[2].Should().Be(0);
        results[3].Should().Be(1);
    }

    /// <summary>验证轮询策略在零 Worker 时返回零</summary>
    [Fact]
    public void RoundRobinStrategy_ZeroWorkers_ReturnsZero() {
        var strategy = new RoundRobinStrategy<string>();
        strategy.Select(0, "msg").Should().Be(0);
    }

    /// <summary>验证随机路由策略返回有效索引</summary>
    [Fact]
    public void RandomRouteStrategy_ReturnsValidIndex() {
        var strategy = new RandomRouteStrategy<string>();
        var result = strategy.Select(5, "msg");
        result.Should().BeInRange(0, 4);
    }

    /// <summary>验证添加 Worker 后能正确注册到路由器</summary>
    [Fact]
    public async Task AddWorkerAsync_RegistersWorker() {
        await using var router = new TestRouterActor();
        var handle = await router.AddTestWorkerAsync("w1");

        handle.Id.Should().Be("w1");
        handle.State.Should().Be(ChildActorState.Running);
        router.WorkerCount.Should().Be(1);
    }

    /// <summary>验证路由消息能投递到 Worker</summary>
    [Fact]
    public async Task RouteAsync_DeliversToWorker() {
        await using var router = new TestRouterActor();
        await router.AddTestWorkerAsync("w1");
        await router.AddTestWorkerAsync("w2");

        var delivered = new List<string>();
        var tcs = new TaskCompletionSource();
        await router.RouteAsync("hello", (msg, worker) => { delivered.Add(msg); tcs.TrySetResult(); });
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        delivered.Should().ContainSingle();
        delivered[0].Should().Be("hello");
    }

    /// <summary>验证轮询路由在多个 Worker 间均匀分发消息</summary>
    [Fact]
    public async Task RouteAsync_RoundRobin_DistributesAcrossWorkers() {
        await using var router = new TestRouterActor();
        await router.AddTestWorkerAsync("w1");
        await router.AddTestWorkerAsync("w2");
        await router.AddTestWorkerAsync("w3");

        var deliveryCount = new ConcurrentDictionary<string, int>();
        var gate = new TaskCompletionSource();
        for (var i = 0; i < 9; i++) {
            await router.RouteAsync($"msg-{i}", (msg, worker) => {
                var id = ((TestWorker)worker).Id;
                deliveryCount.AddOrUpdate(id, 1, (_, v) => v + 1);
                if (deliveryCount.Values.Sum() == 9) gate.TrySetResult();
            });
        }
        await gate.Task.WaitAsync(TimeSpan.FromSeconds(5));

        deliveryCount.Values.Should().HaveCount(3);
        deliveryCount.Values.Sum().Should().Be(9);
        foreach (var count in deliveryCount.Values) {
            count.Should().Be(3);
        }
    }

    /// <summary>验证无 Worker 时路由不投递消息</summary>
    [Fact]
    public async Task RouteAsync_NoWorkers_DoesNotDeliver() {
        await using var router = new TestRouterActor();

        var delivered = false;
        await router.RouteAsync("msg", (_, _) => delivered = true);
        await Task.Delay(100);

        delivered.Should().BeFalse();
    }

    /// <summary>验证异步释放会停止全部 Worker</summary>
    [Fact]
    public async Task DisposeAsync_StopsAllWorkers() {
        var router = new TestRouterActor();
        var h1 = await router.AddTestWorkerAsync("w1");
        var h2 = await router.AddTestWorkerAsync("w2");

        await router.DisposeAsync();

        h1.State.Should().Be(ChildActorState.Stopped);
        h2.State.Should().Be(ChildActorState.Stopped);
    }
}

/// <summary>测试用 RouterActor — 暴露构造函数</summary>
internal sealed class TestRouterActor : RouterActor<string> {
    public TestRouterActor() : base() { }

    /// <summary>添加测试用 Worker 并返回句柄</summary>
    public async ValueTask<ChildActorHandle> AddTestWorkerAsync(string id) {
        return await AddWorkerAsync(id, _ => new ValueTask<IAsyncDisposable>(new TestWorker(id)));
    }
}

/// <summary>测试用 Worker</summary>
internal sealed class TestWorker(string id) : IAsyncDisposable {
    /// <summary>获取 Worker 标识</summary>
    public string Id { get; } = id;
    /// <summary>释放资源</summary>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}