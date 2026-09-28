namespace Core.Utils;

/// <summary>
/// 全双工 Actor 基类 — 输入 Channel + 输出 Channel。
/// <para>外部通过 Tell 发送命令，通过 OutputAsync 拉取输出。</para>
/// <para>Actor 通过 TryPublish 主动推送消息，无需等待外部请求。</para>
/// <para>输入/输出 Channel 均有界(默认容量 DefaultChannelCapacity),输入有水位线+超时,输出满策略 DropOldest。</para>
/// <para>派生类定义命令类型并实现 <see cref="Handle"/>,所有可变状态由 Consumer 线程独占访问,无需锁。</para>
/// <para>线程安全保证:命令按 FIFO 顺序串行处理;多生产者通过 Tell/TrySend 投递。</para>
/// <para>异常容错:单条命令异常不会终止 Consumer 循环,通过 OnConsumerError 回调通知子类。</para>
/// <para>背压:通过 <see cref="ActorBackpressure"/> 配置有界容量、水位线告警、发送超时,防止 OOM 和永久阻塞。</para>
/// </summary>
/// <typeparam name="TCommand">命令类型 — 建议用 record 或 sealed class,实现标记接口以约束合法命令</typeparam>
/// <typeparam name="TOut">输出消息类型 — 建议用 record 或 sealed class</typeparam>
public abstract class ActorBase<TCommand, TOut> : IActor<TCommand>, IAsyncDisposable {
    private readonly Channel<TCommand> _inputChannel;
    private readonly Channel<TOut> _outputChannel;
    private readonly Task _consumerTask;
    private readonly Task _retryTask;
    private readonly CancellationTokenSource _cts = new();
    private readonly ActorBackpressure? _backpressure;
    private readonly Channel<TimeSpan> _bpDelayQueue = Channel.CreateBounded<TimeSpan>(new BoundedChannelOptions(16) {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = false
    });
    private readonly Channel<RetryEntry<TCommand>> _retryQueue = Channel.CreateBounded<RetryEntry<TCommand>>(new BoundedChannelOptions(1024) {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true,
        SingleWriter = false
    });
    private int _disposed;
    private int _inputCount;
    private int _retryQueueCount;
    private int _outputCount;
    private readonly ILogger? _logger;

    private static readonly AsyncLocal<ImmutableDag<string>?> _askWaitGraph = new();

    /// <summary>背压重试最大次数</summary>
    public const int BackpressureMaxRetries = 16;

    /// <summary>默认通道容量 — 无显式背压配置时使用,统一有界防 OOM</summary>
    public const int DefaultChannelCapacity = 2048;

    /// <summary>
    /// 构造 Actor — 有界输入/输出通道(默认容量 DefaultChannelCapacity)。
    /// </summary>
    protected ActorBase()
        : this(null, null, null) {
    }

    /// <summary>
    /// 构造 Actor — 有界输入通道，有界输出通道(默认容量 DefaultChannelCapacity)。
    /// </summary>
    /// <param name="boundedCapacity">有界输入通道容量(null 为默认容量 DefaultChannelCapacity)</param>
    /// <param name="fullMode">有界通道满时策略</param>
    /// <param name="logger">日志记录器(null=静默)</param>
    protected ActorBase(int? boundedCapacity, BoundedChannelFullMode fullMode = BoundedChannelFullMode.Wait, ILogger? logger = null)
        : this(boundedCapacity is null ? null : new ActorBackpressure(boundedCapacity.Value, fullMode), null, logger) {
    }

    /// <summary>
    /// 构造 Actor — 完整背压配置。
    /// </summary>
    /// <param name="backpressure">输入背压配置(null=默认有界容量 DefaultChannelCapacity,无水位线,无超时)</param>
    /// <param name="outputCapacity">输出通道容量(null=默认容量 DefaultChannelCapacity)</param>
    /// <param name="logger">日志记录器(null=静默,不记录审计日志)</param>
    protected ActorBase(ActorBackpressure? backpressure = null, int? outputCapacity = null, ILogger? logger = null) {
        Id = $"{GetType().Name}-{Guid.NewGuid():N}"[..8];
        _logger = logger;
        _backpressure = backpressure;
        _inputChannel = CreateInputChannel(backpressure);
        _outputChannel = CreateOutputChannel(outputCapacity);
        _consumerTask = Task.Factory.StartNew(
            ConsumeLoopAsync,
            CancellationToken.None,
            TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach,
            TaskScheduler.Default).Unwrap();
        _retryTask = Task.Run(ProcessRetryQueueAsync);
    }

