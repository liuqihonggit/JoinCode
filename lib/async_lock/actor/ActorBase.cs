namespace Core.Utils;

/// <summary>
/// 全双工 Actor 基类 — 输入 Channel + 输出 Channel。
/// <para>外部通过 Tell 发送命令，通过 OutputAsync 拉取输出。</para>
/// <para>Actor 通过 TryPublish 主动推送消息，无需等待外部请求。</para>
/// <para>输入/输出 Channel 均有界(默认容量 DefaultChannelCapacity),输入有水位线+超时,输出满策略可配置(默认 DropOldest)。</para>
/// <para>派生类定义命令类型并实现 <see cref="Handle"/>,所有可变状态由 Consumer 线程独占访问,无需锁。</para>
/// <para>线程安全保证:命令按 FIFO 顺序串行处理;多生产者通过 Tell/TrySend 投递。</para>
/// <para>异常容错:单条命令异常不会终止 Consumer 循环,通过 OnConsumerError 回调通知子类。</para>
/// <para>背压:通过 <see cref="ActorBackpressure"/> 配置有界容量、水位线告警、发送超时,防止 OOM 和永久阻塞。</para>
/// </summary>
/// <typeparam name="TCommand">命令类型 — 建议用 record 或 sealed class,实现标记接口以约束合法命令</typeparam>
/// <typeparam name="TOut">输出消息类型 — 建议用 record 或 sealed class</typeparam>
public abstract class ActorBase<TCommand, TOut> : IActor<TCommand>, IActorTell<TCommand>, IActorOutput<TOut>, IAsyncDisposable {
    private readonly Channel<TCommand> _inputChannel;
    private readonly Channel<TOut> _outputChannel;
    private readonly Task _consumerTask;
    private readonly CancellationTokenSource _cts = new();
    private readonly ActorBackpressure? _backpressure;
    private readonly WatermarkMonitor? _watermarkMonitor;
    private readonly IdempotencyGate _idempotencyGate;
    private readonly AskWaitGraphTracker _waitGraphTracker;
    private readonly MessageRetryEngine<TCommand> _retryEngine;
    private readonly ConcurrentBag<Task> _inFlightTasks = new();
    private int _disposed;
    private readonly ILogger? _logger;

    private static readonly AsyncLocal<ImmutableDag<string>?> _askWaitGraph = new();

    /// <summary>背压重试最大次数</summary>
    public const int BackpressureMaxRetries = 16;

    /// <summary>有效重试次数 — 优先用背压配置,无配置时用默认常量(P1-1: 可配置化)</summary>
    private int EffectiveMaxRetries => _backpressure?.MaxRetries ?? BackpressureMaxRetries;

    /// <summary>默认通道容量 — 无显式背压配置时使用,统一有界防 OOM</summary>
    public const int DefaultChannelCapacity = 2048;

    /// <summary>
    /// 构造 Actor — 有界输入/输出通道(默认容量 DefaultChannelCapacity)。
    /// </summary>
    protected ActorBase()
        : this(backpressure: null, outputCapacity: null, logger: null, idempotencyStore: null) {
    }

    /// <summary>
    /// 构造 Actor — 有界输入通道，有界输出通道(默认容量 DefaultChannelCapacity)。
    /// </summary>
    /// <param name="boundedCapacity">有界输入通道容量(null 为默认容量 DefaultChannelCapacity)</param>
    /// <param name="fullMode">有界通道满时策略</param>
    /// <param name="logger">日志记录器(null=静默)</param>
    protected ActorBase(int? boundedCapacity, BoundedChannelFullMode fullMode = BoundedChannelFullMode.Wait, ILogger? logger = null)
        : this(backpressure: boundedCapacity is null ? null : new ActorBackpressure(boundedCapacity.Value, fullMode), outputCapacity: null, logger: logger, idempotencyStore: null) {
    }

