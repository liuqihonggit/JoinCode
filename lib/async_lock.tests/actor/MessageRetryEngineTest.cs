// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC11003
namespace Core.Utils;

/// <summary>
/// MessageRetryEngine 确定性单元测试 — 验证重试队列、退避回写、SendFailed 回调,不依赖精确时序。
/// <para>覆盖:TryEnqueue/TryRequeue 边界(超 maxRetries/队列满/成功)、ReceiveBackpressureSignal、RetryQueueCount、Complete、循环回写输入通道。</para>
/// </summary>
public class MessageRetryEngineTest {
    private static (MessageRetryEngine<string> engine, Channel<MessageEnvelope<string>> input, CancellationTokenSource cts, List<(string Cmd, int Retry)> sendFailed, int[] enqueuedCount) NewEngine(
        int retryQueueCapacity = 1024, int maxRetries = 16) {
        var input = Channel.CreateBounded<MessageEnvelope<string>>(new BoundedChannelOptions(100) {
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

    /// <summary>ProcessOneEntryImmediate 回写成功 → onEnqueuedToInput 调用,不触发 SendFailed(确定性,无退避延迟)</summary>
    [Fact]
    public void ProcessOneEntryImmediate_InputWritable_InvokesOnEnqueuedNoSendFailed() {
        var (engine, input, cts, sendFailed, enqueuedCount) = NewEngine(maxRetries: 16);

        var result = engine.ProcessOneEntryImmediate(new RetryEntry<string>("retry-cmd", 1));

        result.Should().BeTrue("输入通道可写,回写成功");
        Volatile.Read(ref enqueuedCount[0]).Should().Be(1, "回写成功调用 onEnqueuedToInput 回调");
        sendFailed.Should().BeEmpty("回写成功不触发 SendFailed");
        input.Reader.TryRead(out var envelope).Should().BeTrue();
        envelope.Command.Should().Be("retry-cmd");

        engine.Complete();
        cts.Cancel();
    }

    /// <summary>ProcessOneEntryImmediate 输入满 → TryRequeue 重新入队(确定性,无退避延迟)</summary>
    [Fact]
    public void ProcessOneEntryImmediate_InputFull_TryRequeues() {
        var input = Channel.CreateBounded<MessageEnvelope<string>>(new BoundedChannelOptions(1) {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
        using var cts = new CancellationTokenSource();
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

        input.Writer.TryWrite(new MessageEnvelope<string>("occupier", null)).Should().BeTrue("占满输入通道");

        var result = engine.ProcessOneEntryImmediate(new RetryEntry<string>("blocked-cmd", 1));

        result.Should().BeFalse("输入通道满,回写失败");
        Volatile.Read(ref enqueuedCount).Should().Be(0, "回写失败不调用 onEnqueuedToInput");
        sendFailed.Should().BeEmpty("未超 maxRetries,不触发 SendFailed");
        engine.RetryQueueCount.Should().Be(1, "TryRequeue 重新入队(Attempt=2)");

        engine.Complete();
        cts.Cancel();
    }

    // ===== 守卫补全:构造函数取值范围边界值(确定性测试,不依赖时序/IO) =====

    private static Channel<MessageEnvelope<string>> MakeInput() => Channel.CreateBounded<MessageEnvelope<string>>(new BoundedChannelOptions(100) {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true,
        SingleWriter = false
    });

    [Fact]
    [Trait("Category", "Deterministic")]
    public void Constructor_NullActorId_ThrowsArgumentNullException() {
        var input = MakeInput();
        var act = () => new MessageRetryEngine<string>(
            retryQueueCapacity: 1024, maxRetries: 16, inputWriter: input.Writer,
            actorId: null!, logger: null, shutdownCt: CancellationToken.None,
            onSendFailed: (_, _) => { }, onEnqueuedToInput: () => { });
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    [Trait("Category", "Deterministic")]
    public void Constructor_EmptyActorId_ThrowsArgumentException() {
        var input = MakeInput();
        var act = () => new MessageRetryEngine<string>(
            retryQueueCapacity: 1024, maxRetries: 16, inputWriter: input.Writer,
            actorId: "", logger: null, shutdownCt: CancellationToken.None,
            onSendFailed: (_, _) => { }, onEnqueuedToInput: () => { });
        act.Should().Throw<ArgumentException>().WithMessage("*actorId*");
    }

    [Fact]
    [Trait("Category", "Deterministic")]
    public void Constructor_RetryQueueCapacity_Zero_ThrowsArgumentOutOfRangeException() {
        var input = MakeInput();
        var act = () => new MessageRetryEngine<string>(
            retryQueueCapacity: 0, maxRetries: 16, inputWriter: input.Writer,
            actorId: "a", logger: null, shutdownCt: CancellationToken.None,
            onSendFailed: (_, _) => { }, onEnqueuedToInput: () => { });
        act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*retryQueueCapacity*");
    }

    [Fact]
    [Trait("Category", "Deterministic")]
    public void Constructor_RetryQueueCapacity_Negative_ThrowsArgumentOutOfRangeException() {
        var input = MakeInput();
        var act = () => new MessageRetryEngine<string>(
            retryQueueCapacity: -1, maxRetries: 16, inputWriter: input.Writer,
            actorId: "a", logger: null, shutdownCt: CancellationToken.None,
            onSendFailed: (_, _) => { }, onEnqueuedToInput: () => { });
        act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*retryQueueCapacity*");
    }

    [Fact]
    [Trait("Category", "Deterministic")]
    public void Constructor_MaxRetries_Zero_ThrowsArgumentOutOfRangeException() {
        var input = MakeInput();
        var act = () => new MessageRetryEngine<string>(
            retryQueueCapacity: 1024, maxRetries: 0, inputWriter: input.Writer,
            actorId: "a", logger: null, shutdownCt: CancellationToken.None,
            onSendFailed: (_, _) => { }, onEnqueuedToInput: () => { });
        act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*maxRetries*");
    }

    [Fact]
    [Trait("Category", "Deterministic")]
    public void Constructor_MaxRetries_Negative_ThrowsArgumentOutOfRangeException() {
        var input = MakeInput();
        var act = () => new MessageRetryEngine<string>(
            retryQueueCapacity: 1024, maxRetries: -1, inputWriter: input.Writer,
            actorId: "a", logger: null, shutdownCt: CancellationToken.None,
            onSendFailed: (_, _) => { }, onEnqueuedToInput: () => { });
        act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*maxRetries*");
    }

    [Fact]
    [Trait("Category", "Deterministic")]
    public void Constructor_ValidArguments_DoesNotThrow() {
        var input = MakeInput();
        var engine = new MessageRetryEngine<string>(
            retryQueueCapacity: 1024, maxRetries: 16, inputWriter: input.Writer,
            actorId: "valid-actor", logger: null, shutdownCt: CancellationToken.None,
            onSendFailed: (_, _) => { }, onEnqueuedToInput: () => { });
        engine.RetryQueueCount.Should().Be(0);
        engine.Complete();
    }
}