    private static Channel<TCommand> CreateInputChannel(ActorBackpressure? backpressure) {
        if (backpressure is null || backpressure.Capacity == 0) {
            return Channel.CreateBounded<TCommand>(new BoundedChannelOptions(DefaultChannelCapacity) {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });
        }

        return Channel.CreateBounded<TCommand>(new BoundedChannelOptions(backpressure.Capacity) {
            FullMode = backpressure.FullMode,
            SingleReader = true,
            SingleWriter = false
        });
    }

    private static Channel<TOut> CreateOutputChannel(int? capacity) {
        if (capacity is null || capacity == 0) {
            return Channel.CreateBounded<TOut>(new BoundedChannelOptions(DefaultChannelCapacity) {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true
            });
        }

        return Channel.CreateBounded<TOut>(new BoundedChannelOptions(capacity.Value) {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true
        });
    }

    /// <summary>Actor 唯一标识 — 用于日志和监控</summary>
    public string Id { get; }

    /// <summary>
    /// Consumer 任务 — 用于等待 Consumer 退出(Dispose 时)或观察异常。
    /// </summary>
    protected internal Task ConsumerTask => _consumerTask;

    /// <summary>当前输入邮箱消息总数 — 输入通道 + 重试队列(R1: 水位告警基于此值)</summary>
    public int InputCount => Volatile.Read(ref _inputCount) + Volatile.Read(ref _retryQueueCount);

    /// <summary>输入通道内消息数(不含重试队列)</summary>
    public int InputChannelCount => Volatile.Read(ref _inputCount);

    /// <summary>重试队列消息数(入队失败正在重试投递的消息)</summary>
    public int RetryQueueCount => Volatile.Read(ref _retryQueueCount);

    /// <summary>当前输出通道消息数 — 用于监控堆积</summary>
    public int OutputCount => Volatile.Read(ref _outputCount);

    /// <summary>Actor 是否忙碌 — 输入队列有待处理消息 或 输出队列有待消费消息(P2-2: 监控指标)</summary>
    public bool IsBusy => InputCount > 0 || OutputCount > 0;

    /// <summary>输入是否达到高水位线</summary>
    public bool IsInputHighWatermark => _backpressure is not null
        && InputCount >= _backpressure.EffectiveHighWatermark;

    /// <summary>输入是否达到危险水位线</summary>
    public bool IsInputCriticalWatermark => _backpressure is not null
        && InputCount >= _backpressure.EffectiveCriticalWatermark;

    /// <summary>输入背压水位事件</summary>
    public event EventHandler<BackpressureEventArgs>? InputWatermarkReached;

    /// <summary>背压重试失败事件 — 16次重试后消息仍未能入队时触发(不丢弃,外部可计入死信队列)</summary>
    public event EventHandler<BackpressureSendFailedEventArgs<TCommand>>? SendFailed;

    /// <summary>
    /// 向 Actor 同步发送命令 — Tell 模式(射后不理,不阻塞调用方)。
    /// <para>TryWrite(非阻塞),通道满时后台重试(16次+指数退避+换流水号),射后不理不阻塞Actor消费循环。</para>
    /// <para>16次重试失败触发 <see cref="SendFailed"/> 事件(不丢弃,外部可计入死信队列)。</para>
    /// <para>背压信号通过 <see cref="ReceiveBackpressureSignal"/> 接收,重试时消费延迟信号。</para>
    /// <para><b>⚠️ Tell vs Ask</b>:此方法是 Tell(只保证消息入队,不保证 Consumer 已处理)。</para>
    /// <para><b>⚠️ Dispose/DisposeAsync 路径禁止用 Ask</b>(线程池饥饿时 await tcs.Task 死锁)。</para>
    /// <para><b>⚠️ FIFO 不保证</b>:消息入队失败进入重试队列后,可能晚于后续成功入队的消息被消费,命令顺序不保证严格 FIFO。</para>
    /// </summary>
    /// <param name="cmd">命令实例</param>
    /// <exception cref="ObjectDisposedException">Actor 已释放</exception>
    public void Tell(TCommand cmd) {
        ThrowIfDisposed();

        Interlocked.Increment(ref _inputCount);
        if (_inputChannel.Writer.TryWrite(cmd)) {
            CheckInputWatermark();
            return;
        }
        Interlocked.Decrement(ref _inputCount);
        Interlocked.Increment(ref _retryQueueCount);
        if (!_retryQueue.Writer.TryWrite(new RetryEntry<TCommand>(cmd, 1))) {
            Interlocked.Decrement(ref _retryQueueCount);
            _logger?.LogWarning("[Actor:{ActorId}] 重试队列满,消息丢弃", Id);
            try {
                SendFailed?.Invoke(this, new BackpressureSendFailedEventArgs<TCommand>(cmd, 0));
            } catch (Exception ex) {
                _logger?.LogWarning(ex, "[Actor:{ActorId}] SendFailed 订阅者异常忽略", Id);
            }
        }
    }

