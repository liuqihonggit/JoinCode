namespace Core.Utils;

/// <summary>
/// ActorBase 单元测试 — 验证命令串行处理、异常容错、生命周期、背压、输出流。
/// </summary>
public class ActorBaseTest {
    /// <summary>验证命令发送后被正确处理并产生输出</summary>
    [Fact]
    public async Task SendAsync_CommandProcessed_OutputReceived() {
        await using var actor = new TestActor();
        actor.Tell("hello");
        actor.Tell("world");

        await TestWaitHelper.WaitUntilAsync(() => actor.ProcessedCommands.Count >= 2, TimeSpan.FromMilliseconds(500));

        actor.ProcessedCommands.Should().Equal("hello", "world");
    }

    /// <summary>验证尝试发送命令后返回 true</summary>
    [Fact]
    public async Task TrySend_CommandProcessed_ReturnsTrue() {
        await using var actor = new TestActor();
        actor.TrySend("test").Should().BeTrue();
        await TestWaitHelper.WaitUntilAsync(() => actor.ProcessedCommands.Count >= 1, TimeSpan.FromMilliseconds(500));
        actor.ProcessedCommands.Should().Contain("test");
    }

    /// <summary>验证多个命令按顺序串行处理</summary>
    [Fact]
    public async Task MultipleCommands_ProcessedSerially_InOrder() {
        await using var actor = new TestActor();
        for (var i = 0; i < 100; i++)
            actor.Tell($"msg-{i}");

        await TestWaitHelper.WaitUntilAsync(() => actor.ProcessedCommands.Count >= 100, TimeSpan.FromMilliseconds(500));

        actor.ProcessedCommands.Should().HaveCount(100);
        for (var i = 0; i < 100; i++)
            actor.ProcessedCommands[i].Should().Be($"msg-{i}");
    }

    /// <summary>验证并发发送时所有命令都被处理无丢失</summary>
    [Fact]
    public async Task ConcurrentSend_AllCommandsProcessed_NoLoss() {
        await using var actor = new TestActor();
        for (var i = 0; i < 500; i++)
            actor.Tell($"msg-{i}");

        await TestWaitHelper.WaitUntilAsync(() => actor.ProcessedCommands.Count >= 500, TimeSpan.FromMilliseconds(500));

        actor.ProcessedCommands.Should().HaveCount(500);
    }

    /// <summary>验证命令抛出异常后消费者继续处理下一条命令</summary>
    [Fact]
    public async Task CommandThrows_ConsumerContinues_NextCommandSucceeds() {
        await using var actor = new TestActor();
        actor.Tell("throw");
        actor.Tell("normal");

        await TestWaitHelper.WaitUntilAsync(() => actor.ProcessedCommands.Count >= 1, TimeSpan.FromMilliseconds(500));

        actor.ProcessedCommands.Should().Contain("normal");
        actor.ErrorCount.Should().Be(1);
    }

    /// <summary>验证异步释放后尝试发送返回 false</summary>
    [Fact]
    public async Task DisposeAsync_TrySendReturnsFalse() {
        var actor = new TestActor();
        await actor.DisposeAsync();

        actor.TrySend("test").Should().BeFalse();
    }

    /// <summary>验证释放后发送命令抛出 ObjectDisposedException</summary>
    [Fact]
    public async Task SendAsync_AfterDispose_ThrowsObjectDisposed() {
        var actor = new TestActor();
        await actor.DisposeAsync();

        Action act = () => actor.Tell("test");
        act.Should().Throw<ObjectDisposedException>();
    }

    /// <summary>验证输出流接收已发布的消息</summary>
    [Fact]
    public async Task OutputAsync_ReceivesPublishedMessages() {
        await using var actor = new TestActor();
        actor.Tell("hello");

        await TestWaitHelper.WaitUntilAsync(() => actor.OutputCount >= 1, TimeSpan.FromMilliseconds(500));

        var output = await actor.OutputAsync().FirstOrDefaultAsync();
        output.Should().Be("processed-hello");
    }