    /// <summary>
    /// 构造 Actor — 完整背压配置。
    /// </summary>
    /// <param name="backpressure">输入背压配置(null=默认有界容量 DefaultChannelCapacity,无水位线,无超时)</param>
    /// <param name="outputCapacity">输出通道容量(null=默认容量 DefaultChannelCapacity)</param>
    /// <param name="outputFullMode">输出通道满策略(默认 DropOldest — 丢弃最老消息腾位置;DropWrite — 丢弃新消息触发 OutputMessageDropped 事件)</param>
    /// <param name="logger">日志记录器(null=静默,不记录审计日志)</param>
    /// <param name="idempotencyStore">幂等去重存储(null=不启用,构造注入后只读不可修改)</param>
    /// <param name="useLongRunning">Consumer 是否用 LongRunning 专用线程(true=专用线程不占线程池,适合少量长驻Actor;false=线程池调度,适合大量短生命周期Actor)</param>
    protected ActorBase(ActorBackpressure? backpressure = null, int? outputCapacity = null, BoundedChannelFullMode outputFullMode = BoundedChannelFullMode.DropOldest, ILogger? logger = null, IIdempotencyStore? idempotencyStore = null, bool useLongRunning = true) {
        Id = $"{GetType().Name}-{Guid.NewGuid():N}"[..8];
        _logger = logger;
        _backpressure = backpressure;
        _watermarkMonitor = backpressure is null ? null : new WatermarkMonitor(backpressure);
        IdempotencyStore = idempotencyStore;
        _idempotencyGate = new IdempotencyGate(idempotencyStore);
        _waitGraphTracker = new AskWaitGraphTracker(_askWaitGraph);
        _inputChannel = CreateInputChannel(backpressure);
        _outputChannel = CreateOutputChannel(outputCapacity, outputFullMode);
        _retryEngine = new MessageRetryEngine<TCommand>(
            retryQueueCapacity: backpressure?.RetryQueueCapacity ?? 1024,
            maxRetries: EffectiveMaxRetries,
            inputWriter: _inputChannel.Writer,
            actorId: Id,
            logger: logger,
            shutdownCt: _cts.Token,
            onSendFailed: RaiseSendFailed,
            onEnqueuedToInput: CheckInputWatermark);
        var taskOptions = (useLongRunning ? TaskCreationOptions.LongRunning : TaskCreationOptions.None) | TaskCreationOptions.DenyChildAttach;
        _consumerTask = Task.Factory.StartNew(
            ConsumeLoopAsync,
            CancellationToken.None,
            taskOptions,
            TaskScheduler.Default).Unwrap();
        _retryEngine.Start();
    }

    internal static Channel<TCommand> CreateInputChannel(ActorBackpressure? backpressure) {
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

    internal static Channel<TOut> CreateOutputChannel(int? capacity, BoundedChannelFullMode fullMode) {
        var cap = capacity ?? DefaultChannelCapacity;
        return Channel.CreateBounded<TOut>(new BoundedChannelOptions(cap) {
            FullMode = fullMode,
            SingleReader = true,
            SingleWriter = false
        });
    }

    /// <summary>Actor 唯一标识 — 用于日志和监控</summary>
    public string Id { get; }

    /// <summary>
    /// Consumer 任务 — 用于等待 Consumer 退出(Dispose 时)或观察异常。
    /// </summary>
    protected internal Task ConsumerTask => _consumerTask;

    /// <summary>当前输入邮箱消息总数 — 输入通道 + 重试队列(P0-B: 统一用 Channel 原生 Count,废弃 Interlocked 计数器)</summary>
    public int InputCount => _inputChannel.Reader.Count + _retryEngine.RetryQueueCount;

    /// <summary>输入通道内消息数(不含重试队列)</summary>
    public int InputChannelCount => _inputChannel.Reader.Count;

    /// <summary>重试队列消息数(入队失败正在重试投递的消息)</summary>
    public int RetryQueueCount => _retryEngine.RetryQueueCount;

    /// <summary>当前输出通道消息数 — 用于监控堆积</summary>
    public int OutputCount => _outputChannel.Reader.Count;

    /// <summary>Actor 是否忙碌 — 输入队列有待处理消息 或 输出队列有待消费消息(P2-2: 监控指标)</summary>
    public bool IsBusy => InputCount > 0 || OutputCount > 0;

    /// <summary>输入是否达到高水位线</summary>
    public bool IsInputHighWatermark => CheckHighWatermark(_backpressure, InputCount);

    /// <summary>输入是否达到危险水位线</summary>
    public bool IsInputCriticalWatermark => CheckCriticalWatermark(_backpressure, InputCount);

    /// <summary>判断是否达到高水位线 — 纯函数,不依赖 Actor 状态/时序,供确定性测试</summary>
    /// <param name="bp">背压配置(null=无背压,总返回 false)</param>
    /// <param name="inputCount">当前输入计数</param>
    /// <returns>true=达到高水位线;false=无背压或未达到</returns>
    internal static bool CheckHighWatermark(ActorBackpressure? bp, int inputCount)
        => bp is not null && inputCount >= bp.EffectiveHighWatermark;

    /// <summary>判断是否达到危险水位线 — 纯函数,不依赖 Actor 状态/时序,供确定性测试</summary>
    /// <param name="bp">背压配置(null=无背压,总返回 false)</param>
    /// <param name="inputCount">当前输入计数</param>
    /// <returns>true=达到危险水位线;false=无背压或未达到</returns>
    internal static bool CheckCriticalWatermark(ActorBackpressure? bp, int inputCount)
        => bp is not null && inputCount >= bp.EffectiveCriticalWatermark;

    /// <summary>输入背压水位事件</summary>
    public event EventHandler<BackpressureEventArgs>? InputWatermarkReached;

    /// <summary>背压重试失败事件 — 16次重试后消息仍未能入队时触发(不丢弃,外部可计入死信队列)</summary>
    public event EventHandler<BackpressureSendFailedEventArgs<TCommand>>? SendFailed;

    /// <summary>输出通道满丢弃消息事件 — TryPublish 写入失败时触发(FullMode=DropWrite 且通道满,或通道已完成)</summary>
    public event EventHandler<OutputDroppedEventArgs<TOut>>? OutputMessageDropped;

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

        if (_inputChannel.Writer.TryWrite(cmd)) {
            CheckInputWatermark();
            return;
        }
        if (!_retryEngine.TryEnqueue(new RetryEntry<TCommand>(cmd, 1))) {
            _logger?.LogWarning("[Actor:{ActorId}] 重试队列满,首次入队失败,触发SendFailed", Id);
            RaiseSendFailed(cmd, 0);
        }
    }