    /// <summary>
    /// 单例重试队列处理 — 一个后台 Task 处理所有重试消息(P1-2: 替代每消息一 Task,高负载时不产生大量 Delay 任务)
    /// </summary>
    private async Task ProcessRetryQueueAsync() {
        try {
            await foreach (var entry in _retryQueue.Reader.ReadAllAsync(_cts.Token).ConfigureAwait(false)) {
                if (Volatile.Read(ref _disposed) != 0) return;

                var bpDelay = ConsumeBackpressureDelay();
                var backoffMs = 100 * (1 << Math.Min(entry.Attempt, 10));
                backoffMs = Math.Min(backoffMs, 5000);
                var totalDelayMs = Math.Min(bpDelay.TotalMilliseconds + backoffMs, 10_000);
                if (totalDelayMs > 0)
                    await Task.Delay(TimeSpan.FromMilliseconds(totalDelayMs), _cts.Token).ConfigureAwait(false);

                Interlocked.Decrement(ref _retryQueueCount);
                Interlocked.Increment(ref _inputCount);
                if (_inputChannel.Writer.TryWrite(entry.Command)) {
                    CheckInputWatermark();
                    continue;
                }
                Interlocked.Decrement(ref _inputCount);

                if (entry.Attempt < BackpressureMaxRetries) {
                    Interlocked.Increment(ref _retryQueueCount);
                    _retryQueue.Writer.TryWrite(new RetryEntry<TCommand>(entry.Command, entry.Attempt + 1));
                } else {
                    try {
                        SendFailed?.Invoke(this, new BackpressureSendFailedEventArgs<TCommand>(entry.Command, BackpressureMaxRetries));
                    } catch (Exception ex) {
                        _logger?.LogWarning(ex, "[Actor:{ActorId}] SendFailed 订阅者异常忽略", Id);
                    }
                }
            }
        } catch (OperationCanceledException) { }
    }

    /// <summary>
    /// 接收远程背压信号 — 写入延迟信号队列,供重试时消费
    /// </summary>
    /// <param name="suggestedDelay">建议延迟时间</param>
    public void ReceiveBackpressureSignal(TimeSpan suggestedDelay) => _bpDelayQueue.Writer.TryWrite(suggestedDelay);

    /// <summary>
    /// 消费所有待处理背压延迟信号,取最新延迟(R2: 远程背压信号是瞬时建议,取最新非累加)
    /// </summary>
    private TimeSpan ConsumeBackpressureDelay() {
        var last = TimeSpan.Zero;
        while (_bpDelayQueue.Reader.TryRead(out var delay)) last = delay;
        return last;
    }

    /// <summary>
    /// 向 Actor 同步尝试发送命令 — Tell 模式(发消息即走)。
    /// <para>通道已关闭、已释放或(有界通道)已满时返回 false。</para>
    /// <para><b>⚠️ Dispose 路径首选</b>:DisposeAsync 中用 TrySend 发清理命令,不阻塞等待 Consumer,避免线程池饥饿死锁。</para>
    /// </summary>
    /// <param name="cmd">命令实例</param>
    /// <returns>true 表示已入队,false 表示未入队</returns>
    public bool TrySend(TCommand cmd) {
        if (Volatile.Read(ref _disposed) != 0) return false;
        Interlocked.Increment(ref _inputCount);
        var written = _inputChannel.Writer.TryWrite(cmd);
        if (written) {
            CheckInputWatermark();
        } else {
            Interlocked.Decrement(ref _inputCount);
        }
        return written;
    }