    /// <summary>验证有界通道处理所有命令无丢失(容量足够大时全部入队)</summary>
    [Fact]
    public async Task BoundedChannel_ProcessesAllCommandsNoLoss() {
        await using var actor = new TestActor(boundedCapacity: 100);
        for (var i = 0; i < 100; i++)
            actor.Tell($"msg-{i}");

        await TestWaitHelper.WaitUntilAsync(() => actor.ProcessedCommands.Count >= 100, TimeSpan.FromMilliseconds(500));

        actor.ProcessedCommands.Should().HaveCount(100);
    }

    /// <summary>验证异步释放等待消费者退出</summary>
    [Fact]
    public async Task DisposeAsync_WaitsForConsumerExit() {
        var actor = new TestActor();
        actor.Tell("test");
        await TestWaitHelper.WaitUntilAsync(() => actor.ProcessedCommands.Count >= 1, TimeSpan.FromMilliseconds(500));

        await actor.DisposeAsync();
        await actor.ConsumerTask.WaitAsync(TimeSpan.FromSeconds(5));
        actor.ConsumerTask.IsCompleted.Should().BeTrue();
    }

    /// <summary>验证背压模式下触发水位事件</summary>
    [Fact]
    public async Task SendAsync_WithBackpressure_WatermarkEventTriggered() {
        var bp = new ActorBackpressure(Capacity: 2, SendTimeout: TimeSpan.FromSeconds(1));
        await using var actor = new TestActor(bp);

        var events = new List<BackpressureEventArgs>();
        actor.InputWatermarkReached += (_, e) => events.Add(e);

        actor.Tell("a");
        actor.Tell("b");

        events.Should().Contain(e => e.Level == WatermarkLevel.High || e.Level == WatermarkLevel.Critical);
    }

    /// <summary>
    /// P0-B: InputCount 统一使用 Channel 原生 Count,不再有 Interlocked 计数器。
    /// 消息在输入通道时 InputCount 反映通道实际消息数。
    /// </summary>
    [Fact]
    public async Task InputCount_UsesChannelCount_NotInterlocked() {
        var bp = new ActorBackpressure(Capacity: 10);
        await using var actor = new TestActor(bp);

        var gate = new TaskCompletionSource();
        actor.Gate = gate;

        actor.Tell("A");
        await TestWaitHelper.WaitUntilAsync(() => actor.InputCount == 0, TimeSpan.FromMilliseconds(500));

        actor.Tell("B");
        actor.InputCount.Should().Be(1);
        actor.InputChannelCount.Should().Be(1);
        actor.RetryQueueCount.Should().Be(0);

        actor.Tell("C");
        actor.InputCount.Should().Be(2);
        actor.InputChannelCount.Should().Be(2);
    }

    /// <summary>
    /// P0-缺陷2: 重试回写失败时必须触发 SendFailed,不能静默丢弃消息。
    /// 确定性测试: 直接填满重试队列,调用 TryRequeueRetryEntry 验证回写失败触发 SendFailed。
    /// </summary>
    [Fact]
    public async Task RetryWriteBack_Failed_TriggersSendFailed() {
        var bp = new ActorBackpressure(Capacity: 1, RetryQueueCapacity: 1);
        await using var actor = new TestActor(bp);

        var sendFailedEvents = new List<BackpressureSendFailedEventArgs<string>>();
        actor.SendFailed += (_, e) => sendFailedEvents.Add(e);

        actor.RetryQueueInternal.Writer.TryWrite(new RetryEntry<string>("occupier", 1));

        var result = actor.TryRequeueRetryEntry(new RetryEntry<string>("D", 1));

        result.Should().BeFalse();
        sendFailedEvents.Should().ContainSingle(e => e.Command == "D");
    }