    /// <summary>触发 SendFailed 事件 — 逐个调用handler,一个异常不阻断其余(P0-C: 事件多播异常隔离)</summary>
    private void RaiseSendFailed(TCommand command, int retryCount) {
        RaiseEvent(SendFailed, this, new BackpressureSendFailedEventArgs<TCommand>(command, retryCount), "SendFailed");
    }

    /// <summary>安全触发事件 — 逐个调用handler,每个独立try-catch,一个handler异常不影响其余</summary>
    private void RaiseEvent<TArgs>(EventHandler<TArgs>? handler, object sender, TArgs args, string eventName) {
        if (handler is null) return;
        foreach (var h in handler.GetInvocationList()) {
            try {
                ((EventHandler<TArgs>)h)(sender, args);
            } catch (Exception ex) {
                _logger?.LogWarning(ex, "[Actor:{ActorId}] {EventName} 订阅者异常忽略", Id, eventName);
            }
        }
    }

    /// <summary>
    /// 尝试将重试条目回写到重试队列 — 超过最大重试次数或重试队列满时触发 SendFailed。
    /// 返回 true 表示成功回写,false 表示失败(已触发 SendFailed)。
    /// </summary>
    /// <param name="entry">待回写的重试条目</param>
    internal bool TryRequeueRetryEntry(RetryEntry<TCommand> entry) => _retryEngine.TryRequeue(entry);

    /// <summary>重试队列 — 供测试直接写入构造边界条件(内部接口,转发至 MessageRetryEngine)</summary>
    internal Channel<RetryEntry<TCommand>> RetryQueueInternal => _retryEngine.RetryQueue;

    /// <summary>
    /// 接收远程背压信号 — 写入延迟信号队列,供重试时消费
    /// </summary>
    /// <param name="suggestedDelay">建议延迟时间</param>
    public void ReceiveBackpressureSignal(TimeSpan suggestedDelay) => _retryEngine.ReceiveBackpressureSignal(suggestedDelay);

    /// <summary>
    /// 向 Actor 同步尝试发送命令 — Tell 模式(发消息即走)，<b>保证严格 FIFO</b>。
    /// <para>通道已关闭、已释放或(有界通道)已满时返回 false。</para>
    /// <para><b>⚠️ Dispose 路径首选</b>:DisposeAsync 中用 TrySend 发清理命令,不阻塞等待 Consumer,避免线程池饥饿死锁。</para>
    /// <para><b>与 <see cref="Tell"/> 区别</b>:Tell 入队失败进重试队列(不保证FIFO);TrySend 入队失败返回false(保证FIFO,调用方自行处理)。</para>
    /// </summary>
    /// <param name="cmd">命令实例</param>
    /// <returns>true 表示已入队,false 表示未入队</returns>
    public bool TrySend(TCommand cmd) {
        if (Volatile.Read(ref _disposed) != 0) return false;
        var written = _inputChannel.Writer.TryWrite(cmd);
        if (written) {
            CheckInputWatermark();
        }
        return written;
    }