    /// <summary>
    /// Actor 主动推送消息到输出 Channel — 外部通过 OutputAsync 拉取。
    /// </summary>
    protected bool TryPublish(TOut msg) {
        if (Volatile.Read(ref _disposed) != 0) return false;
        Interlocked.Increment(ref _outputCount);
        if (_outputChannel.Writer.TryWrite(msg)) {
            return true;
        }
        Interlocked.Decrement(ref _outputCount);
        return false;
    }

    /// <summary>
    /// 外部拉取输出流 — 阻塞式 IAsyncEnumerable。
    /// </summary>
    public async IAsyncEnumerable<TOut> OutputAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
        await foreach (var item in _outputChannel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false)) {
            Interlocked.Decrement(ref _outputCount);
            yield return item;
        }
    }

    private void CheckInputWatermark() {
        if (_backpressure is null) return;
        var count = InputCount;
        var level = count >= _backpressure.EffectiveCriticalWatermark ? WatermarkLevel.Critical
                   : count >= _backpressure.EffectiveHighWatermark ? WatermarkLevel.High
                   : WatermarkLevel.Normal;
        if (level != WatermarkLevel.Normal) {
            InputWatermarkReached?.Invoke(this, new BackpressureEventArgs(
                GetType().Name, count, _backpressure.Capacity, level));
        }
    }

    /// <summary>
    /// 子类实现命令处理逻辑 — 由 Consumer 线程串行同步调用,此方法内访问实例可变状态无需锁。
    /// <para><b>⚠️ 同步 Handle 铁律</b>:</para>
    /// <para>1. <b>禁止 async/await</b> — 签名为 void,编译器无法阻止 async lambda,但运行时会破坏 Consumer 串行不变量</para>
    /// <para>2. <b>禁止阻塞 I/O</b> — 同步文件/网络 I/O 会卡死 Consumer 线程,所有 I/O 委托给 I/O Actor(Tell)</para>
    /// <para>3. <b>fire-and-forget 必须回投</b> — Task.Run 中异步操作完成后必须 Self.Tell 回投结果,不得直接修改状态</para>
    /// <para>4. <b>禁止返回 Task</b> — 异步 Handle 会导致 ConsumeLoop 提前消费下一条命令,破坏 FIFO 顺序</para>
    /// <para>违反以上任一规约将导致:命令乱序、状态竞态、Consumer 死锁。参见 ADR 0118。</para>
    /// </summary>
    /// <param name="command">待处理命令</param>
    /// <param name="ct">取消令牌(Actor 释放时触发取消)</param>
    protected abstract void Handle(TCommand command, CancellationToken ct);

    /// <summary>
    /// Consumer 处理单条命令异常的回调 — 默认忽略,子类可重写以记录日志或计数。
    /// <para>此方法在 Consumer 线程内调用,不应抛异常(抛出会被吞掉)。</para>
    /// </summary>
    /// <param name="ex">命令处理异常</param>
    protected virtual void OnConsumerError(Exception ex) { }

    /// <summary>
    /// 幂等去重存储 — 双 Tell 协议的 Consumer 端幂等守卫。
    /// <para>设置后,ConsumeLoop 对实现 <see cref="IRequestCommand"/> 的命令检查缓存:</para>
    /// <para>命中 → 调用 <see cref="IRequestCommand.TryRestoreFromCache"/> 恢复结果(调用命令自带 OnSuccess 回调) → 跳过 Handle</para>
    /// <para>未命中 → 执行 Handle(派生类自行 TryRegister 缓存结果)</para>
    /// <para>null=不启用幂等去重(默认)。派生类在构造函数中设置。</para>
    /// </summary>
    protected IIdempotencyStore? IdempotencyStore { get; set; }

    private async Task ConsumeLoopAsync() {
        using var actorScope = AsyncFlowIdentity.EnterActorScope(Id);
        try {
            await foreach (var cmd in _inputChannel.Reader.ReadAllAsync(_cts.Token).ConfigureAwait(false)) {
                Interlocked.Decrement(ref _inputCount);
                CheckInputWatermark();
                try {
                    if (cmd is IRequestCommand requestCmd && IdempotencyStore is not null &&
                        requestCmd.TryRestoreFromCache(IdempotencyStore)) {
                        continue;
                    }
                    Handle(cmd, _cts.Token);
                } catch (OperationCanceledException) when (_cts.IsCancellationRequested) {
                    return;
                } catch (Exception ex) {
                    OnConsumerError(ex);
                }
            }
        } catch (OperationCanceledException) { }
    }

    /// <summary>
    /// 标准背压回调 — 根据水位层级计算延迟并异步重试发送命令。
    /// <para><b>层级策略</b>:</para>
    /// <para>Critical: 延迟 signal.SuggestedDelay(100ms×超出量,上限1s)后重试</para>
    /// <para>High: 延迟 signal.SuggestedDelay(50ms)后重试</para>
    /// <para>Normal: 立即重试(恢复生产)</para>
    /// <para>回调在 Consumer 线程执行,重试通过 fire-and-forget 异步调度,不阻塞 Consumer。</para>
    /// </summary>
    /// <param name="resend">重试委托 — 延迟后调用,重新发送命令(如 TrySend(cmd))</param>
    /// <param name="logger">日志记录器(null=静默,resend 异常时记录)</param>
    /// <returns>标准背压回调,可直接作为命令的 OnBackpressure 参数</returns>
    public static Action<BackpressureSignal> CreateBackpressureHandler(Action resend, ILogger? logger = null) {
        return signal => {
            if (signal.SuggestedDelay > TimeSpan.Zero) {
                _ = Task.Run(async () => {
                    try {
                        await Task.Delay(signal.SuggestedDelay).ConfigureAwait(false);
                        resend();
                    } catch (Exception ex) {
                        logger?.LogWarning(ex, "CreateBackpressureHandler resend 异常忽略");
                    }
                });
            } else {
                try { resend(); } catch (Exception ex) { logger?.LogWarning(ex, "CreateBackpressureHandler resend 异常忽略"); }
            }
        };
    }

    private void ThrowIfDisposed() {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(GetType().Name);
    }

    /// <summary>
    /// Ask 模式等待回复 — 内置死锁检测(超时抛 <see cref="ActorAskDeadlockException"/>) + 等待图环检测(防循环 Ask 死锁)。
    /// <para><b>⚠️ Ask vs Tell</b>:Ask = 发消息等回复(阻塞当前线程);Tell = 发消息即走(fire-and-forget)。</para>
    /// <para><b>死锁风险</b>:Ask 依赖 Consumer 被调度,Consumer 无法运行 → tcs 永不完成 → 死锁。</para>
    /// <para>此方法加超时守卫,超时抛带诊断信息的异常,避免永久挂死。</para>
    /// <para><b>等待图环检测</b>:通过线程ID自动识别调用方Actor,记入等待图,检测到环(A等B且B等A)抛 <see cref="ActorCyclicAskException"/>。</para>
    /// <para><b>规则</b>:Dispose/DisposeAsync 路径禁止用 Ask(改用 Tell/TrySend);查询路径用 Ask 但必须经此方法加超时。</para>
    /// </summary>
    /// <typeparam name="T">回复类型</typeparam>
    /// <param name="tcs">回复源(由调用方创建,命令发送后传入)</param>
    /// <param name="ct">取消令牌</param>
    /// <param name="timeoutMs">超时(默认10s,超时抛死锁诊断异常)</param>
    /// <exception cref="ActorAskDeadlockException">Ask 超时 — 可能线程池饥饿导致 Consumer 无法调度</exception>
    /// <exception cref="ActorCyclicAskException">等待图检测到环 — 循环 Ask 死锁</exception>
    protected async Task<T> AskAwait<T>(TaskCompletionSource<T> tcs, CancellationToken ct = default, int timeoutMs = 10_000) {
        using var waitScope = EnterWaitGraph(TryGetCallerActorId());
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(timeoutMs);
        try {
            return await tcs.Task.WaitAsync(linkedCts.Token).ConfigureAwait(false);
        } catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
            throw new ActorAskDeadlockException(GetType().Name, timeoutMs);
        }
    }

    /// <summary>
    /// Ask 模式等待回复(无返回值) — 内置死锁检测 + 等待图环检测,非泛型重载
    /// </summary>
    protected async Task AskAwait(TaskCompletionSource tcs, CancellationToken ct = default, int timeoutMs = 10_000) {
        using var waitScope = EnterWaitGraph(TryGetCallerActorId());
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(timeoutMs);
        try {
            await tcs.Task.WaitAsync(linkedCts.Token).ConfigureAwait(false);
        } catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
            throw new ActorAskDeadlockException(GetType().Name, timeoutMs);
        }
    }

    /// <summary>
    /// Ask 重试模式等待回复 — 内置重试16次+指数退避+全图环检测+幂等支持。
    /// <para><b>与 AskAwait 区别</b>:此方法接收命令工厂委托,内部重试时重新发送命令;AskAwait 只等待已有 tcs。</para>
    /// <para><b>重试策略</b>:单次超时 <paramref name="singleTimeoutMs"/>,超时后指数退避(100ms×2^attempt),最多重试 <paramref name="maxRetries"/> 次。</para>
    /// <para><b>幂等</b>:命令实现 <see cref="IIdempotent"/> 标记接口时,重试安全(无副作用);非幂等命令重试由调用方确保安全。</para>
    /// <para><b>全图环检测</b>:DFS 遍历等待图,检测间接环(A→B→C→A),不仅检测直接环。</para>
    /// <para><b>总超时</b> = singleTimeoutMs × (maxRetries+1) + 退避总和,超过抛 <see cref="ActorAskDeadlockException"/>。</para>
    /// </summary>
    /// <typeparam name="T">回复类型</typeparam>
    /// <param name="commandFactory">命令工厂 — 接收新 TCS,返回命令实例(每次重试创建新 TCS 和新命令)</param>
    /// <param name="ct">取消令牌</param>
    /// <param name="singleTimeoutMs">单次超时(默认10s,每次重试等待此超时)</param>
    /// <param name="maxRetries">最大重试次数(默认16,总尝试=maxRetries+1)</param>
    /// <exception cref="ActorAskDeadlockException">重试耗尽仍超时 — 可能线程池饥饿或 Consumer 阻塞</exception>
    /// <exception cref="ActorCyclicAskException">等待图检测到环(含间接环) — 循环 Ask 死锁</exception>
    protected async Task<T> AskWithRetryAsync<T>(
        Func<TaskCompletionSource<T>, TCommand> commandFactory,
        CancellationToken ct = default,
        int singleTimeoutMs = 10_000,
        int maxRetries = 16) {
        using var waitScope = EnterWaitGraph(TryGetCallerActorId());
        for (var attempt = 0; attempt <= maxRetries; attempt++) {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cmd = commandFactory(tcs);
            if (!TrySend(cmd)) {
                if (attempt >= maxRetries)
                    throw new ActorAskDeadlockException(GetType().Name, singleTimeoutMs * (maxRetries + 1));
                var delayMs = Math.Min(100 * (1 << Math.Min(attempt, 10)), 5000);
                await Task.Delay(delayMs, ct).ConfigureAwait(false);
                continue;
            }
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linkedCts.CancelAfter(singleTimeoutMs);
            try {
                return await tcs.Task.WaitAsync(linkedCts.Token).ConfigureAwait(false);
            } catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
                if (attempt >= maxRetries)
                    throw new ActorAskDeadlockException(GetType().Name, singleTimeoutMs * (maxRetries + 1));
                var delayMs = Math.Min(100 * (1 << Math.Min(attempt, 10)), 5000);
                await Task.Delay(delayMs, ct).ConfigureAwait(false);
            }
        }
        throw new ActorAskDeadlockException(GetType().Name, singleTimeoutMs * (maxRetries + 1));
    }

    /// <summary>
    /// Ask 重试模式等待回复(无返回值)— 内置重试16次+指数退避+全图环检测,非泛型重载。
    /// </summary>
    /// <param name="commandFactory">命令工厂 — 接收新 TCS,返回命令实例</param>
    /// <param name="ct">取消令牌</param>
    /// <param name="singleTimeoutMs">单次超时(默认10s)</param>
    /// <param name="maxRetries">最大重试次数(默认16)</param>
    protected async Task AskWithRetryAsync(
        Func<TaskCompletionSource, TCommand> commandFactory,
        CancellationToken ct = default,
        int singleTimeoutMs = 10_000,
        int maxRetries = 16) {
        using var waitScope = EnterWaitGraph(TryGetCallerActorId());
        for (var attempt = 0; attempt <= maxRetries; attempt++) {
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var cmd = commandFactory(tcs);
            if (!TrySend(cmd)) {
                if (attempt >= maxRetries)
                    throw new ActorAskDeadlockException(GetType().Name, singleTimeoutMs * (maxRetries + 1));
                var delayMs = Math.Min(100 * (1 << Math.Min(attempt, 10)), 5000);
                await Task.Delay(delayMs, ct).ConfigureAwait(false);
                continue;
            }
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linkedCts.CancelAfter(singleTimeoutMs);
            try {
                await tcs.Task.WaitAsync(linkedCts.Token).ConfigureAwait(false);
                return;
            } catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
                if (attempt >= maxRetries)
                    throw new ActorAskDeadlockException(GetType().Name, singleTimeoutMs * (maxRetries + 1));
                var delayMs = Math.Min(100 * (1 << Math.Min(attempt, 10)), 5000);
                await Task.Delay(delayMs, ct).ConfigureAwait(false);
            }
        }
        throw new ActorAskDeadlockException(GetType().Name, singleTimeoutMs * (maxRetries + 1));
    }

    /// <summary>
    /// 进入等待图作用域 — 在当前异步流的调用链本地图加边 callerId→Id,检测环,返回 scope(Dispose 恢复父图)。
    /// <para>等待图用 AsyncLocal 存储调用链本地图,避免全局静态图跨调用链污染/并发覆盖(Bug1 修复)。</para>
    /// <para>每次创建新 ImmutableDag 副本(从父图无锁快照复制),各异步流独立不竞态(P1-3 修复)。</para>
    /// <para>callerId 为 null 或等于自身时返回 null(无需加边)。</para>
    /// </summary>
    private WaitGraphScope? EnterWaitGraph(string? callerId) {
        if (callerId is null || callerId == Id) return null;
        var previousGraph = _askWaitGraph.Value;
        var graph = CloneWaitGraph(previousGraph);
        _askWaitGraph.Value = graph;
        if (!graph.Nodes.ContainsKey(callerId))
            graph.AddNode(new ImmutableDagNode<string> { Id = callerId, Payload = callerId });
        if (!graph.Nodes.ContainsKey(Id))
            graph.AddNode(new ImmutableDagNode<string> { Id = Id, Payload = Id });
        var edge = new DagEdge { FromId = callerId, ToId = Id };
        try {
            graph.AddEdge(edge);
        } catch (InvalidOperationException) {
            throw new ActorCyclicAskException(callerId, Id);
        }
        return new WaitGraphScope(_askWaitGraph, previousGraph);
    }

    /// <summary>从源图无锁快照复制所有节点和边到新 ImmutableDag 实例</summary>
    private static ImmutableDag<string> CloneWaitGraph(ImmutableDag<string>? source) {
        if (source is null) return new ImmutableDag<string>();
        var dag = new ImmutableDag<string>();
        foreach (var node in source.Nodes.Values)
            dag.AddNode(node);
        foreach (var edge in source.Edges.Values)
            dag.TryAddEdge(edge);
        return dag;
    }

    /// <summary>等待图作用域 — Dispose 时恢复父图引用(P1-3: 每次创建副本,无需 RemoveEdge)</summary>
    private sealed class WaitGraphScope(AsyncLocal<ImmutableDag<string>?> store, ImmutableDag<string>? previousGraph) : IDisposable {
        /// <summary>释放资源。</summary>
        public void Dispose() => store.Value = previousGraph;
    }

    private string? TryGetCallerActorId() => AsyncFlowIdentity.CurrentActorId;

    /// <summary>
    /// 释放 Actor — 取消 Consumer、完成通道,等待 Consumer 真正退出后释放 CTS。
    /// <para>Consumer 用 LongRunning 专用线程运行(不占线程池),Dispose await 不会导致线程池饥饿死锁。</para>
    /// <para>设计理由:fire-and-forget 会掩盖 Consumer 未完成清理的问题,改回 await 确保资源真正释放。</para>
    /// </summary>
    public virtual async ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _cts.Cancel();
        _inputChannel.Writer.TryComplete();
        _outputChannel.Writer.TryComplete();
        _retryQueue.Writer.TryComplete();
        try {
            await _consumerTask.ConfigureAwait(false);
        } catch (OperationCanceledException) { }
        try {
            await _retryTask.ConfigureAwait(false);
        } catch (OperationCanceledException) { }
        _cts.Dispose();
    }
}

