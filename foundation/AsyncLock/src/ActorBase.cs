namespace Core.Utils;

/// <summary>
/// Actor 基类 — 单消费者 Channel + 命令处理。
/// <para>派生类定义命令类型并实现 <see cref="HandleAsync"/>,所有可变状态由 Consumer 线程独占访问,无需锁。</para>
/// <para>线程安全保证:命令按 FIFO 顺序串行处理;多生产者通过 <see cref="SendAsync"/>/<see cref="TrySend"/> 投递。</para>
/// <para>异常容错:单条命令异常不会终止 Consumer 循环,通过 <see cref="OnConsumerError"/> 回调通知子类。</para>
/// </summary>
/// <typeparam name="TCommand">命令类型 — 建议用 record 或 sealed class,实现标记接口以约束合法命令</typeparam>
public abstract class ActorBase<TCommand> : IAsyncDisposable
{
    private readonly Channel<TCommand> _channel;
    private readonly Task _consumerTask;
    private readonly CancellationTokenSource _cts = new();
    private int _disposed;

    /// <summary>
    /// 构造 Actor — 启动后台 Consumer 循环消费命令通道。
    /// </summary>
    /// <param name="boundedCapacity">有界通道容量(null 为无界通道)。有界通道在满时按 <paramref name="fullMode"/> 策略处理。</param>
    /// <param name="fullMode">有界通道满时策略(默认 <see cref="BoundedChannelFullMode.Wait"/> 阻塞生产者)。</param>
    protected ActorBase(int? boundedCapacity = null, BoundedChannelFullMode fullMode = BoundedChannelFullMode.Wait)
    {
        if (boundedCapacity is null)
        {
            _channel = Channel.CreateUnbounded<TCommand>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });
        }
        else
        {
            _channel = Channel.CreateBounded<TCommand>(new BoundedChannelOptions(boundedCapacity.Value)
            {
                FullMode = fullMode,
                SingleReader = true,
                SingleWriter = false
            });
        }
        _consumerTask = Task.Run(ConsumeLoopAsync);
    }

    /// <summary>
    /// Consumer 任务 — 用于等待 Consumer 退出(Dispose 时)或观察异常。
    /// </summary>
    protected internal Task ConsumerTask => _consumerTask;

    /// <summary>
    /// 向 Actor 异步发送命令 — 无界通道立即返回,有界通道在满时背压等待。
    /// </summary>
    /// <param name="cmd">命令实例</param>
    /// <param name="ct">取消令牌</param>
    /// <exception cref="ObjectDisposedException">Actor 已释放</exception>
    protected ValueTask SendAsync(TCommand cmd, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        return _channel.Writer.WriteAsync(cmd, ct);
    }

    /// <summary>
    /// 向 Actor 同步尝试发送命令 — 通道已关闭、已释放或(有界通道)已满时返回 false。
    /// </summary>
    /// <param name="cmd">命令实例</param>
    /// <returns>true 表示已入队,false 表示未入队</returns>
    protected bool TrySend(TCommand cmd)
    {
        if (Volatile.Read(ref _disposed) != 0) return false;
        return _channel.Writer.TryWrite(cmd);
    }

    /// <summary>
    /// 子类实现命令处理逻辑 — 由 Consumer 线程串行调用,此方法内访问实例可变状态无需锁。
    /// </summary>
    /// <param name="command">待处理命令</param>
    /// <param name="ct">取消令牌(Actor 释放时触发取消)</param>
    protected abstract ValueTask HandleAsync(TCommand command, CancellationToken ct);

    /// <summary>
    /// Consumer 处理单条命令异常的回调 — 默认忽略,子类可重写以记录日志或计数。
    /// <para>此方法在 Consumer 线程内调用,不应抛异常(抛出会被吞掉)。</para>
    /// </summary>
    /// <param name="ex">命令处理异常</param>
    protected virtual void OnConsumerError(Exception ex) { }

    /// <summary>
    /// Consumer 循环 — 从通道读取命令并调用 <see cref="HandleAsync"/>。单条异常不退出。
    /// </summary>
    private async Task ConsumeLoopAsync()
    {
        try
        {
            await foreach (var cmd in _channel.Reader.ReadAllAsync(_cts.Token).ConfigureAwait(false))
            {
                try
                {
                    await HandleAsync(cmd, _cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_cts.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    OnConsumerError(ex);
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(GetType().Name);
    }

    /// <summary>
    /// 释放 Actor — 取消 Consumer、完成通道写入、等待 Consumer 退出。
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        _cts.Cancel();
        _channel.Writer.TryComplete();
        try
        {
            await _consumerTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            OnConsumerError(ex);
        }
        _cts.Dispose();
    }
}