    /// <summary>
    /// TrySend 的语义别名 — 强调严格 FIFO 保证(P1-4)。
    /// <para>入队失败直接返回 false,不进重试队列,不丢消息(调用方自行重试或计入死信)。</para>
    /// <para>适合需要严格命令顺序的场景(如状态机、事务序列)。</para>
    /// </summary>
    /// <param name="cmd">命令实例</param>
    /// <returns>true 表示已入队,false 表示未入队(通道满或已释放)</returns>
    public bool TryTell(TCommand cmd) => TrySend(cmd);

    /// <summary>
    /// Actor 主动推送消息到输出 Channel — 外部通过 OutputAsync 拉取。
    /// <para>输出通道满(FullMode=DropWrite)或通道已完成时返回 false 并触发 <see cref="OutputMessageDropped"/> 事件。</para>
    /// <para>FullMode=DropOldest 时 TryWrite 总返回 true(丢弃最老消息腾位置,静默不触发事件)。</para>
    /// </summary>
    /// <param name="msg">输出消息</param>
    /// <returns>true 表示已写入,false 表示被丢弃(通道满或已完成)</returns>
    protected bool TryPublish(TOut msg) {
        if (Volatile.Read(ref _disposed) != 0) return false;
        var ok = _outputChannel.Writer.TryWrite(msg);
        if (!ok) {
            RaiseEvent(OutputMessageDropped, this, new OutputDroppedEventArgs<TOut>(msg), nameof(OutputMessageDropped));
            _logger?.LogWarning("[Actor:{ActorId}] TryPublish 消息被丢弃", Id);
        }
        return ok;
    }

    /// <summary>TryPublish 内部接口 — 供测试直接调用,不依赖 Consumer 调度时序</summary>
    internal bool TryPublishInternal(TOut msg) => TryPublish(msg);

    /// <summary>
    /// 注册 in-flight 任务 — 子类 Handle 里 fire-and-forget 启动的任务应通过此方法注册。
    /// <para>DisposeAsync 在 Consumer 退出后自动等待所有注册的 in-flight 任务完成,确保资源真正释放。</para>
    /// <para>ADR: [0125](docs/adr/0125-actor-register-inflight-dispose-guard.md) — 统一 in-flight 守卫基础设施。</para>
    /// </summary>
    /// <param name="task">fire-and-forget 启动的任务引用</param>
    protected void RegisterInFlight(Task task) {
        ThrowIfDisposed();
        _inFlightTasks.Add(task);
    }