/// <summary>
/// Actor Ask 模式死锁异常 — Ask 超时后抛出,带诊断信息指导修复。
/// </summary>
/// <remarks>
/// <para>触发条件:AskAwait 超时(默认10s) — Consumer 未在超时内处理命令并设置 Tcs。</para>
/// <para>常见根因:线程池饥饿 — 所有线程被阻塞等待,Consumer 任务无法被调度。</para>
/// <para>修复指导:Dispose 路径改用 Tell(TrySend);查询路径检查 Consumer 是否阻塞或线程池是否不足。</para>
/// </remarks>
public sealed class ActorAskDeadlockException : TimeoutException {
    /// <summary>Actor 类型名</summary>
    public string ActorName { get; }

    /// <summary>超时毫秒数</summary>
    public int TimeoutMs { get; }

    /// <summary>
    /// 构造 Ask 死锁异常
    /// </summary>
    /// <param name="actorName">Actor 类型名</param>
    /// <param name="timeoutMs">超时毫秒数</param>
    public ActorAskDeadlockException(string actorName, int timeoutMs)
        : base($"Actor {actorName} Ask 超时({timeoutMs}ms) — 可能线程池饥饿导致 Consumer 无法调度。" +
               "Dispose 路径改用 Tell(TrySend);查询路径检查 Consumer 是否阻塞或线程池是否不足。") {
        ActorName = actorName;
        TimeoutMs = timeoutMs;
    }
}

