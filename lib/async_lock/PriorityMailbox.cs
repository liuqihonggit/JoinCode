namespace Core.Utils;

/// <summary>
/// 消息优先级 — 多通道优先级邮箱用。
/// </summary>
public enum MessagePriority
{
    /// <summary>高优先级 — 用户交互,立即处理</summary>
    High,

    /// <summary>普通优先级 — LLM 请求等</summary>
    Normal,

    /// <summary>低优先级 — 后台编译、索引重建等</summary>
    Low
}

/// <summary>
/// 优先级背压事件参数。
/// </summary>
public sealed record PriorityBackpressureEventArgs(
    string ActorId,
    MessagePriority Priority,
    int CurrentCount,
    int Capacity,
    WatermarkLevel Level);

/// <summary>
/// 优先级事件 — 命令处理事件,通过 OutputAsync 流输出。
/// </summary>
public sealed record PriorityEvt<TCommand>(TCommand Command, MessagePriority Priority);

/// <summary>
/// 优先级邮箱 — 多通道按优先级消费,高优先级先处理。
/// <para>三通道独立背压:High(用户交互)、Normal(LLM)、Low(后台编译)。</para>
/// <para>Consumer 循环:先 TryRead High,再 Normal,再 Low;全空时信号量等待。</para>
/// <para>同优先级 FIFO,高优先级严格先于低优先级(只要高优先级队列非空)。</para>
/// <para>处理事件通过 OutputAsync 流输出。</para>
/// </summary>
/// <typeparam name="TCommand">命令类型</typeparam>
public abstract class PriorityMailbox<TCommand> : IAsyncDisposable
{
    private readonly Channel<TCommand> _highChannel;
    private readonly Channel<TCommand> _normalChannel;
    private readonly Channel<TCommand> _lowChannel;
    private readonly Channel<PriorityEvt<TCommand>> _outputChannel;
    private readonly ActorBackpressure? _highBackpressure;
    private readonly ActorBackpressure? _normalBackpressure;
    private readonly ActorBackpressure? _lowBackpressure;
    private readonly SemaphoreSlim _signal = new(0, int.MaxValue);
    private readonly Task _consumerTask;
    private readonly CancellationTokenSource _cts = new();
    private int _disposed;

