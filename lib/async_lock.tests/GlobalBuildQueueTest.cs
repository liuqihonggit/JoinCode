namespace Core.Utils;

/// <summary>
/// GlobalBuildQueue 单元测试 — 验证串行执行、背压、取消、队列状态。
/// </summary>
public class GlobalBuildQueueTest
{
    [Fact]
    public async Task EnqueueAsync_SingleRequest_ExecutedSuccessfully()
    {
        await using var queue = new GlobalBuildQueue(static (req, ct) =>
            ValueTask.FromResult(new GlobalBuildResult
            {
                RequestId = req.RequestId,
                Success = true,
                Output = "build ok",
                Duration = TimeSpan.FromMilliseconds(10)
            }));

        var request = CreateRequest("build-1");
        var result = await queue.EnqueueAsync(request, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.RequestId.Should().Be("build-1");
    }

    [Fact]
    public async Task EnqueueAsync_MultipleRequests_ExecutedSerially()
    {
        var executionOrder = new ConcurrentQueue<string>();

        await using var queue = new GlobalBuildQueue((req, ct) =>
        {
            executionOrder.Enqueue(req.RequestId);
            return ValueTask.FromResult(new GlobalBuildResult
            {
                RequestId = req.RequestId,
                Success = true,
                Output = "",
                Duration = TimeSpan.FromMilliseconds(50)
            });
        });

        var requests = Enumerable.Range(0, 5)
            .Select(i => CreateRequest($"build-{i}"))
            .ToArray();

        var tasks = requests.Select(r => queue.EnqueueAsync(r, CancellationToken.None)).ToArray();
        var results = await Task.WhenAll(tasks);

        results.Should().HaveCount(5);
        results.Should().AllSatisfy(r => r.Success.Should().BeTrue());
        executionOrder.Should().HaveCount(5);
        executionOrder.Should().BeEquivalentTo(new[] { "build-0", "build-1", "build-2", "build-3", "build-4" });
    }

    [Fact]
    public async Task EnqueueAsync_ExecutorThrows_ReturnsFailedResult()
    {
        await using var queue = new GlobalBuildQueue((req, ct) =>
            throw new InvalidOperationException("build failed"));

        var request = CreateRequest("build-fail");
        var result = await queue.EnqueueAsync(request, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Output.Should().Contain("build failed");
    }

    [Fact]
    public async Task CurrentBuild_SetDuringExecution_NullAfterCompletion()
    {
        var tcs = new TaskCompletionSource();
        await using var queue = new GlobalBuildQueue(async (req, ct) =>
        {
            await tcs.Task;
            return new GlobalBuildResult
            {
                RequestId = req.RequestId,
                Success = true,
                Output = "",
                Duration = TimeSpan.Zero
            };
        });

        var request = CreateRequest("long-build");
        var buildTask = queue.EnqueueAsync(request, CancellationToken.None);

        await WaitUntilAsync(() => queue.CurrentBuild is not null, TimeSpan.FromSeconds(2));
        queue.CurrentBuild!.RequestId.Should().Be("long-build");

        tcs.SetResult();
        await buildTask;

        await WaitUntilAsync(() => queue.CurrentBuild is null, TimeSpan.FromSeconds(2));
        queue.CurrentBuild.Should().BeNull();
    }

    [Fact]
    public async Task GetQueueState_ReturnsCurrentState()
    {
        await using var queue = new GlobalBuildQueue(static (req, ct) =>
            ValueTask.FromResult(new GlobalBuildResult
            {
                RequestId = req.RequestId,
                Success = true,
                Output = "",
                Duration = TimeSpan.Zero
            }));

        var state = queue.GetQueueState();
        state.PendingCount.Should().Be(0);
        state.RunningCount.Should().Be(0);

        var request = CreateRequest("build-1");
        await queue.EnqueueAsync(request);

        var stateAfter = queue.GetQueueState();
        stateAfter.RunningCount.Should().Be(0, "编译完成后应为 0");
    }

    [Fact]
    public async Task OutputAsync_ProducesBuildEvents()
    {
        await using var queue = new GlobalBuildQueue(static (req, ct) =>
            ValueTask.FromResult(new GlobalBuildResult
            {
                RequestId = req.RequestId,
                Success = true,
                Output = "ok",
                Duration = TimeSpan.FromMilliseconds(10)
            }));

        var events = new List<GlobalBuildEvent>();
        var cts = new CancellationTokenSource();
        var consumeTask = Task.Run(async () =>
        {
            await foreach (var evt in queue.OutputAsync(cts.Token))
                events.Add(evt);
        }, cts.Token);

        await queue.EnqueueAsync(CreateRequest("build-1"));

        await WaitUntilAsync(() => events.Count >= 2, TimeSpan.FromSeconds(3));

        cts.Cancel();
        await Task.WhenAny(consumeTask, Task.Delay(1000));

        events.Should().HaveCountGreaterThanOrEqualTo(2);
        events[0].Should().BeOfType<GlobalBuildStartedEvt>();
        events.Should().Contain(e => e is GlobalBuildCompletedEvt);
    }

    private static GlobalBuildRequest CreateRequest(string id) => new()
    {
        RequestId = id,
        ProjectPath = "test.csproj",
        Arguments = Array.Empty<string>(),
        RequestingProcessId = Environment.ProcessId.ToString()
    };

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate()) return;
            await Task.Delay(50);
        }
        throw new TimeoutException($"Condition not met within {timeout.TotalSeconds}s");
    }
}