/// <summary>
/// Actor 循环 Ask 异常 — 等待图检测到环时抛出,预防循环 Ask 死锁(类型2)。
/// </summary>
/// <remarks>
/// <para>触发条件:AskAwait 检测到等待图环 — Actor A 等 B 回复,同时 B 等 A 回复。</para>
/// <para>检测机制:静态等待图(wait-for graph),通过线程ID自动识别调用方Actor,记入等待边,检测到环即抛异常。</para>
/// <para>修复指导:打破循环 — 其中一方改用 Tell(不等回复),或重构调用链消除循环依赖。</para>
/// </remarks>
public sealed class ActorCyclicAskException : InvalidOperationException {
    /// <summary>调用方 Actor ID</summary>
    public string CallerActorId { get; }

    /// <summary>目标 Actor ID</summary>
    public string TargetActorId { get; }

    /// <summary>
    /// 构造循环 Ask 异常
    /// </summary>
    /// <param name="callerActorId">调用方 Actor ID</param>
    /// <param name="targetActorId">目标 Actor ID</param>
    public ActorCyclicAskException(string callerActorId, string targetActorId)
        : base($"循环 Ask 检测: Actor {callerActorId} 等 {targetActorId} 回复,同时 {targetActorId} 等 {callerActorId} 回复 → 等待图环 → 死锁。" +
               "修复:其中一方改用 Tell(TrySend,不等回复),或重构调用链消除循环依赖。") {
        CallerActorId = callerActorId;
        TargetActorId = targetActorId;
    }
}

