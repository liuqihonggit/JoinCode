namespace Core.Utils;

/// <summary>
/// 全双工 Actor 基类 — 输入 Channel + 输出 Channel。
/// <para>外部通过 SendAsync 发送命令，通过 OutputAsync 拉取输出。</para>
/// <para>Actor 通过 TryPublish 主动推送消息，无需等待外部请求。</para>
/// <para>输入 Channel 有背压（有界 + 水位线 + 超时），输出 Channel 无背压（无界）。</para>
/// <para>派生类定义命令类型并实现 <see cref="HandleAsync"/>,所有可变状态由 Consumer 线程独占访问,无需锁。</para>
/// <para>线程安全保证:命令按 FIFO 顺序串行处理;多生产者通过 SendAsync/TrySend 投递。</para>
/// <para>异常容错:单条命令异常不会终止 Consumer 循环,通过 OnConsumerError 回调通知子类。</para>
/// <para>背压:通过 <see cref="ActorBackpressure"/> 配置有界容量、水位线告警、发送超时,防止 OOM 和永久阻塞。</para>
/// </summary>
/// <typeparam name="TCommand">命令类型 — 建议用 record 或 sealed class,实现标记接口以约束合法命令</typeparam>
/// <typeparam name="TOut">输出消息类型 — 建议用 record 或 sealed class</typeparam>
public abstract class ActorBase<TCommand, TOut> : IActor<TCommand>, IAsyncDisposable
{
    private readonly Channel<TCommand> _inputChannel;
    private readonly Channel<TOut> _outputChannel;
    private readonly Task _consumerTask;
    private readonly CancellationTokenSource _cts = new();
    private readonly ActorBackpressure? _backpressure;
    private int _disposed;
    private int _inputCount;
    private int _outputCount;

    /// <summary>
    /// 构造 Actor — 无界输入通道，无界输出通道。
    /// </summary>
    protected ActorBase()
        : this(null, null)
    {
    }

    /// <summary>
    /// 构造 Actor — 有界输入通道，无界输出通道。
    /// </summary>
    /// <param name="boundedCapacity">有界输入通道容量(null 为无界)</param>
    /// <param name="fullMode">有界通道满时策略</param>
    protected ActorBase(int? boundedCapacity, BoundedChannelFullMode fullMode = BoundedChannelFullMode.Wait)
        : this(boundedCapacity is null ? null : new ActorBackpressure(boundedCapacity.Value, fullMode), null)
    {
    }

    /// <summary>
    /// 构造 Actor — 完整背压配置。
    /// </summary>
    /// <param name="backpressure">输入背压配置(null=无界通道,无水位线,无超时)</param>
    /// <param name="outputCapacity">输出通道容量(null=无界)</param>
    protected ActorBase(ActorBackpressure? backpressure = null, int? outputCapacity = null)
    {
        Id = $"{GetType().Name}-{Guid.NewGuid():N}"[..8];
        _backpressure = backpressure;
        _inputChannel = CreateInputChannel(backpressure);
        _outputChannel = CreateOutputChannel(outputCapacity);
        _consumerTask = Task.Run(ConsumeLoopAsync);
    }

