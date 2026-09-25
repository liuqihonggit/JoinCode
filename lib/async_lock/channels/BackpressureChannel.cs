namespace Core.Utils;

/// <summary>
/// 有界背压通道 — 不丢弃消息,高水位时反向通知发送方延迟重试
/// <para>写入用 TryWrite(非阻塞),读取检测水位线后反向 Tell(射后不理)</para>
/// <para>协议: 收发双方环形通信,16次重试,每次换流水号,重试次数计数</para>
/// </summary>
/// <typeparam name="T">业务数据类型</typeparam>
public sealed class BackpressureChannel<T> : IAsyncDisposable where T : notnull {

    private readonly Channel<BackpressureMessage<T>> _data;
    private readonly ActorBackpressure _config;
    private readonly string _ownerId;
    private readonly Func<BackpressureSignal, ValueTask>? _notifyBackpressureAsync;
    private readonly Channel<BackpressureSignal> _signalQueue;
    private long _nextSequenceId;
    private WatermarkLevel _lastNotifiedLevel = WatermarkLevel.Normal;
    private int _disposed;

    /// <summary>当前队列长度</summary>
    public int Count => _data.Reader.Count;

    /// <summary>通道容量</summary>
    public int Capacity => _config.Capacity;

    /// <summary>是否达到高水位线</summary>
    public bool IsHighWatermark => Count >= _config.EffectiveHighWatermark;

    /// <summary>是否达到危险水位线</summary>
    public bool IsCriticalWatermark => Count >= _config.EffectiveCriticalWatermark;

    /// <summary>当前水位等级</summary>
    public WatermarkLevel CurrentLevel =>
        Count >= _config.EffectiveCriticalWatermark ? WatermarkLevel.Critical :
        Count >= _config.EffectiveHighWatermark ? WatermarkLevel.High :
        WatermarkLevel.Normal;

    /// <summary>
    /// 构造有界背压通道
    /// </summary>
    /// <param name="ownerId">本通道拥有者标识(接收方)</param>
    /// <param name="config">背压配置(容量+水位线+超时)</param>
    /// <param name="notifyBackpressureAsync">反向通知发送方的回调(射后不理,Tell)</param>
    /// <param name="singleReader">单读者优化</param>
    /// <param name="singleWriter">单写者优化</param>
    public BackpressureChannel(
        string ownerId,
        ActorBackpressure config,
        Func<BackpressureSignal, ValueTask>? notifyBackpressureAsync = null,
        bool singleReader = true,
        bool singleWriter = false) {
        ArgumentNullException.ThrowIfNull(ownerId);
        ArgumentNullException.ThrowIfNull(config);
        _ownerId = ownerId;
        _config = config;
        _notifyBackpressureAsync = notifyBackpressureAsync;
        _data = Channel.CreateBounded<BackpressureMessage<T>>(new BoundedChannelOptions(config.Capacity) {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = singleReader,
            SingleWriter = singleWriter
        });
        _signalQueue = Channel.CreateBounded<BackpressureSignal>(new BoundedChannelOptions(16) {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
    }

    /// <summary>
    /// 写入方: TryWrite(非阻塞) — 每次生成新流水号,射后不理
    /// </summary>
    /// <param name="item">业务数据</param>
    /// <param name="targetId">接收方标识</param>
    /// <param name="retryCount">重试次数(0=首次)</param>
    /// <returns>true=写入成功,false=通道满,调用方应延迟重试</returns>
    public bool TryWrite(T item, string targetId, int retryCount = 0) {
        var seq = Interlocked.Increment(ref _nextSequenceId);
        var msg = new BackpressureMessage<T>(item, seq, retryCount, _ownerId, targetId);
        return _data.Writer.TryWrite(msg);
    }

    /// <summary>
    /// 读取方: 读取 + 水位线检测 + 反向通知(射后不理)
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>读取到的背压协议消息</returns>
    public async ValueTask<BackpressureMessage<T>> ReadAsync(CancellationToken ct) {
        var msg = await _data.Reader.ReadAsync(ct).ConfigureAwait(false);
        await CheckWatermarkAndNotifyAsync(msg).ConfigureAwait(false);
        return msg;
    }

    /// <summary>
    /// 读取方: 批量读取 — 每次读取后检测水位线并反向通知
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>背压协议消息异步枚举</returns>
    public IAsyncEnumerable<BackpressureMessage<T>> ReadAllAsync(CancellationToken ct) => ReadAllCoreAsync(ct);

    private async IAsyncEnumerable<BackpressureMessage<T>> ReadAllCoreAsync(
        [EnumeratorCancellation] CancellationToken ct) {
        await foreach (var msg in _data.Reader.ReadAllAsync(ct).ConfigureAwait(false)) {
            await CheckWatermarkAndNotifyAsync(msg).ConfigureAwait(false);
            yield return msg;
        }
    }

    /// <summary>
    /// 接收方收到远程背压信号 → 写入信号队列(供写入方消费)
    /// </summary>
    /// <param name="signal">远程发来的背压信号</param>
    public void ReceiveSignal(BackpressureSignal signal) => _signalQueue.Writer.TryWrite(signal);

    /// <summary>
    /// 写入方: 消费所有待处理背压信号,返回累计建议延迟时间
    /// </summary>
    /// <returns>总延迟时间(TimeSpan.Zero=无背压)</returns>
    public TimeSpan ConsumePendingSignals() {
        var totalMs = 0.0;
        while (_signalQueue.Reader.TryRead(out var signal)) {
            if (signal.Level != WatermarkLevel.Normal)
                totalMs += signal.SuggestedDelay.TotalMilliseconds;
        }
        return TimeSpan.FromMilliseconds(totalMs);
    }

    /// <summary>
    /// 水位线检测 + 反向通知 — 只在水位跨越阈值时发信号(避免信号风暴)
    /// </summary>
    private async ValueTask CheckWatermarkAndNotifyAsync(BackpressureMessage<T> msg) {
        if (_notifyBackpressureAsync is null) return;

        var level = CurrentLevel;
        if (level == _lastNotifiedLevel) return;

        _lastNotifiedLevel = level;
        var delay = level switch {
            WatermarkLevel.Critical => CalculateCriticalDelay(),
            WatermarkLevel.High => TimeSpan.FromMilliseconds(50),
            WatermarkLevel.Normal => TimeSpan.Zero,
            _ => TimeSpan.Zero
        };

        await _notifyBackpressureAsync(new BackpressureSignal(
            msg.SequenceId, _ownerId, msg.SourceId, level, delay, msg.RetryCount
        )).ConfigureAwait(false);
    }

    /// <summary>Critical 水位建议延迟 — 超出高水位越多延迟越长,上限1秒</summary>
    private TimeSpan CalculateCriticalDelay() {
        var overflow = Count - _config.EffectiveHighWatermark;
        return TimeSpan.FromMilliseconds(Math.Min(100 * overflow, 1000));
    }

    /// <summary>完成通道写入</summary>
    public void Complete() => _data.Writer.TryComplete();

    /// <summary>异步释放资源</summary>
    public ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return ValueTask.CompletedTask;
        _data.Writer.TryComplete();
        _signalQueue.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}