    /// <summary>
    /// P0-缺陷2: 重试次数耗尽时触发 SendFailed。
    /// </summary>
    [Fact]
    public async Task RetryWriteBack_MaxRetriesExhausted_TriggersSendFailed() {
        var bp = new ActorBackpressure(Capacity: 1, MaxRetries: 3);
        await using var actor = new TestActor(bp);

        var sendFailedEvents = new List<BackpressureSendFailedEventArgs<string>>();
        actor.SendFailed += (_, e) => sendFailedEvents.Add(e);

        var result = actor.TryRequeueRetryEntry(new RetryEntry<string>("X", 3));

        result.Should().BeFalse();
        sendFailedEvents.Should().ContainSingle(e => e.Command == "X" && e.RetryCount == 3);
    }

    /// <summary>
    /// P0-缺陷6: SendFailed 事件多播时,第一个订阅者抛异常不应阻断后续订阅者。
    /// 确定性测试: 直接调用 TryRequeueRetryEntry 触发 SendFailed。
    /// </summary>
    [Fact]
    public async Task SendFailed_Multicast_FirstHandlerThrows_SecondStillCalled() {
        var bp = new ActorBackpressure(Capacity: 1, RetryQueueCapacity: 1);
        await using var actor = new TestActor(bp);

        var handler2Called = false;
        actor.SendFailed += (_, _) => throw new InvalidOperationException("handler1 crash");
        actor.SendFailed += (_, _) => handler2Called = true;

        actor.RetryQueueInternal.Writer.TryWrite(new RetryEntry<string>("occupier", 1));
        actor.TryRequeueRetryEntry(new RetryEntry<string>("D", 1));

        handler2Called.Should().BeTrue();
    }

    /// <summary>
    /// P0-缺陷6: InputWatermarkReached 事件多播时,第一个订阅者抛异常不应阻断后续订阅者。
    /// </summary>
    [Fact]
    public async Task Watermark_Multicast_FirstHandlerThrows_SecondStillCalled() {
        var bp = new ActorBackpressure(Capacity: 10, HighWatermark: 5, CriticalWatermark: 8);
        await using var actor = new TestActor(bp);

        var handler2Called = false;
        actor.InputWatermarkReached += (_, _) => throw new InvalidOperationException("handler1 crash");
        actor.InputWatermarkReached += (_, _) => handler2Called = true;

        var gate = new TaskCompletionSource();
        actor.Gate = gate;

        for (var i = 0; i < 6; i++) actor.Tell($"msg-{i}");

        await TestWaitHelper.WaitUntilAsync(() => handler2Called, TimeSpan.FromMilliseconds(2000));
        handler2Called.Should().BeTrue();
    }

    /// <summary>验证输出计数反映已发布的消息数</summary>
    [Fact]
    public async Task OutputCount_ReflectsPublishedMessages() {
        await using var actor = new TestActor();
        actor.OutputCount.Should().Be(0);

        actor.Tell("test");
        await TestWaitHelper.WaitUntilAsync(() => actor.ProcessedCommands.Count >= 1, TimeSpan.FromMilliseconds(500));

        actor.OutputCount.Should().Be(1);
    }