    /// <summary>
    /// 构造优先级邮箱 — 每个优先级独立背压配置(null=无界通道)。
    /// </summary>
    /// <param name="highBackpressure">高优先级背压(用户交互)</param>
    /// <param name="normalBackpressure">普通优先级背压(LLM 请求)</param>
    /// <param name="lowBackpressure">低优先级背压(后台编译)</param>
    protected PriorityMailbox(
        ActorBackpressure? highBackpressure = null,
        ActorBackpressure? normalBackpressure = null,
        ActorBackpressure? lowBackpressure = null)
    {
        _highBackpressure = highBackpressure;
        _normalBackpressure = normalBackpressure;
        _lowBackpressure = lowBackpressure;
        _highChannel = CreateChannel(highBackpressure);
        _normalChannel = CreateChannel(normalBackpressure);
        _lowChannel = CreateChannel(lowBackpressure);
        _outputChannel = Channel.CreateUnbounded<PriorityEvt<TCommand>>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true
        });
        _consumerTask = Task.Run(ConsumeLoopAsync);
    }

    private static Channel<TCommand> CreateChannel(ActorBackpressure? bp)
    {
        if (bp is null || bp.Capacity == 0)
        {
            return Channel.CreateUnbounded<TCommand>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });
        }
        return Channel.CreateBounded<TCommand>(new BoundedChannelOptions(bp.Capacity)
        {
            FullMode = bp.FullMode,
            SingleReader = true,
            SingleWriter = false
        });
    }

    /// <summary>Consumer 任务 — 用于等待 Consumer 退出</summary>
    protected internal Task ConsumerTask => _consumerTask;

    /// <summary>所有优先级通道的总消息数(无界通道返回 0)</summary>
    public int MailboxCount => SafeCount(_highChannel) + SafeCount(_normalChannel) + SafeCount(_lowChannel);

    /// <summary>指定优先级通道的消息数(无界通道返回 0)</summary>
    public int MailboxCountByPriority(MessagePriority priority) => SafeCount(GetChannel(priority));

    private static int SafeCount(Channel<TCommand> channel) =>
        channel.Reader.CanCount ? channel.Reader.Count : 0;

    /// <summary>指定优先级是否达到高水位线(无背压配置时永远 false)</summary>
    public bool IsHighWatermark(MessagePriority priority) => GetBackpressure(priority) is { } bp
        && SafeCount(GetChannel(priority)) >= bp.EffectiveHighWatermark;

    /// <summary>指定优先级是否达到危险水位线(无背压配置时永远 false)</summary>
    public bool IsCriticalWatermark(MessagePriority priority) => GetBackpressure(priority) is { } bp
        && SafeCount(GetChannel(priority)) >= bp.EffectiveCriticalWatermark;

    /// <summary>优先级水位事件 — 达到 High/Critical 水位线时触发</summary>
    public event EventHandler<PriorityBackpressureEventArgs>? PriorityWatermarkReached;

    /// <summary>
    /// 向指定优先级通道异步发送命令 — 无界通道立即返回,有界通道在满时背压等待。
    /// </summary>
    /// <param name="cmd">命令实例</param>
    /// <param name="priority">消息优先级</param>
    /// <param name="ct">取消令牌</param>
    /// <exception cref="ObjectDisposedException">Actor 已释放</exception>
    /// <exception cref="TimeoutException">发送超时(背压配置了 SendTimeout 且通道满)</exception>
    protected internal async ValueTask SendAsync(TCommand cmd, MessagePriority priority, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var channel = GetChannel(priority);
        var bp = GetBackpressure(priority);

        if (bp?.SendTimeout is { } timeout)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linkedCts.CancelAfter(timeout);
            try
            {
                await channel.Writer.WriteAsync(cmd, linkedCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"PriorityMailbox {GetType().Name} 优先级 {priority} 发送超时({timeout.TotalSeconds:F0}s)");
            }
        }
        else
        {
            await channel.Writer.WriteAsync(cmd, ct).ConfigureAwait(false);
        }

        CheckWatermark(priority, channel, bp);
        if (Volatile.Read(ref _disposed) == 0)
            _signal.Release();
    }

    /// <summary>
    /// 向指定优先级通道同步尝试发送命令 — 通道已关闭、已释放或(有界通道)已满时返回 false。
    /// </summary>
    protected bool TrySend(TCommand cmd, MessagePriority priority)
    {
        if (Volatile.Read(ref _disposed) != 0) return false;
        var channel = GetChannel(priority);
        if (!channel.Writer.TryWrite(cmd)) return false;
        CheckWatermark(priority, channel, GetBackpressure(priority));
        if (Volatile.Read(ref _disposed) == 0)
            _signal.Release();
        return true;
    }

    /// <summary>
    /// 子类实现命令处理逻辑 — 由 Consumer 线程串行调用,按优先级顺序消费。
    /// </summary>
    /// <param name="command">待处理命令</param>
    /// <param name="priority">命令优先级</param>
    /// <param name="ct">取消令牌(Actor 释放时触发取消)</param>
    protected abstract ValueTask HandleAsync(TCommand command, MessagePriority priority, CancellationToken ct);

    /// <summary>
    /// Actor 主动推送消息到输出 Channel — 外部通过 OutputAsync 拉取。
    /// </summary>
    protected bool TryPublish(PriorityEvt<TCommand> evt)
    {
        if (Volatile.Read(ref _disposed) != 0) return false;
        return _outputChannel.Writer.TryWrite(evt);
    }

    /// <summary>
    /// 外部拉取输出流 — 阻塞式 IAsyncEnumerable。
    /// </summary>
    public IAsyncEnumerable<PriorityEvt<TCommand>> OutputAsync(CancellationToken cancellationToken = default)
    {
        return _outputChannel.Reader.ReadAllAsync(cancellationToken);
    }

    /// <summary>
    /// Consumer 处理单条命令异常的回调 — 默认忽略,子类可重写以记录日志或计数。
    /// </summary>
    protected virtual void OnConsumerError(Exception ex) { }

    /// <summary>Consumer 循环 — 按优先级顺序消费,全空时信号量等待</summary>
    private async Task ConsumeLoopAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                while (TryReadByPriority(out var cmd, out var priority))
                {
                    try
                    {
                        await HandleAsync(cmd, priority, _cts.Token).ConfigureAwait(false);
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

                await _signal.WaitAsync(_cts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
    }

    private bool TryReadByPriority(out TCommand cmd, out MessagePriority priority)
    {
        if (_highChannel.Reader.TryRead(out var highCmd))
        {
            cmd = highCmd;
            priority = MessagePriority.High;
            return true;
        }
        if (_normalChannel.Reader.TryRead(out var normalCmd))
        {
            cmd = normalCmd;
            priority = MessagePriority.Normal;
            return true;
        }
        if (_lowChannel.Reader.TryRead(out var lowCmd))
        {
            cmd = lowCmd;
            priority = MessagePriority.Low;
            return true;
        }
        cmd = default!;
        priority = default;
        return false;
    }

    private Channel<TCommand> GetChannel(MessagePriority priority) => priority switch
    {
        MessagePriority.High => _highChannel,
        MessagePriority.Normal => _normalChannel,
        MessagePriority.Low => _lowChannel,
        _ => throw new ArgumentOutOfRangeException(nameof(priority))
    };

    private ActorBackpressure? GetBackpressure(MessagePriority priority) => priority switch
    {
        MessagePriority.High => _highBackpressure,
        MessagePriority.Normal => _normalBackpressure,
        MessagePriority.Low => _lowBackpressure,
        _ => null
    };

    private void CheckWatermark(MessagePriority priority, Channel<TCommand> channel, ActorBackpressure? bp)
    {
        if (bp is null) return;
        var count = channel.Reader.Count;
        var level = count >= bp.EffectiveCriticalWatermark ? WatermarkLevel.Critical
                   : count >= bp.EffectiveHighWatermark ? WatermarkLevel.High
                   : WatermarkLevel.Normal;
        if (level != WatermarkLevel.Normal)
        {
            PriorityWatermarkReached?.Invoke(this, new PriorityBackpressureEventArgs(
                GetType().Name, priority, count, bp.Capacity, level));
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(GetType().Name);
    }

    /// <summary>
    /// 释放 — 取消 Consumer、完成所有通道、等待 Consumer 退出。
    /// </summary>
    public virtual async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        _cts.Cancel();
        _highChannel.Writer.TryComplete();
        _normalChannel.Writer.TryComplete();
        _lowChannel.Writer.TryComplete();
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
        _signal.Dispose();
    }
}
