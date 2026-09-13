namespace Host.Tests.Queue;

/// <summary>
/// CommandQueue 单元测试 — 验证优先级排序、FIFO、线程安全、快照。
/// </summary>
public class CommandQueueTests
{
    [Fact]
    public void Enqueue_Dequeue_SingleItem_ReturnsItem()
    {
        var queue = new CommandQueue();
        var cmd = new QueuedCommand("hello", CommandOrigin.User, QueuePriority.Next);

        queue.Enqueue(cmd);
        var dequeued = queue.Dequeue();

        Assert.NotNull(dequeued);
        Assert.Equal("hello", dequeued!.Content);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void Dequeue_EmptyQueue_ReturnsNull()
    {
        var queue = new CommandQueue();
        Assert.Null(queue.Dequeue());
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void Dequeue_PriorityOrder_NowBeforeNextBeforeLater()
    {
        var queue = new CommandQueue();
        queue.Enqueue(new QueuedCommand("later", CommandOrigin.TaskNotification, QueuePriority.Later));
        queue.Enqueue(new QueuedCommand("next", CommandOrigin.User, QueuePriority.Next));
        queue.Enqueue(new QueuedCommand("now", CommandOrigin.PermissionResponse, QueuePriority.Now));

        Assert.Equal("now", queue.Dequeue()!.Content);
        Assert.Equal("next", queue.Dequeue()!.Content);
        Assert.Equal("later", queue.Dequeue()!.Content);
    }

    [Fact]
    public void Dequeue_SamePriority_FifoOrder()
    {
        var queue = new CommandQueue();
        queue.Enqueue(new QueuedCommand("first", CommandOrigin.User, QueuePriority.Next));
        queue.Enqueue(new QueuedCommand("second", CommandOrigin.User, QueuePriority.Next));
        queue.Enqueue(new QueuedCommand("third", CommandOrigin.User, QueuePriority.Next));

        Assert.Equal("first", queue.Dequeue()!.Content);
        Assert.Equal("second", queue.Dequeue()!.Content);
        Assert.Equal("third", queue.Dequeue()!.Content);
    }

    [Fact]
    public void Dequeue_MixedPriority_NowAlwaysFirst()
    {
        var queue = new CommandQueue();
        queue.Enqueue(new QueuedCommand("next1", CommandOrigin.User, QueuePriority.Next));
        queue.Enqueue(new QueuedCommand("next2", CommandOrigin.User, QueuePriority.Next));
        queue.Enqueue(new QueuedCommand("now1", CommandOrigin.PermissionResponse, QueuePriority.Now));

        Assert.Equal("now1", queue.Dequeue()!.Content);
        Assert.Equal("next1", queue.Dequeue()!.Content);
        Assert.Equal("next2", queue.Dequeue()!.Content);
    }

    [Fact]
    public void Count_TracksEnqueueAndDequeue()
    {
        var queue = new CommandQueue();
        Assert.Equal(0, queue.Count);

        queue.Enqueue(new QueuedCommand("a", CommandOrigin.User, QueuePriority.Next));
        queue.Enqueue(new QueuedCommand("b", CommandOrigin.User, QueuePriority.Later));
        Assert.Equal(2, queue.Count);

        queue.Dequeue();
        Assert.Equal(1, queue.Count);

        queue.Dequeue();
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void GetSnapshot_CapturesCurrentState()
    {
        var queue = new CommandQueue();
        queue.Enqueue(new QueuedCommand("now-cmd", CommandOrigin.PermissionResponse, QueuePriority.Now));
        queue.Enqueue(new QueuedCommand("next-cmd", CommandOrigin.User, QueuePriority.Next));
        queue.Enqueue(new QueuedCommand("later-cmd", CommandOrigin.TaskNotification, QueuePriority.Later));

        var snapshot = queue.GetSnapshot();

        Assert.Single(snapshot.Now);
        Assert.Single(snapshot.Next);
        Assert.Single(snapshot.Later);
        Assert.Equal(3, snapshot.TotalCount);
        Assert.Equal("now-cmd", snapshot.Now[0].Content);
        Assert.Equal("next-cmd", snapshot.Next[0].Content);
        Assert.Equal("later-cmd", snapshot.Later[0].Content);
    }

    [Fact]
    public async Task ConcurrentEnqueueDequeue_ThreadSafe()
    {
        var queue = new CommandQueue();
        const int itemsPerProducer = 100;
        const int producerCount = 4;

        var producers = Enumerable.Range(0, producerCount).Select(i => Task.Run(() =>
        {
            for (int j = 0; j < itemsPerProducer; j++)
                queue.Enqueue(new QueuedCommand($"p{i}-{j}", CommandOrigin.User, QueuePriority.Next));
        })).ToArray();

        await Task.WhenAll(producers);

        Assert.Equal(producerCount * itemsPerProducer, queue.Count);

        var consumed = 0;
        while (queue.Dequeue() is not null)
            consumed++;

        Assert.Equal(producerCount * itemsPerProducer, consumed);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void TryDequeue_ReturnsTrueWhenNonEmpty()
    {
        var queue = new CommandQueue();
        queue.Enqueue(new QueuedCommand("test", CommandOrigin.User, QueuePriority.Next));

        Assert.True(queue.TryDequeue(out var cmd));
        Assert.Equal("test", cmd.Content);
    }

    [Fact]
    public void TryDequeue_ReturnsFalseWhenEmpty()
    {
        var queue = new CommandQueue();
        Assert.False(queue.TryDequeue(out _));
    }

    [Fact]
    public async Task DequeueAsync_AfterEnqueue_ReturnsItemImmediately()
    {
        var queue = new CommandQueue();
        queue.Enqueue(new QueuedCommand("hello", CommandOrigin.User, QueuePriority.Next));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var dequeued = await queue.DequeueAsync(cts.Token);

        Assert.Equal("hello", dequeued.Content);
        Assert.Equal(CommandOrigin.User, dequeued.Origin);
        Assert.Equal(QueuePriority.Next, dequeued.Priority);
    }

    [Fact]
    public async Task DequeueAsync_EmptyQueue_WaitsUntilEnqueueThenReturns()
    {
        var queue = new CommandQueue();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var dequeueTask = queue.DequeueAsync(cts.Token);
        Assert.False(dequeueTask.IsCompleted);

        await Task.Delay(50, cts.Token);
        queue.Enqueue(new QueuedCommand("late", CommandOrigin.User, QueuePriority.Next));

        var dequeued = await dequeueTask;
        Assert.Equal("late", dequeued.Content);
    }

    [Fact]
    public async Task DequeueAsync_PriorityOrder_NowBeforeNextBeforeLater()
    {
        var queue = new CommandQueue();
        queue.Enqueue(new QueuedCommand("later", CommandOrigin.TaskNotification, QueuePriority.Later));
        queue.Enqueue(new QueuedCommand("next", CommandOrigin.User, QueuePriority.Next));
        queue.Enqueue(new QueuedCommand("now", CommandOrigin.PermissionResponse, QueuePriority.Now));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Assert.Equal("now", (await queue.DequeueAsync(cts.Token)).Content);
        Assert.Equal("next", (await queue.DequeueAsync(cts.Token)).Content);
        Assert.Equal("later", (await queue.DequeueAsync(cts.Token)).Content);
    }

    [Fact]
    public async Task DequeueAsync_Cancellation_ThrowsOperationCanceledException()
    {
        var queue = new CommandQueue();
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAsync<OperationCanceledException>(() => queue.DequeueAsync(cts.Token));
    }

    [Fact]
    public async Task DequeueAsync_MultipleWaiters_EachReceivesOneItem()
    {
        var queue = new CommandQueue();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var waiter1 = queue.DequeueAsync(cts.Token);
        var waiter2 = queue.DequeueAsync(cts.Token);

        queue.Enqueue(new QueuedCommand("a", CommandOrigin.User, QueuePriority.Next));
        queue.Enqueue(new QueuedCommand("b", CommandOrigin.User, QueuePriority.Next));

        var results = await Task.WhenAll(waiter1, waiter2);
        var contents = results.Select(r => r.Content).ToHashSet();
        Assert.Equal(2, contents.Count);
        Assert.Contains("a", contents);
        Assert.Contains("b", contents);
    }
}