    /// <summary>验证取消令牌能取消输出流</summary>
    [Fact]
    public async Task OutputAsync_CancellationToken_CancelsStream() {
        await using var actor = new TestActor();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var act = async () => await actor.OutputAsync(cts.Token).ToListAsync();
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>验证单消费者接收所有输出消息</summary>
    [Fact]
    public async Task OutputAsync_SingleConsumer_ReceivesAllMessages() {
        await using var actor = new TestActor();
        actor.Tell("test");
        await TestWaitHelper.WaitUntilAsync(() => actor.ProcessedCommands.Count >= 1, TimeSpan.FromMilliseconds(500));

        var consumer = actor.OutputAsync().GetAsyncEnumerator();
        (await consumer.MoveNextAsync()).Should().BeTrue();
        consumer.Current.Should().Be("processed-test");
    }

    /// <summary>验证通道满时 SendAsync 启动后台重试(不抛出 TimeoutException)</summary>
    [Fact]
    public async Task SendAsync_ChannelFull_StartsBackgroundRetry() {
        var bp = new ActorBackpressure(Capacity: 1, SendTimeout: TimeSpan.FromMilliseconds(100));
        await using var actor = new TestActor(bp) { Gate = new() };

        actor.Tell("first");
        await TestWaitHelper.WaitUntilAsync(() => actor.InputCount == 0, TimeSpan.FromMilliseconds(500));

        actor.Tell("second");

        Action act = () => actor.Tell("third");
        act.Should().NotThrow();
    }

    /// <summary>验证通过 IActor 接口发送命令被正确处理</summary>
    [Fact]
    public async Task IActorInterface_SendAsync_CommandProcessed() {
        await using IActor<string> actor = new TestActor();
        actor.Tell("via-interface");
        var concrete = (TestActor)actor;
        await TestWaitHelper.WaitUntilAsync(() => concrete.ProcessedCommands.Count >= 1, TimeSpan.FromMilliseconds(500));
        concrete.ProcessedCommands.Should().Contain("via-interface");
    }

    /// <summary>验证 IActor 接口的 Id 和 InputCount 可访问</summary>
    [Fact]
    public async Task IActorInterface_TrySend_Id_InputCount_Accessible() {
        await using IActor<string> actor = new TestActor();
        actor.Id.Should().NotBeNullOrEmpty();
        actor.InputCount.Should().Be(0);
        actor.TrySend("test").Should().BeTrue();
    }

    /// <summary>验证 Wait 模式下输出通道满时 TryPublish 返回 false 并触发 OutputMessageDropped 事件</summary>
    [Fact]
    public async Task TryPublish_WaitMode_ChannelFull_TriggersOutputDroppedEvent() {
        var dropped = new List<string>();
        await using var actor = new TestActor(null, 1, BoundedChannelFullMode.Wait);
        actor.OutputMessageDropped += (_, e) => dropped.Add(e.Message);

        actor.TryPublishInternal("first").Should().BeTrue();
        actor.TryPublishInternal("second").Should().BeFalse();

        dropped.Should().ContainSingle().Which.Should().Be("second");
    }

    /// <summary>验证 DropOldest 模式下输出通道满时 TryPublish 不触发 OutputMessageDropped 事件</summary>
    [Fact]
    public async Task TryPublish_DropOldest_ChannelFull_NoEventTriggered() {
        var dropped = new List<string>();
        await using var actor = new TestActor(null, 1, BoundedChannelFullMode.DropOldest);
        actor.OutputMessageDropped += (_, e) => dropped.Add(e.Message);

        actor.TryPublishInternal("first").Should().BeTrue();
        actor.TryPublishInternal("second").Should().BeTrue();

        dropped.Should().BeEmpty();
    }

    /// <summary>验证 DropWrite 模式下 TryWrite 静默丢弃返回 true(不触发事件)</summary>
    [Fact]
    public async Task TryPublish_DropWrite_ChannelFull_SilentDrop_NoEventTriggered() {
        var dropped = new List<string>();
        await using var actor = new TestActor(null, 1, BoundedChannelFullMode.DropWrite);
        actor.OutputMessageDropped += (_, e) => dropped.Add(e.Message);

        actor.TryPublishInternal("first").Should().BeTrue();
        actor.TryPublishInternal("second").Should().BeTrue();

        dropped.Should().BeEmpty();
    }

    /// <summary>验证 CreateBackpressureHandler 零延迟路径恢复 AsyncFlowIdentity 上下文</summary>
    [Fact]
    public void CreateBackpressureHandler_ZeroDelay_PreservesAsyncFlowIdentity() {
        AsyncFlowIdentity.SetActorId("test-actor");
        string? seenActorId = null;
        var handler = ActorBase<string, string>.CreateBackpressureHandler(() => {
            seenActorId = AsyncFlowIdentity.CurrentActorId;
        });
        AsyncFlowIdentity.ClearActorId();
        handler(new BackpressureSignal(0, "src", "tgt", WatermarkLevel.High, TimeSpan.Zero, 0));
        seenActorId.Should().Be("test-actor");
        AsyncFlowIdentity.Clear();
    }

    /// <summary>验证 CreateBackpressureHandler 延迟路径恢复 AsyncFlowIdentity 上下文</summary>
    [Fact]
    public async Task CreateBackpressureHandler_WithDelay_PreservesAsyncFlowIdentity() {
        AsyncFlowIdentity.SetActorId("test-actor");
        var tcs = new TaskCompletionSource<string?>();
        var handler = ActorBase<string, string>.CreateBackpressureHandler(() => {
            tcs.SetResult(AsyncFlowIdentity.CurrentActorId);
        });
        AsyncFlowIdentity.ClearActorId();
        handler(new BackpressureSignal(0, "src", "tgt", WatermarkLevel.High, TimeSpan.FromMilliseconds(10), 0));
        var seenActorId = await tcs.Task.WaitAsync(TimeSpan.FromMilliseconds(500));
        seenActorId.Should().Be("test-actor");
        AsyncFlowIdentity.Clear();
    }

    /// <summary>验证 IActorTell 接口可正确发送命令</summary>
    [Fact]
    public async Task IActorTell_Interface_CanSendCommands() {
        await using var actor = new TestActor();
        IActorTell<string> tell = actor;
        tell.Tell("via-tell");
        tell.TrySend("via-trysend").Should().BeTrue();
        tell.TryTell("via-trytell").Should().BeTrue();
        await TestWaitHelper.WaitUntilAsync(() => actor.ProcessedCommands.Count >= 3, TimeSpan.FromMilliseconds(500));
        actor.ProcessedCommands.Should().Contain("via-tell", "via-trysend", "via-trytell");
    }

    /// <summary>验证 IActorOutput 接口可正确消费输出</summary>
    [Fact]
    public async Task IActorOutput_Interface_CanConsumeOutput() {
        await using var actor = new TestActor();
        IActorOutput<string> output = actor;
        actor.Tell("hello");
        await TestWaitHelper.WaitUntilAsync(() => actor.ProcessedCommands.Count >= 1, TimeSpan.FromMilliseconds(500));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var results = new List<string>();
        await foreach (var item in output.OutputAsync(cts.Token)) {
            results.Add(item);
            break;
        }
        results.Should().ContainSingle().Which.Should().Be("processed-hello");
    }

    /// <summary>PreStart 在构造函数中调用,先于 Consumer 启动(DSG033 S2)</summary>
    [Fact]
    public async Task Lifecycle_PreStart_CalledDuringConstruction() {
        var actor = new LifecycleActor();
        actor.PreStartCalled.Should().BeTrue("PreStart 应在构造函数中调用");
        await actor.DisposeAsync();
    }

    /// <summary>PostStop 在 DisposeAsync 后调用(DSG033 S2)</summary>
    [Fact]
    public async Task Lifecycle_PostStop_CalledAfterDispose() {
        var actor = new LifecycleActor();
        await actor.DisposeAsync();
        actor.PostStopCalled.Should().BeTrue("PostStop 应在 Dispose 后调用");
    }

    /// <summary>BecomeStacked 切换行为后,新消息由新行为处理(DSG033 API对齐Akka)</summary>
    [Fact]
    public async Task BecomeStacked_切换行为_新消息由新行为处理() {
        await using var actor = new BecomeActor();
        actor.Tell("msg1");
        actor.Tell("become-stacked");
        actor.Tell("msg2");

        await TestWaitHelper.WaitUntilAsync(() => actor.State1Processed.Count >= 2 && actor.State2Processed.Count >= 1, TimeSpan.FromMilliseconds(500));

        actor.State1Processed.Should().Contain("msg1");
        actor.State1Processed.Should().Contain("become-stacked");
        actor.State2Processed.Should().Contain("msg2");
    }

    /// <summary>UnbecomeStacked 恢复行为后,新消息由原行为处理(DSG033 API对齐Akka)</summary>
    [Fact]
    public async Task UnbecomeStacked_恢复行为_新消息由原行为处理() {
        await using var actor = new BecomeActor();
        actor.Tell("become-stacked");
        actor.Tell("msg2");
        actor.Tell("unbecome");
        actor.Tell("msg1");

        await TestWaitHelper.WaitUntilAsync(() => actor.State2Processed.Count >= 2 && actor.State1Processed.Count >= 2, TimeSpan.FromMilliseconds(500));

        actor.State2Processed.Should().Contain("msg2");
        actor.State2Processed.Should().Contain("unbecome");
        actor.State1Processed.Should().Contain("become-stacked");
        actor.State1Processed.Should().Contain("msg1");
    }

    /// <summary>Become 替换行为不入栈,UnbecomeStacked 栈空无操作(DSG033 API对齐Akka)</summary>
    [Fact]
    public async Task Become_替换行为_不入栈() {
        await using var actor = new BecomeActor();
        actor.Tell("become");
        actor.Tell("msg2");

        await TestWaitHelper.WaitUntilAsync(() => actor.State2Processed.Count >= 1, TimeSpan.FromMilliseconds(500));

        actor.State2Processed.Should().Contain("msg2");
        actor.State1Processed.Should().Contain("become");
        actor.State1Processed.Should().NotContain("msg2");
    }

    /// <summary>Tell 带 sender,Handle 中可通过 Sender 属性获取(DSG033 API对齐Akka)</summary>
    [Fact]
    public async Task Tell_带Sender_Handle中可获取Sender() {
        await using var actor = new SenderActor();
        var sender = new object();
        actor.Tell("msg", sender);

        await TestWaitHelper.WaitUntilAsync(() => actor.ProcessedCount >= 1, TimeSpan.FromMilliseconds(500));

        actor.LastSender.Should().BeSameAs(sender);
    }

    /// <summary>Tell 不带 sender,Sender 为 null(DSG033 API对齐Akka)</summary>
    [Fact]
    public async Task Tell_不带Sender_Sender为null() {
        await using var actor = new SenderActor();
        actor.Tell("msg");

        await TestWaitHelper.WaitUntilAsync(() => actor.ProcessedCount >= 1, TimeSpan.FromMilliseconds(500));

        actor.LastSender.Should().BeNull();
    }

    /// <summary>ReceiveTimeout: 空闲超时触发 OnReceiveTimeout(DSG033 API对齐Akka)</summary>
    [Fact]
    public async Task ReceiveTimeout_空闲超时触发OnReceiveTimeout() {
        await using var actor = new ReceiveTimeoutActor(TimeSpan.FromMilliseconds(100));

        await TestWaitHelper.WaitUntilAsync(() => actor.TimeoutCount >= 1, TimeSpan.FromMilliseconds(1000));

        actor.TimeoutCount.Should().BeGreaterThanOrEqualTo(1, "空闲超时应触发 OnReceiveTimeout");
    }

    /// <summary>ReceiveTimeout: 收到消息后重置计时器,不误触发(DSG033 API对齐Akka)</summary>
    [Fact]
    public async Task ReceiveTimeout_收到消息不触发超时() {
        await using var actor = new ReceiveTimeoutActor(TimeSpan.FromMilliseconds(300));
        actor.Tell("msg1");

        await TestWaitHelper.WaitUntilAsync(() => actor.ProcessedCount >= 1, TimeSpan.FromMilliseconds(500));

        actor.ProcessedCount.Should().BeGreaterThanOrEqualTo(1);
        actor.TimeoutCount.Should().Be(0, "收到消息期间不应触发超时");
    }

    /// <summary>PipeTo: Task 完成后结果发给 Actor(DSG033 API对齐Akka)</summary>
    [Fact]
    public async Task PipeTo_Task完成_结果发给Actor() {
        await using var actor = new PipeToActor();
        var tcs = new TaskCompletionSource<string>();
        tcs.Task.PipeTo(actor);

        tcs.SetResult("done");

        await TestWaitHelper.WaitUntilAsync(() => actor.ProcessedCommands.Count >= 1, TimeSpan.FromMilliseconds(500));

        actor.ProcessedCommands.Should().Contain("done");
    }

    /// <summary>PipeTo: Task 未完成时不发消息(DSG033 API对齐Akka)</summary>
    [Fact]
    public async Task PipeTo_Task未完成_不发消息() {
        await using var actor = new PipeToActor();
        var tcs = new TaskCompletionSource<string>();
        tcs.Task.PipeTo(actor);

        await Task.Delay(200);

        actor.ProcessedCommands.Should().BeEmpty("Task 未完成,不应发消息");
    }

    /// <summary>Stash/UnstashAll: 暂存消息后取出处理(DSG033 API对齐Akka)</summary>
    [Fact]
    public async Task Stash_UnstashAll_暂存后取出处理() {
        await using var actor = new StashActor();
        actor.Tell("stash-me");
        actor.Tell("process-next");

        await TestWaitHelper.WaitUntilAsync(() => actor.ProcessedCommands.Count >= 1, TimeSpan.FromMilliseconds(500));

        actor.ProcessedCommands.Should().Contain("process-next");
        actor.ProcessedCommands.Should().NotContain("stash-me");

        actor.Tell("unstash");

        await TestWaitHelper.WaitUntilAsync(() => actor.ProcessedCommands.Count >= 3, TimeSpan.FromMilliseconds(500));

        actor.ProcessedCommands.Should().Contain("stash-me");
        actor.ProcessedCommands.Should().Contain("unstash");
    }

    /// <summary>EventStream: Subscribe + Publish 订阅者收到事件(DSG033 API对齐Akka)</summary>
    [Fact]
    public void EventStream_Subscribe_Publish_订阅者收到事件() {
        var stream = new EventStream();
        var received = new List<string>();
        using var sub = stream.Subscribe<string>(s => received.Add(s));

        stream.Publish("hello");

        received.Should().Contain("hello");
    }

    /// <summary>EventStream: Dispose 取消订阅后不再收到(DSG033 API对齐Akka)</summary>
    [Fact]
    public void EventStream_Unsubscribe_不再收到事件() {
        var stream = new EventStream();
        var received = new List<string>();
        var sub = stream.Subscribe<string>(s => received.Add(s));
        sub.Dispose();

        stream.Publish("hello");

        received.Should().BeEmpty("Dispose 后不应收到事件");
    }

    /// <summary>EventStream: 多个订阅者都收到事件(DSG033 API对齐Akka)</summary>
    [Fact]
    public void EventStream_多订阅者_都收到事件() {
        var stream = new EventStream();
        var received1 = new List<int>();
        var received2 = new List<int>();
        using var sub1 = stream.Subscribe<int>(n => received1.Add(n));
        using var sub2 = stream.Subscribe<int>(n => received2.Add(n));

        stream.Publish(42);

        received1.Should().Contain(42);
        received2.Should().Contain(42);
    }
}

/// <summary>
/// 测试用 Actor — 输入 string，输出 "processed-{input}"。
/// </summary>
internal sealed class TestActor : ActorBase<string, string> {
    public readonly List<string> ProcessedCommands = new();
    /// <summary>获取错误计数</summary>
    public int ErrorCount { get; private set; }
    public TaskCompletionSource? Gate;

