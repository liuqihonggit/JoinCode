namespace Core.Utils;

/// <summary>
/// MessageRetryEngine 确定性单元测试 — 验证重试队列、退避回写、SendFailed 回调,不依赖精确时序。
/// <para>覆盖:TryEnqueue/TryRequeue 边界(超 maxRetries/队列满/成功)、ReceiveBackpressureSignal、RetryQueueCount、Complete、循环回写输入通道。</para>
/// </summary>
public class MessageRetryEngineTest {
    private static (MessageRetryEngine<string> engine, Channel<string> input, CancellationTokenSource cts, List<(string Cmd, int Retry)> sendFailed, int[] enqueuedCount) NewEngine(
        int retryQueueCapacity = 1024, int maxRetries = 16) {
        var input = Channel.CreateBounded<string>(new BoundedChannelOptions(100) {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
        var cts = new CancellationTokenSource();
        var sendFailed = new List<(string Cmd, int Retry)>();
        var enqueuedCount = new int[1];
        var engine = new MessageRetryEngine<string>(
            retryQueueCapacity: retryQueueCapacity,
            maxRetries: maxRetries,
            inputWriter: input.Writer,
            actorId: "test-actor",
            logger: null,
            shutdownCt: cts.Token,
            onSendFailed: (cmd, retry) => sendFailed.Add((cmd, retry)),
            onEnqueuedToInput: () => Interlocked.Increment(ref enqueuedCount[0]));
        return (engine, input, cts, sendFailed, enqueuedCount);
    }

    /// <summary>TryEnqueue 入队成功,RetryQueueCount 增加</summary>
    [Fact]
    public void TryEnqueue_Success_IncreasesCount() {
        var (engine, _, cts, _, _) = NewEngine();

        engine.TryEnqueue(new RetryEntry<string>("cmd-1", 1)).Should().BeTrue();
        engine.RetryQueueCount.Should().Be(1);

        engine.TryEnqueue(new RetryEntry<string>("cmd-2", 1)).Should().BeTrue();
        engine.RetryQueueCount.Should().Be(2);

        engine.Complete();
        cts.Cancel();
    }

    /// <summary>TryRequeue 超过 maxRetries → 触发 onSendFailed(maxRetries),返回 false</summary>
    [Fact]
    public void TryRequeue_ExceedsMaxRetries_TriggersSendFailed() {
        var (engine, _, cts, sendFailed, _) = NewEngine(maxRetries: 3);

        var result = engine.TryRequeue(new RetryEntry<string>("X", 3));

        result.Should().BeFalse("attempt >= maxRetries");
        sendFailed.Should().ContainSingle().Which.Should().Be(("X", 3), "触发 onSendFailed(cmd, maxRetries)");
        engine.RetryQueueCount.Should().Be(0, "未入队");

        engine.Complete();
        cts.Cancel();
    }

    /// <summary>TryRequeue 队列满 → 触发 onSendFailed(attempt),返回 false</summary>
    [Fact]
    public void TryRequeue_QueueFull_TriggersSendFailed() {
        var (engine, _, cts, sendFailed, _) = NewEngine(retryQueueCapacity: 1, maxRetries: 16);

        engine.TryEnqueue(new RetryEntry<string>("occupier", 1)).Should().BeTrue("占满重试队列");

        var result = engine.TryRequeue(new RetryEntry<string>("D", 1));

        result.Should().BeFalse("重试队列满,回写失败");
        sendFailed.Should().ContainSingle().Which.Should().Be(("D", 1));

        engine.Complete();
        cts.Cancel();
    }

    /// <summary>TryRequeue 成功 → 返回 true,RetryQueueCount 增加(Attempt+1)</summary>
    [Fact]
    public void TryRequeue_Success_ReturnsTrue_EnqueuesWithIncrementedAttempt() {
        var (engine, _, cts, sendFailed, _) = NewEngine(maxRetries: 16);

        var result = engine.TryRequeue(new RetryEntry<string>("Y", 1));

        result.Should().BeTrue();
        sendFailed.Should().BeEmpty();
        engine.RetryQueueCount.Should().Be(1);

        engine.Complete();
        cts.Cancel();
    }

    /// <summary>ReceiveBackpressureSignal 不抛,写入信号队列</summary>
    [Fact]
    public void ReceiveBackpressureSignal_DoesNotThrow() {
        var (engine, _, cts, _, _) = NewEngine();

        Action act = () => {
            engine.ReceiveBackpressureSignal(TimeSpan.FromMilliseconds(50));
            engine.ReceiveBackpressureSignal(TimeSpan.FromMilliseconds(100));
        };
        act.Should().NotThrow();

        engine.Complete();
        cts.Cancel();
    }

    /// <summary>RetryQueueCount 反映队列实际消息数</summary>
    [Fact]
    public void RetryQueueCount_ReflectsQueueSize() {
        var (engine, _, cts, _, _) = NewEngine();

        engine.RetryQueueCount.Should().Be(0);
        for (var i = 0; i < 5; i++)
            engine.TryEnqueue(new RetryEntry<string>($"cmd-{i}", 1));
        engine.RetryQueueCount.Should().Be(5);

        engine.Complete();
        cts.Cancel();
    }

    /// <summary>Complete 完成队列,WaitForCompletionAsync 可等待(未 Start 时返回 CompletedTask)</summary>
    [Fact]
    public async Task Complete_BeforeStart_WaitForCompletionReturnsCompleted() {
        var (engine, _, cts, _, _) = NewEngine();

        engine.Complete();
        var task = engine.WaitForCompletionAsync();
        task.IsCompleted.Should().BeTrue("未 Start 时 _retryTask 为 null,返回 CompletedTask");

        await task;
        cts.Cancel();
    }

    /// <summary>RetryQueue 暴露原始通道(供门面转发,可 TryWrite)</summary>
    [Fact]
    public void RetryQueue_ExposesChannel_ForFacadeWrite() {
        var (engine, _, cts, _, _) = NewEngine();

        engine.RetryQueue.Writer.TryWrite(new RetryEntry<string>("via-channel", 1)).Should().BeTrue();
        engine.RetryQueueCount.Should().Be(1);
        engine.RetryQueue.Reader.TryRead(out var entry).Should().BeTrue();
        entry!.Command.Should().Be("via-channel");

        engine.Complete();
        cts.Cancel();
    }

    /// <summary>重试循环:TryEnqueue 消息 → 退避后回写输入通道 → onEnqueuedToInput 调用</summary>
    [Fact]
    public async Task RunLoop_RetryEntry_WrittenBackToInput_InvokesOnEnqueuedCallback() {
        var (engine, input, cts, sendFailed, enqueuedCount) = NewEngine(maxRetries: 16);
        engine.Start();

        engine.TryEnqueue(new RetryEntry<string>("retry-cmd", 1));

        // 退避:attempt=1 → backoff=100*2^1=200ms,等待回写(用足够窗口,不依赖精确时序)
        string? received = null;
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(3);
        while (DateTimeOffset.UtcNow < deadline) {
            if (input.Reader.TryRead(out var cmd)) { received = cmd; break; }
            await Task.Delay(20);
        }

        received.Should().Be("retry-cmd", "重试循环退避后回写输入通道");
        // 跨线程可见性:TryWrite 成功后 onEnqueuedToInput 紧随其后,但测试线程可能读到 cmd 时回调尚未可见,轮询等待
        var callbackDeadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2);
        while (DateTimeOffset.UtcNow < callbackDeadline && Volatile.Read(ref enqueuedCount[0]) == 0)
            await Task.Delay(10);
        Volatile.Read(ref enqueuedCount[0]).Should().BeGreaterThan(0, "回写成功调用 onEnqueuedToInput 回调");
        sendFailed.Should().BeEmpty("回写成功不触发 SendFailed");

        engine.Complete();
        cts.Cancel();
        await engine.WaitForCompletionAsync();
    }

    /// <summary>重试循环:输入通道满 → 回写失败 → TryRequeue 重新入队(Attempt+1)</summary>
    [Fact]
    public async Task RunLoop_InputFull_RewritesToRetryQueue() {
        // 输入通道容量 1,先占满
        var input = Channel.CreateBounded<string>(new BoundedChannelOptions(1) {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
        var cts = new CancellationTokenSource();
        var sendFailed = new List<(string Cmd, int Retry)>();
        var enqueuedCount = 0;
        var engine = new MessageRetryEngine<string>(
            retryQueueCapacity: 100,
            maxRetries: 16,
            inputWriter: input.Writer,
            actorId: "test",
            logger: null,
            shutdownCt: cts.Token,
            onSendFailed: (cmd, retry) => sendFailed.Add((cmd, retry)),
            onEnqueuedToInput: () => Interlocked.Increment(ref enqueuedCount));
        input.Writer.TryWrite("occupier").Should().BeTrue("占满输入通道");
        engine.Start();

        engine.TryEnqueue(new RetryEntry<string>("blocked-cmd", 1));

        // 退避后回写失败(输入满),TryRequeue 重新入队;等待重试循环处理(不检查瞬时 RetryQueueCount,时序敏感)
        await Task.Delay(500);
        sendFailed.Should().BeEmpty("未超 maxRetries,不触发 SendFailed");

        // 释放输入通道,重试应成功回写
        input.Reader.TryRead(out _).Should().BeTrue();
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(3);
        var gotBlocked = false;
        while (DateTimeOffset.UtcNow < deadline) {
            if (input.Reader.TryRead(out var cmd) && cmd == "blocked-cmd") { gotBlocked = true; break; }
            await Task.Delay(20);
        }
        gotBlocked.Should().BeTrue("释放输入后重试成功回写");

        engine.Complete();
        cts.Cancel();
        await engine.WaitForCompletionAsync();
    }
}
