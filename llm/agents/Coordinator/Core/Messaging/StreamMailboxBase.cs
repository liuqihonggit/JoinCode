namespace Core.Agents.Coordinator;

/// <summary>
/// 流式邮箱基类 — 后台接收循环的模板方法基类。
/// <para>继承 <see cref="MailboxBase{TMessage}"/>，复用全部双工+背压+水位线+超时能力。</para>
/// <para>接收循环：后台 Task 从传输层读取帧序列，逐帧交给子类处理，外层异常统一捕获日志。</para>
/// <para>子类实现 <see cref="ReceiveFramesAsync"/> + <see cref="HandleFrameAsync"/> + <see cref="LogReceiveLoopError"/> 即可获得完整接收循环。</para>
/// <para>生命周期：内部 <see cref="CancellationTokenSource"/> 管理循环取消，<see cref="StopReceiveLoopAsync"/> 在 Dispose 中调用。</para>
/// <para>并发安全：无锁，靠后台 Task 单线程读取 + Channel 消息传递。</para>
/// </summary>
/// <typeparam name="TMessage">邮箱消息类型</typeparam>
/// <typeparam name="TFrame">传输帧类型 — 由子类从传输层读取的原始帧</typeparam>
public abstract class StreamMailboxBase<TMessage, TFrame> : MailboxBase<TMessage> {
    private readonly CancellationTokenSource _cts;
    private Task? _receiveLoopTask;
    private int _started;
    private int _disposed;

    /// <summary>
    /// 构造流式邮箱基类。
    /// </summary>
    /// <param name="commandBackpressure">命令通道背压</param>
    /// <param name="agentBackpressure">Agent 消息通道背压</param>
    /// <param name="outputCapacity">事件输出通道容量</param>
    protected StreamMailboxBase(
        ActorBackpressure? commandBackpressure = null,
        ActorBackpressure? agentBackpressure = null,
        int? outputCapacity = null)
        : base(commandBackpressure, agentBackpressure, outputCapacity) {
        _cts = new CancellationTokenSource();
    }

    /// <summary>
    /// 接收循环使用的取消令牌 — 子类可用于其他需要随循环取消的操作。
    /// </summary>
    protected CancellationToken ReceiveLoopToken => _cts.Token;

    /// <summary>
    /// 启动接收循环 — 子类在 <c>StartAsync</c> 中调用一次。
    /// <para>幂等：重复调用无效。</para>
    /// </summary>
    protected void StartReceiveLoop() {
        if (Interlocked.Exchange(ref _started, 1) != 0) return;
        _receiveLoopTask = Task.Run(() => ReceiveLoopAsync(_cts.Token), CancellationToken.None);
    }

    /// <summary>
    /// 停止接收循环 — 子类在 <c>DisposeAsync</c> 中调用一次。
    /// <para>取消内部令牌 + 等待循环退出 + 释放令牌源。</para>
    /// <para>无超时等待 — 符合 AGENTS.md 规则4（释放函数禁止超时）。</para>
    /// <para>幂等：重复调用无效。</para>
    /// </summary>
    protected async ValueTask StopReceiveLoopAsync() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _cts.Cancel();
        if (_receiveLoopTask is not null) {
            try { await _receiveLoopTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }
        _cts.Dispose();
    }

    /// <summary>
    /// 接收循环骨架 — 从子类读取帧序列，逐帧处理，外层异常统一捕获。
    /// <para>取消异常静默吞掉，其他异常交给 <see cref="LogReceiveLoopError"/> 日志。</para>
    /// </summary>
    /// <param name="ct">取消令牌</param>
    private async Task ReceiveLoopAsync(CancellationToken ct) {
        try {
            await foreach (var frame in ReceiveFramesAsync(ct).ConfigureAwait(false)) {
                await HandleFrameAsync(frame, ct).ConfigureAwait(false);
            }
        } catch (OperationCanceledException) { } catch (Exception ex) {
            LogReceiveLoopError(ex);
        }
    }

    /// <summary>
    /// 子类实现 — 从传输层读取帧序列（异步枚举）。
    /// <para>循环骨架会逐帧调用 <see cref="HandleFrameAsync"/> 处理。</para>
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>帧异步枚举流</returns>
    protected abstract IAsyncEnumerable<TFrame> ReceiveFramesAsync(CancellationToken ct);

    /// <summary>
    /// 子类实现 — 处理单帧（反序列化/转换 + 投递到本地 Agent Channel）。
    /// <para>单帧处理异常由子类自行捕获（如 JSON 反序列化失败），避免单帧错误终止整个循环。</para>
    /// </summary>
    /// <param name="frame">传输帧</param>
    /// <param name="ct">取消令牌</param>
    protected abstract ValueTask HandleFrameAsync(TFrame frame, CancellationToken ct);

    /// <summary>
    /// 子类实现 — 日志接收循环错误（非取消异常）。
    /// </summary>
    /// <param name="ex">异常</param>
    protected abstract void LogReceiveLoopError(Exception ex);
}