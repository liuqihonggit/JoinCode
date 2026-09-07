namespace Core.Utils;

/// <summary>
/// Actor 基类 — 单消费者 Channel + 命令处理。
/// <para>派生类定义命令类型并实现 <see cref="HandleAsync"/>,所有可变状态由 Consumer 线程独占访问,无需锁。</para>
/// <para>线程安全保证:命令按 FIFO 顺序串行处理;多生产者通过 <see cref="SendAsync"/>/<see cref="TrySend"/> 投递。</para>
/// <para>异常容错:单条命令异常不会终止 Consumer 循环,通过 <see cref="OnConsumerError"/> 回调通知子类。</para>
/// <para>背压:通过 <see cref="ActorBackpressure"/> 配置有界容量、水位线告警、发送超时,防止 OOM 和永久阻塞。</para>
/// </summary>
/// <typeparam name="TCommand">命令类型 — 建议用 record 或 sealed class,实现标记接口以约束合法命令</typeparam>
public abstract class ActorBase<TCommand> : IAsyncDisposable
{
    private readonly Channel<TCommand> _channel;
    private readonly Task _consumerTask;
    private readonly CancellationTokenSource _cts = new();
    private readonly ActorBackpressure? _backpressure;
    private int _disposed;

    /// <summary>
    /// 构造 Actor — 启动后台 Consumer 循环消费命令通道。
    /// </summary>
    /// <param name="boundedCapacity">有界通道容量(null 为无界通道)。有界通道在满时按 <paramref name="fullMode"/> 策略处理。</param>
    /// <param name="fullMode">有界通道满时策略(默认 <see cref="BoundedChannelFullMode.Wait"/> 阻塞生产者)。</param>
    protected ActorBase(int? boundedCapacity = null, BoundedChannelFullMode fullMode = BoundedChannelFullMode.Wait)
        : this(boundedCapacity is null ? null : new ActorBackpressure(boundedCapacity.Value, fullMode))
    {
    }

    /// <summary>
    /// 构造 Actor — 使用完整背压配置。
    /// </summary>
    /// <param name="backpressure">背压配置(null=无界通道,无水位线,无超时)</param>
    protected ActorBase(ActorBackpressure? backpressure)
    {
        _backpressure = backpressure;
        if (backpressure is null || backpressure.Capacity == 0)
        {
            _channel = Channel.CreateUnbounded<TCommand>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });
        }
        else
        {
            _channel = Channel.CreateBounded<TCommand>(new BoundedChannelOptions(backpressure.Capacity)
            {
                FullMode = backpressure.FullMode,
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

    /// <summary>当前邮箱消息数 — 用于监控背压状态</summary>
    public int MailboxCount => _channel.Reader.Count;

    /// <summary>是否达到高水位线 — 生产者可检查后降速(无背压配置时永远 false)</summary>
    public bool IsHighWatermark => _backpressure is not null
        && MailboxCount >= _backpressure.EffectiveHighWatermark;

    /// <summary>是否达到危险水位线 — 即将满,生产者应停止发送(无背压配置时永远 false)</summary>
    public bool IsCriticalWatermark => _backpressure is not null
        && MailboxCount >= _backpressure.EffectiveCriticalWatermark;

    /// <summary>背压水位事件 — 达到 High/Critical 水位线时触发,生产者可订阅降速</summary>
    public event EventHandler<BackpressureEventArgs>? WatermarkReached;

    /// <summary>
    /// 向 Actor 异步发送命令 — 无界通道立即返回,有界通道在满时背压等待。
    /// <para>配置了 <see cref="ActorBackpressure.SendTimeout"/> 时,超时抛 <see cref="TimeoutException"/>。</para>
    /// </summary>
    /// <param name="cmd">命令实例</param>
    /// <param name="ct">取消令牌</param>
    /// <exception cref="ObjectDisposedException">Actor 已释放</exception>
    /// <exception cref="TimeoutException">发送超时(背压配置了 SendTimeout 且通道满)</exception>
    protected async ValueTask SendAsync(TCommand cmd, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        CheckWatermark();

        if (_backpressure?.SendTimeout is { } timeout)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linkedCts.CancelAfter(timeout);
            try
            {
                await _channel.Writer.WriteAsync(cmd, linkedCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"Actor {GetType().Name} 发送超时({timeout.TotalSeconds:F0}s),邮箱可能已满({MailboxCount}/{_backpressure.Capacity})");
            }
        }
        else
        {
            await _channel.Writer.WriteAsync(cmd, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 向 Actor 同步尝试发送命令 — 通道已关闭、已释放或(有界通道)已满时返回 false。
    /// </summary>
    /// <param name="cmd">命令实例</param>
    /// <returns>true 表示已入队,false 表示未入队</returns>
    protected bool TrySend(TCommand cmd)
    {
        if (Volatile.Read(ref _disposed) != 0) return false;
        var written = _channel.Writer.TryWrite(cmd);
        if (written) CheckWatermark();
        return written;
    }

    /// <summary>检查水位线并触发事件 — 入队后调用</summary>
    private void CheckWatermark()
    {
        if (_backpressure is null) return;
        var count = MailboxCount;
        var level = count >= _backpressure.EffectiveCriticalWatermark ? WatermarkLevel.Critical
                   : count >= _backpressure.EffectiveHighWatermark ? WatermarkLevel.High
                   : WatermarkLevel.Normal;
        if (level != WatermarkLevel.Normal)
        {
            WatermarkReached?.Invoke(this, new BackpressureEventArgs(
                GetType().Name, count, _backpressure.Capacity, level));
        }
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