    /// <summary>
    /// 外部拉取输出流 — 阻塞式 IAsyncEnumerable。
    /// </summary>
    public async IAsyncEnumerable<TOut> OutputAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
        await foreach (var item in _outputChannel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false)) {
            yield return item;
        }
    }

    private void CheckInputWatermark() {
        if (_watermarkMonitor is null) return;
        if (_watermarkMonitor.Check(InputCount, GetType().Name, out var args) && args is not null) {
            RaiseEvent(InputWatermarkReached, this, args, "InputWatermarkReached");
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
    protected IIdempotencyStore? IdempotencyStore { get; private set; }

    private async Task ConsumeLoopAsync() {
        using var actorScope = AsyncFlowIdentity.EnterActorScope(Id);
        try {
            await foreach (var cmd in _inputChannel.Reader.ReadAllAsync(_cts.Token).ConfigureAwait(false)) {
                if (!ProcessSingleCommand(cmd, _cts.Token)) return;
            }
        } catch (OperationCanceledException) { }
    }

    /// <summary>
    /// 处理单条命令 — 幂等守卫+Handle+异常处理,供测试直接调用(不依赖 Consumer 调度时序)。
    /// <para>确定性验证入口:测试直接调用此方法处理单条命令,无需启动 Consumer 循环/无需 Tell+等待调度。</para>
    /// <para>时序分离:ConsumeLoopAsync 只负责循环编排(时序),本方法负责单条命令处理(确定性)。</para>
    /// </summary>
    /// <param name="cmd">待处理命令</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>true=已处理(或幂等跳过);false=Actor 关闭需退出循环</returns>
    internal bool ProcessSingleCommand(TCommand cmd, CancellationToken ct) {
        CheckInputWatermark();
        try {
            if (_idempotencyGate.TryRestore(cmd)) {
                return true;
            }
            Handle(cmd, ct);
        } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
            return false;
        } catch (Exception ex) {
            HandleConsumerErrorSafe(ex);
        }
        return true;
    }

    /// <summary>安全调用 OnConsumerError — 回调异常吞掉记日志(不传播,不中断 Consumer)</summary>
    private void HandleConsumerErrorSafe(Exception ex) {
        try {
            OnConsumerError(ex);
        } catch (Exception innerEx) {
            _logger?.LogError(innerEx, "[Actor:{ActorId}] OnConsumerError 异常忽略", Id);
        }
    }

    /// <summary>判断背压信号是否需要延迟重试 — 纯函数,不依赖 Actor 状态/时序,供确定性测试</summary>
    /// <param name="suggestedDelay">背压信号建议延迟</param>
    /// <returns>true=需要延迟(正延迟);false=立即重试(零或负延迟)</returns>
    internal static bool ShouldDelayRetry(TimeSpan suggestedDelay) => suggestedDelay > TimeSpan.Zero;

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
    /// <param name="ct">取消令牌(Actor 释放时取消延迟重试,避免 Dispose 后残留后台任务)</param>
    /// <returns>标准背压回调,可直接作为命令的 OnBackpressure 参数</returns>
    public static Action<BackpressureSignal> CreateBackpressureHandler(Action resend, ILogger? logger = null, CancellationToken ct = default) {
        var capturedFlow = AsyncFlowIdentity.Capture();
        var capturedWaitGraph = _askWaitGraph.Value;

        return signal => {
            if (ShouldDelayRetry(signal.SuggestedDelay)) {
                _ = Task.Run(async () => {
                    try {
                        using var flowScope = AsyncFlowIdentity.Restore(capturedFlow);
                        _askWaitGraph.Value = capturedWaitGraph;
                        await Task.Delay(signal.SuggestedDelay, ct).ConfigureAwait(false);
                        resend();
                    } catch (OperationCanceledException) {
                        // Actor 释放,取消延迟重试
                    } catch (Exception ex) {
                        logger?.LogWarning(ex, "CreateBackpressureHandler resend 异常忽略");
                    }
                });
            } else {
                try {
                    using var flowScope = AsyncFlowIdentity.Restore(capturedFlow);
                    _askWaitGraph.Value = capturedWaitGraph;
                    resend();
                } catch (Exception ex) { logger?.LogWarning(ex, "CreateBackpressureHandler resend 异常忽略"); }
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
        } catch (OperationCanceledException) when (IsTimeoutCancellation(ct)) {
            throw CreateAskDeadlockException(GetType().Name, timeoutMs);
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
        } catch (OperationCanceledException) when (IsTimeoutCancellation(ct)) {
            throw CreateAskDeadlockException(GetType().Name, timeoutMs);
        }
    }

    /// <summary>计算指数退避延迟(ms) — 100×2^attempt,上限5000,attempt>10 钳制为10防溢出</summary>
    /// <param name="attempt">当前重试次数(0基)</param>
    /// <returns>退避延迟(ms): attempt=0→100, 1→200, 2→400, ..., ≥10→5000(上限)</returns>
    internal static int ComputeBackoffDelayMs(int attempt) {
        if (attempt < 0) return 100;
        var clamped = Math.Min(attempt, 10);
        return Math.Min(100 * (1 << clamped), 5000);
    }

    /// <summary>计算总超时(ms) — singleTimeoutMs × (maxRetries+1),用于诊断异常消息</summary>
    /// <para>用 long 计算后钳制到 int.MaxValue,防止 int 乘法溢出(如 singleTimeoutMs=int.MaxValue, maxRetries=1)。</para>
    internal static int ComputeTotalTimeoutMs(int singleTimeoutMs, int maxRetries) {
        var total = (long)singleTimeoutMs * ((long)maxRetries + 1);
        return total > int.MaxValue ? int.MaxValue : (int)total;
    }

    /// <summary>判断命令是否幂等 — 实现 IIdempotent 标记接口</summary>
    internal static bool IsIdempotentCommand(TCommand cmd) => cmd is IIdempotent;

    /// <summary>判断取消是否由超时触发(而非外部取消令牌) — OperationCanceledException 捕获时调用</summary>
    /// <param name="externalCt">外部取消令牌(非 linkedCts)</param>
    /// <returns>true=超时触发(外部令牌未取消);false=外部取消触发</returns>
    internal static bool IsTimeoutCancellation(CancellationToken externalCt) => !externalCt.IsCancellationRequested;

    /// <summary>创建 Ask 死锁异常 — 封装诊断信息构造,供 AskAwait/AskWithRetryAsync 统一调用</summary>
    internal static ActorAskDeadlockException CreateAskDeadlockException(string actorName, int timeoutMs)
        => new(actorName, timeoutMs);

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
    /// <param name="allowNonIdempotentRetry">false=非IIdempotent命令重试直接抛异常;true=仅打日志(默认)</param>
    /// <exception cref="ActorAskDeadlockException">重试耗尽仍超时 — 可能线程池饥饿或 Consumer 阻塞</exception>
    /// <exception cref="ActorCyclicAskException">等待图检测到环(含间接环) — 循环 Ask 死锁</exception>
    /// <exception cref="InvalidOperationException">allowNonIdempotentRetry=false 且命令未实现 IIdempotent</exception>
    protected async Task<T> AskWithRetryAsync<T>(
        Func<TaskCompletionSource<T>, TCommand> commandFactory,
        CancellationToken ct = default,
        int singleTimeoutMs = 10_000,
        int maxRetries = 16,
        bool allowNonIdempotentRetry = true) {
        using var waitScope = EnterWaitGraph(TryGetCallerActorId());
        for (var attempt = 0; attempt <= maxRetries; attempt++) {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cmd = commandFactory(tcs);
            var cmdType = cmd?.GetType().Name ?? "null";
            if (!allowNonIdempotentRetry && !IsIdempotentCommand(cmd)) {
                throw new InvalidOperationException($"AskWithRetryAsync: 命令 {cmdType} 未实现 IIdempotent，不允许重试");
            }
            if (!IsIdempotentCommand(cmd)) {
                _logger?.LogWarning("[Actor:{ActorId}] AskWithRetryAsync 使用非幂等命令 {CmdType}, 重试可能产生重复副作用", Id, cmdType);
            }
            if (!TrySend(cmd)) {
                if (attempt >= maxRetries)
                    throw CreateAskDeadlockException(GetType().Name, ComputeTotalTimeoutMs(singleTimeoutMs, maxRetries));
                var delayMs = ComputeBackoffDelayMs(attempt);
                await Task.Delay(delayMs, ct).ConfigureAwait(false);
                continue;
            }
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linkedCts.CancelAfter(singleTimeoutMs);
            try {
                return await tcs.Task.WaitAsync(linkedCts.Token).ConfigureAwait(false);
            } catch (OperationCanceledException) when (IsTimeoutCancellation(ct)) {
                if (attempt >= maxRetries)
                    throw CreateAskDeadlockException(GetType().Name, ComputeTotalTimeoutMs(singleTimeoutMs, maxRetries));
                var delayMs = ComputeBackoffDelayMs(attempt);
                await Task.Delay(delayMs, ct).ConfigureAwait(false);
            }
        }
        throw CreateAskDeadlockException(GetType().Name, ComputeTotalTimeoutMs(singleTimeoutMs, maxRetries));
    }

    /// <summary>
    /// Ask 重试模式等待回复(无返回值)— 内置重试16次+指数退避+全图环检测,非泛型重载。
    /// </summary>
    /// <param name="commandFactory">命令工厂 — 接收新 TCS,返回命令实例</param>
    /// <param name="ct">取消令牌</param>
    /// <param name="singleTimeoutMs">单次超时(默认10s)</param>
    /// <param name="maxRetries">最大重试次数(默认16)</param>
    /// <param name="allowNonIdempotentRetry">false=非IIdempotent命令重试直接抛异常;true=仅打日志(默认)</param>
    protected async Task AskWithRetryAsync(
        Func<TaskCompletionSource, TCommand> commandFactory,
        CancellationToken ct = default,
        int singleTimeoutMs = 10_000,
        int maxRetries = 16,
        bool allowNonIdempotentRetry = true) {
        using var waitScope = EnterWaitGraph(TryGetCallerActorId());
        for (var attempt = 0; attempt <= maxRetries; attempt++) {
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var cmd = commandFactory(tcs);
            var cmdType = cmd?.GetType().Name ?? "null";
            if (!allowNonIdempotentRetry && !IsIdempotentCommand(cmd)) {
                throw new InvalidOperationException($"AskWithRetryAsync: 命令 {cmdType} 未实现 IIdempotent，不允许重试");
            }
            if (!IsIdempotentCommand(cmd)) {
                _logger?.LogWarning("[Actor:{ActorId}] AskWithRetryAsync 使用非幂等命令 {CmdType}, 重试可能产生重复副作用", Id, cmdType);
            }
            if (!TrySend(cmd)) {
                if (attempt >= maxRetries)
                    throw CreateAskDeadlockException(GetType().Name, ComputeTotalTimeoutMs(singleTimeoutMs, maxRetries));
                var delayMs = ComputeBackoffDelayMs(attempt);
                await Task.Delay(delayMs, ct).ConfigureAwait(false);
                continue;
            }
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linkedCts.CancelAfter(singleTimeoutMs);
            try {
                await tcs.Task.WaitAsync(linkedCts.Token).ConfigureAwait(false);
                return;
            } catch (OperationCanceledException) when (IsTimeoutCancellation(ct)) {
                if (attempt >= maxRetries)
                    throw CreateAskDeadlockException(GetType().Name, ComputeTotalTimeoutMs(singleTimeoutMs, maxRetries));
                var delayMs = ComputeBackoffDelayMs(attempt);
                await Task.Delay(delayMs, ct).ConfigureAwait(false);
            }
        }
        throw CreateAskDeadlockException(GetType().Name, ComputeTotalTimeoutMs(singleTimeoutMs, maxRetries));
    }

    /// <summary>
    /// 进入等待图作用域 — 在当前异步流的调用链本地图加边 callerId→Id,检测环,返回 scope(Dispose 恢复父图)。
    /// <para>等待图用 AsyncLocal 存储调用链本地图,避免全局静态图跨调用链污染/并发覆盖(Bug1 修复)。</para>
    /// <para>每次创建新 ImmutableDag 副本(从父图无锁快照复制),各异步流独立不竞态(P1-3 修复)。</para>
    /// <para>callerId 为 null 或等于自身时返回 null(无需加边)。</para>
    /// <para><b>⚠️ AsyncLocal 局限</b>:等待图依赖 AsyncLocal 上下文流动,跨裸线程/Task.Run 调用会丢失上下文,</para>
    /// <para>等待图断裂无法检测跨线程循环 Ask 死锁。Ask 调用链必须在同一异步流上下文内调用。</para>
    /// </summary>
    private AskWaitGraphTracker.WaitGraphScope? EnterWaitGraph(string? callerId) {
        return _waitGraphTracker.EnterScope(callerId, Id);
    }

    private string? TryGetCallerActorId() => AsyncFlowIdentity.CurrentActorId;

    /// <summary>
    /// 释放 Actor — 取消 Consumer、完成通道,等待 Consumer 真正退出后等待所有 in-flight 任务,最后释放 CTS。
    /// <para>Consumer 用 LongRunning 专用线程运行(不占线程池),Dispose await 不会导致线程池饥饿死锁。</para>
    /// <para>设计理由:fire-and-forget 会掩盖 Consumer 未完成清理的问题,改回 await 确保资源真正释放。</para>
    /// <para>in-flight 守卫:子类 Handle 里通过 <see cref="RegisterInFlight"/> 注册的任务,在 Consumer 退出后统一等待(ADR 0125)。</para>
    /// </summary>
    public virtual async ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _cts.Cancel();
        _inputChannel.Writer.TryComplete();
        _outputChannel.Writer.TryComplete();
        _retryEngine.Complete();
        try {
            await _consumerTask.ConfigureAwait(false);
        } catch (OperationCanceledException) { }
        try {
            await _retryEngine.WaitForCompletionAsync().ConfigureAwait(false);
        } catch (OperationCanceledException) { }
        var inflight = _inFlightTasks.ToArray();
        if (inflight.Length > 0) {
            await Task.WhenAll(inflight).ConfigureAwait(false);
        }
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

/// <summary>
/// 输出消息丢弃事件参数 — 输出通道满时 TryPublish 丢弃的消息
/// </summary>
/// <typeparam name="TOut">输出消息类型</typeparam>
/// <param name="Message">被丢弃的消息(外部可计入死信队列或重投)</param>
public sealed record OutputDroppedEventArgs<TOut>(TOut Message);