/// <summary>
/// 幂等命令标记接口 — 纯开发规约标记,框架不自动处理重试安全。
/// <para><b>⚠️ 框架行为</b>:ActorBase 不读取此接口,不自动缓存或校验幂等性。重试安全由调用方保证。</para>
/// <para><b>与 IRequestCommand 区别</b>:IRequestCommand 携带幂等键+TryRestoreFromCache,框架 ConsumeLoop 自动做缓存命中跳过;本接口仅为文档标记。</para>
/// <para>典型幂等命令:查询(Get/Read)、取消(Cancel)、状态切换到固定值(SetXxx)。</para>
/// <para>非幂等命令:追加(Append)、递增(Increment)、创建(Create) — 重试可能产生重复副作用。</para>
/// </summary>
public interface IIdempotent { }

/// <summary>
/// 单元类型 — 用于不需要输出的 Actor 的 TOut 参数。
/// </summary>
public readonly record struct Unit {
    /// <summary>唯一实例</summary>
    public static readonly Unit Value = default;
}

/// <summary>
/// 背压重试失败事件参数 — 16次重试后消息仍未能入队
/// </summary>
/// <typeparam name="TCommand">命令类型</typeparam>
/// <param name="Command">未能入队的命令(外部可计入死信队列)</param>
/// <param name="RetryCount">重试次数</param>
public sealed record BackpressureSendFailedEventArgs<TCommand>(
    TCommand Command,
    int RetryCount);

/// <summary>
/// 重试队列条目 — 命令 + 当前重试次数(P1-2: 单例重试队列)
/// </summary>
/// <typeparam name="TCommand">命令类型</typeparam>
/// <param name="Command">待重试命令</param>
/// <param name="Attempt">当前重试次数(1=首次重试)</param>
internal sealed record RetryEntry<TCommand>(
    TCommand Command,
    int Attempt);