    private static Channel<TCommand> CreateInputChannel(ActorBackpressure? backpressure)
    {
        if (backpressure is null || backpressure.Capacity == 0)
        {
            return Channel.CreateUnbounded<TCommand>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });
        }

        return Channel.CreateBounded<TCommand>(new BoundedChannelOptions(backpressure.Capacity)
        {
            FullMode = backpressure.FullMode,
            SingleReader = true,
            SingleWriter = false
        });
    }

    private static Channel<TOut> CreateOutputChannel(int? capacity)
    {
        if (capacity is null || capacity == 0)
        {
            return Channel.CreateUnbounded<TOut>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true
            });
        }

        return Channel.CreateBounded<TOut>(new BoundedChannelOptions(capacity.Value)
        {
            FullMode = BoundedChannelFullMode.Wait,
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

    /// <summary>当前输入邮箱消息数 — 用于监控背压状态</summary>
    public int InputCount => Volatile.Read(ref _inputCount);

    /// <summary>当前输出通道消息数 — 用于监控堆积</summary>
    public int OutputCount => Volatile.Read(ref _outputCount);

    /// <summary>输入是否达到高水位线</summary>
    public bool IsInputHighWatermark => _backpressure is not null
        && InputCount >= _backpressure.EffectiveHighWatermark;

    /// <summary>输入是否达到危险水位线</summary>
    public bool IsInputCriticalWatermark => _backpressure is not null
        && InputCount >= _backpressure.EffectiveCriticalWatermark;

    /// <summary>输入背压水位事件</summary>
    public event EventHandler<BackpressureEventArgs>? InputWatermarkReached;

    /// <summary>
    /// 向 Actor 异步发送命令 — 无界通道立即返回,有界通道在满时背压等待。
    /// <para>配置了 <see cref="ActorBackpressure.SendTimeout"/> 时,超时抛 <see cref="TimeoutException"/>。</para>
    /// </summary>
    /// <param name="cmd">命令实例</param>
    /// <param name="ct">取消令牌</param>
    /// <exception cref="ObjectDisposedException">Actor 已释放</exception>
    /// <exception cref="TimeoutException">发送超时(背压配置了 SendTimeout 且通道满)</exception>
    public async ValueTask SendAsync(TCommand cmd, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        CheckInputWatermark();

        if (_backpressure?.SendTimeout is { } timeout)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linkedCts.CancelAfter(timeout);
            try
            {
                await _inputChannel.Writer.WriteAsync(cmd, linkedCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"Actor {GetType().Name} 发送超时({timeout.TotalSeconds:F0}s),邮箱可能已满({InputCount}/{_backpressure.Capacity})");
            }
        }
        else
        {
            await _inputChannel.Writer.WriteAsync(cmd, ct).ConfigureAwait(false);
        }
        Interlocked.Increment(ref _inputCount);
    }

    /// <summary>
    /// 向 Actor 同步尝试发送命令 — 通道已关闭、已释放或(有界通道)已满时返回 false。
    /// </summary>
    /// <param name="cmd">命令实例</param>
    /// <returns>true 表示已入队,false 表示未入队</returns>
    public bool TrySend(TCommand cmd)
    {
        if (Volatile.Read(ref _disposed) != 0) return false;
        var written = _inputChannel.Writer.TryWrite(cmd);
        if (written)
        {
            Interlocked.Increment(ref _inputCount);
            CheckInputWatermark();
        }
        return written;
    }

    /// <summary>
    /// Actor 主动推送消息到输出 Channel — 外部通过 OutputAsync 拉取。
    /// </summary>
    protected bool TryPublish(TOut msg)
    {
        if (Volatile.Read(ref _disposed) != 0) return false;
        if (_outputChannel.Writer.TryWrite(msg))
        {
            Interlocked.Increment(ref _outputCount);
            return true;
        }
        return false;
    }

    /// <summary>
    /// 外部拉取输出流 — 阻塞式 IAsyncEnumerable。
    /// </summary>
    public async IAsyncEnumerable<TOut> OutputAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in _outputChannel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            Interlocked.Decrement(ref _outputCount);
            yield return item;
        }
    }

    private void CheckInputWatermark()
    {
        if (_backpressure is null) return;
        var count = InputCount;
        var level = count >= _backpressure.EffectiveCriticalWatermark ? WatermarkLevel.Critical
                   : count >= _backpressure.EffectiveHighWatermark ? WatermarkLevel.High
                   : WatermarkLevel.Normal;
        if (level != WatermarkLevel.Normal)
        {
            InputWatermarkReached?.Invoke(this, new BackpressureEventArgs(
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

    private async Task ConsumeLoopAsync()
    {
        try
        {
            await foreach (var cmd in _inputChannel.Reader.ReadAllAsync(_cts.Token).ConfigureAwait(false))
            {
                Interlocked.Decrement(ref _inputCount);
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
    /// 释放 Actor — 取消 Consumer、完成输入通道、等待 Consumer 退出。
    /// </summary>
    public virtual async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        _cts.Cancel();
        _inputChannel.Writer.TryComplete();
        _outputChannel.Writer.TryComplete();
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

/// <summary>
/// 单元类型 — 用于不需要输出的 Actor 的 TOut 参数。
/// </summary>
public readonly record struct Unit
{
    /// <summary>唯一实例</summary>
    public static readonly Unit Value = default;
}