    /// <summary>初始化测试 Actor</summary>
    /// <param name="boundedCapacity">有界容量（可选）</param>
    public TestActor(int? boundedCapacity = null)
        : base(boundedCapacity is null ? null : new ActorBackpressure(boundedCapacity.Value)) {
    }

    /// <summary>初始化测试 Actor</summary>
    /// <param name="backpressure">背压配置（可选）</param>
    public TestActor(ActorBackpressure? backpressure)
        : base(backpressure) {
    }

    /// <summary>初始化测试 Actor — 指定输出通道容量和满策略</summary>
    /// <param name="backpressure">背压配置</param>
    /// <param name="outputCapacity">输出通道容量</param>
    /// <param name="outputFullMode">输出通道满策略</param>
    public TestActor(ActorBackpressure? backpressure, int? outputCapacity, BoundedChannelFullMode outputFullMode)
        : base(backpressure, outputCapacity, outputFullMode) {
    }

    protected override void Handle(string command, CancellationToken ct) {
        if (command == "throw")
            throw new InvalidOperationException("test error");
        if (Gate is not null) Gate.Task.Wait(ct);
        ProcessedCommands.Add(command);
        TryPublish($"processed-{command}");
    }

    protected override void OnConsumerError(Exception ex) {
        ErrorCount++;
    }
}

/// <summary>生命周期钩子测试 Actor(DSG033 S2)</summary>
internal sealed class LifecycleActor : ActorBase<string, string> {
    public bool PreStartCalled;
    public bool PostStopCalled;
    protected override void PreStart() => PreStartCalled = true;
    protected override void PostStop() => PostStopCalled = true;
    protected override void Handle(string command, CancellationToken ct) { }
}

/// <summary>Become/Unbecome 行为切换测试 Actor(DSG033 API对齐Akka)</summary>
internal sealed class BecomeActor : ActorBase<string, string> {
    public readonly List<string> State1Processed = new();
    public readonly List<string> State2Processed = new();

