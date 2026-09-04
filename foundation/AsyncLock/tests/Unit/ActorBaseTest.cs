namespace Core.Utils;

/// <summary>
/// ActorBase 单元测试 — 验证命令串行处理、异常容错、生命周期、背压。
/// </summary>
public class ActorBaseTest
{
    [Fact]
    public async Task SendAsync_CommandProcessed_ReturnsResult()
    {
        await using var actor = new TestActor();
        var tcs = new TaskCompletionSource<int>();
        await actor.IncrementAsync(tcs);
        (await tcs.Task).Should().Be(1);
    }

    [Fact]
    public async Task TrySend_CommandProcessed_ReturnsTrue()
    {
        await using var actor = new TestActor();
        var tcs = new TaskCompletionSource<int>();
        actor.TryIncrement(tcs).Should().BeTrue();
        (await tcs.Task).Should().Be(1);
    }

    [Fact]
    public async Task MultipleCommands_ProcessedSerially_ValueIncrements()
    {
        await using var actor = new TestActor();
        var tasks = new List<Task<int>>();
        for (var i = 0; i < 100; i++)
        {
            var tcs = new TaskCompletionSource<int>();
            await actor.IncrementAsync(tcs);
            tasks.Add(tcs.Task);
        }

        var results = await Task.WhenAll(tasks);
        results.Should().BeInAscendingOrder();
        results.Should().HaveCount(100);
        results[^1].Should().Be(100);
    }

    [Fact]
    public async Task ConcurrentSend_AllCommandsProcessed_NoLoss()
    {
        await using var actor = new TestActor();
        const int count = 500;
        var tcsArray = new TaskCompletionSource<int>[count];
        for (var i = 0; i < count; i++)
            tcsArray[i] = new TaskCompletionSource<int>();

        await Task.WhenAll(Enumerable.Range(0, count).Select(async i =>
        {
            await actor.IncrementAsync(tcsArray[i]);
        }));

        var results = await Task.WhenAll(tcsArray.Select(t => t.Task));
        var sorted = results.Order().ToList();
        sorted.Should().HaveCount(count);
        sorted[0].Should().Be(1);
        sorted[^1].Should().Be(count);
    }

    [Fact]
    public async Task CommandThrows_ConsumerContinues_NextCommandSucceeds()
    {
        await using var actor = new TestActor();
        var throwTcs = new TaskCompletionSource();
        await actor.ThrowAsync(throwTcs);
        await Assert.ThrowsAsync<InvalidOperationException>(() => throwTcs.Task);

        var tcs = new TaskCompletionSource<int>();
        await actor.IncrementAsync(tcs);
        (await tcs.Task).Should().Be(1);

        actor.ErrorCount.Should().Be(1);
    }

    [Fact]
    public async Task DisposeAsync_CompletesChannel_TrySendReturnsFalse()
    {
        var actor = new TestActor();
        await actor.DisposeAsync();

        var tcs = new TaskCompletionSource<int>();
        actor.TryIncrement(tcs).Should().BeFalse();
    }

    [Fact]
    public async Task SendAsync_AfterDispose_ThrowsObjectDisposed()
    {
        var actor = new TestActor();
        await actor.DisposeAsync();

        var tcs = new TaskCompletionSource<int>();
        var act = async () => await actor.IncrementAsync(tcs);
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task GetValueQuery_ReturnsCurrentValue()
    {
        await using var actor = new TestActor();
        var incTcs1 = new TaskCompletionSource<int>();
        await actor.IncrementAsync(incTcs1);
        await incTcs1.Task;

        var incTcs2 = new TaskCompletionSource<int>();
        await actor.IncrementAsync(incTcs2);
        await incTcs2.Task;

        var queryTcs = new TaskCompletionSource<int>();
        await actor.GetValueAsync(queryTcs);
        (await queryTcs.Task).Should().Be(2);
    }

    [Fact]
    public async Task BoundedChannel_ProcessesAllCommandsNoLoss()
    {
        await using var actor = new TestActor(boundedCapacity: 4);
        var tasks = new List<Task<int>>();
        for (var i = 0; i < 100; i++)
        {
            var tcs = new TaskCompletionSource<int>();
            await actor.IncrementAsync(tcs);
            tasks.Add(tcs.Task);
        }

        var results = await Task.WhenAll(tasks);
        results.Should().HaveCount(100);
        results.Order().Last().Should().Be(100);
    }

    [Fact]
    public async Task DisposeAsync_WaitsForConsumerExit()
    {
        var actor = new TestActor();
        var tcs = new TaskCompletionSource<int>();
        await actor.IncrementAsync(tcs);
        await tcs.Task;

        await actor.DisposeAsync();
        actor.ConsumerTask.IsCompleted.Should().BeTrue();
    }
}

/// <summary>
/// 测试用 Actor — 计数器,验证命令串行处理与异常容错。
/// </summary>
internal sealed class TestActor : ActorBase<TestActor.ICommand>
{
    internal interface ICommand;

    internal sealed record IncrementCommand(TaskCompletionSource<int> Tcs) : ICommand;
    internal sealed record GetValueQuery(TaskCompletionSource<int> Tcs) : ICommand;
    internal sealed record ThrowCommand(TaskCompletionSource TaskCompletionSource) : ICommand;

    private int _value;
    private int _errorCount;

    public int ErrorCount => _errorCount;

    public TestActor(int? boundedCapacity = null) : base(boundedCapacity) { }

    protected override ValueTask HandleAsync(ICommand command, CancellationToken ct)
    {
        switch (command)
        {
            case IncrementCommand(var tcs):
                _value++;
                tcs.SetResult(_value);
                return ValueTask.CompletedTask;
            case GetValueQuery(var tcs):
                tcs.SetResult(_value);
                return ValueTask.CompletedTask;
            case ThrowCommand(var tcs):
                _errorCount++;
                tcs.SetException(new InvalidOperationException("test error"));
                return ValueTask.CompletedTask;
            default:
                return ValueTask.CompletedTask;
        }
    }

    public ValueTask IncrementAsync(TaskCompletionSource<int> tcs) => SendAsync(new IncrementCommand(tcs));
    public bool TryIncrement(TaskCompletionSource<int> tcs) => TrySend(new IncrementCommand(tcs));
    public ValueTask GetValueAsync(TaskCompletionSource<int> tcs) => SendAsync(new GetValueQuery(tcs));
    public ValueTask ThrowAsync(TaskCompletionSource tcs) => SendAsync(new ThrowCommand(tcs));
}