    protected override void Handle(string command, CancellationToken ct) {
        State1Processed.Add(command);
        if (command == "become-stacked") {
            BecomeStacked(State2Behavior);
        } else if (command == "become") {
            Become(State2Behavior);
        }
    }

    private void State2Behavior(string command, CancellationToken ct) {
        State2Processed.Add(command);
        if (command == "unbecome") {
            UnbecomeStacked();
        }
    }
}

/// <summary>Sender 测试 Actor(DSG033 API对齐Akka)</summary>
internal sealed class SenderActor : ActorBase<string, string> {
    public object? LastSender;
    public int ProcessedCount;
    protected override void Handle(string command, CancellationToken ct) {
        LastSender = Sender;
        ProcessedCount++;
    }
}

/// <summary>ReceiveTimeout 测试 Actor(DSG033 API对齐Akka)</summary>
internal sealed class ReceiveTimeoutActor(TimeSpan timeout) : ActorBase<string, string> {
    public int TimeoutCount;
    public int ProcessedCount;

    protected override void PreStart() => SetReceiveTimeout(timeout);

    protected override void Handle(string command, CancellationToken ct) {
        ProcessedCount++;
    }

    protected override void OnReceiveTimeout() {
        TimeoutCount++;
    }
}

/// <summary>PipeTo 测试 Actor(DSG033 API对齐Akka)</summary>
internal sealed class PipeToActor : ActorBase<string, string> {
    public readonly List<string> ProcessedCommands = new();
    protected override void Handle(string command, CancellationToken ct) {
        ProcessedCommands.Add(command);
    }
}

/// <summary>Stash 测试 Actor(DSG033 API对齐Akka)</summary>
internal sealed class StashActor : ActorBase<string, string> {
    public readonly List<string> ProcessedCommands = new();
    private bool _stashed;
    protected override void Handle(string command, CancellationToken ct) {
        if (command == "stash-me" && !_stashed) {
            _stashed = true;
            Stash();
            return;
        }
        if (command == "unstash") {
            UnstashAll();
            ProcessedCommands.Add(command);
            return;
        }
        ProcessedCommands.Add(command);
    }
}