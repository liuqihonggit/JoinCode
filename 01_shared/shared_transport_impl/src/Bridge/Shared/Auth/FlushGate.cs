namespace JoinCode.Transport.Bridge;

/// <summary>
/// 刷新门控选项 - 控制消息批处理的参数
/// </summary>
public sealed class FlushGateOptions
{
    /// <summary>每批最大条目数</summary>
    public const int DefaultMaxBatchSize = 100;

    /// <summary>定时刷新间隔（毫秒）</summary>
    public const int DefaultFlushIntervalMs = 1000;

    /// <summary>最大等待时间（毫秒），超过此时间强制刷新</summary>
    public const int DefaultMaxWaitMs = 5000;

    /// <summary>每批最大条目数</summary>
    [JsonPropertyName("maxBatchSize")]
    public int MaxBatchSize { get; init; } = DefaultMaxBatchSize;

    /// <summary>定时刷新间隔（毫秒）</summary>
    [JsonPropertyName("flushIntervalMs")]
    public int FlushIntervalMs { get; init; } = DefaultFlushIntervalMs;

    /// <summary>最大等待时间（毫秒）</summary>
    [JsonPropertyName("maxWaitMs")]
    public int MaxWaitMs { get; init; } = DefaultMaxWaitMs;

    /// <summary>
    /// 创建默认配置的选项实例
    /// </summary>
    public static FlushGateOptions CreateDefault() => new();
}

/// <summary>
/// 刷新门控命令 — Actor 消息类型
/// </summary>
public interface IFlushGateCommand<T>;

public sealed record FlushAddCmd<T>(T Item, TaskCompletionSource Tcs) : IFlushGateCommand<T>;
public sealed record FlushManualCmd<T>(TaskCompletionSource Tcs) : IFlushGateCommand<T>;
public sealed record FlushStartCmd<T>(TaskCompletionSource Tcs) : IFlushGateCommand<T>;
public sealed record FlushStopCmd<T>(TaskCompletionSource Tcs) : IFlushGateCommand<T>;
public sealed record FlushTickCmd<T> : IFlushGateCommand<T>;
public sealed record FlushGetSizeCmd<T>(TaskCompletionSource<int> Tcs) : IFlushGateCommand<T>;

/// <summary>
/// 刷新门控 - Actor 化：Consumer 线程独占 _currentBatch/_batchAgeStopwatch，消除 AsyncLock。
/// <para>定时刷新循环改为 Timer + TrySend(FlushTickCmd) 自消息，Consumer 串行处理。</para>
/// <para>发送方通过 TaskCompletionSource 等待 Consumer 处理完成，保证命令语义同步。</para>
/// </summary>
public sealed class FlushGate<T> : ActorBase<IFlushGateCommand<T>, Unit>, IFlushGate<T>
{
    private readonly FlushGateOptions _options;
    private readonly ILogger? _logger;
    private readonly Timer _flushTimer;
    private readonly Stopwatch _batchAgeStopwatch;
    private int _isDisposed;

    private readonly List<T> _currentBatch;
    private bool _isRunning;

    public event EventHandler<BatchFlushedEventArgs<T>>? BatchFlushed;

    public FlushGate(
        FlushGateOptions? options = null,
        ILogger? logger = null,
        TimeProvider? timeProvider = null)
        : base()
    {
        _options = options ?? FlushGateOptions.CreateDefault();
        _logger = logger;
        _currentBatch = new List<T>(_options.MaxBatchSize);
        _batchAgeStopwatch = new Stopwatch();
        _flushTimer = new Timer(_ => TrySend(new FlushTickCmd<T>()), null, Timeout.Infinite, Timeout.Infinite);
    }

    private static TaskCompletionSource CreateTcs() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// 当前批次中的条目数量
    /// </summary>
    public async Task<int> GetCurrentBatchSizeAsync(CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        await SendAsync(new FlushGetSizeCmd<T>(tcs), ct).ConfigureAwait(false);
        return await tcs.Task.ConfigureAwait(false);
    }

    /// <summary>
    /// 启动定时刷新循环
    /// </summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed != 0, this);
        var tcs = CreateTcs();
        await SendAsync(new FlushStartCmd<T>(tcs), ct).ConfigureAwait(false);
        await tcs.Task.ConfigureAwait(false);
    }

    /// <summary>
    /// 停止定时刷新循环，并刷新剩余条目
    /// </summary>
    public async Task StopAsync(CancellationToken ct = default)
    {
        var tcs = CreateTcs();
        await SendAsync(new FlushStopCmd<T>(tcs), ct).ConfigureAwait(false);
        await tcs.Task.ConfigureAwait(false);
    }

    /// <summary>
    /// 添加条目到当前批次
    /// 当批次满或超过最大等待时间时自动触发刷新
    /// </summary>
    public async Task AddAsync(T item, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed != 0, this);
        var tcs = CreateTcs();
        await SendAsync(new FlushAddCmd<T>(item, tcs), ct).ConfigureAwait(false);
        await tcs.Task.ConfigureAwait(false);
    }

    /// <summary>
    /// 手动触发刷新
    /// </summary>
    public async Task FlushAsync(CancellationToken ct = default)
    {
        var tcs = CreateTcs();
        await SendAsync(new FlushManualCmd<T>(tcs), ct).ConfigureAwait(false);
        await tcs.Task.ConfigureAwait(false);
    }

    protected override async ValueTask HandleAsync(IFlushGateCommand<T> command, CancellationToken ct)
    {
        switch (command)
        {
            case FlushStartCmd<T> start:
                if (_isRunning)
                {
                    _logger?.LogWarning("[FlushGate] 已在运行中");
                    start.Tcs.TrySetResult();
                    return;
                }
                _isRunning = true;
                _batchAgeStopwatch.Start();
                _flushTimer.Change(TimeSpan.FromMilliseconds(_options.FlushIntervalMs), TimeSpan.FromMilliseconds(_options.FlushIntervalMs));
                _logger?.LogInformation("[FlushGate] 已启动，刷新间隔: {IntervalMs}ms，最大批次: {MaxBatch}",
                    _options.FlushIntervalMs, _options.MaxBatchSize);
                start.Tcs.TrySetResult();
                break;

            case FlushStopCmd<T> stop:
                if (!_isRunning)
                {
                    stop.Tcs.TrySetResult();
                    return;
                }
                _isRunning = false;
                _flushTimer.Change(Timeout.Infinite, Timeout.Infinite);
                FlushCore();
                _batchAgeStopwatch.Stop();
                _logger?.LogInformation("[FlushGate] 已停止");
                stop.Tcs.TrySetResult();
                break;

            case FlushAddCmd<T> add:
                _currentBatch.Add(add.Item);
                if (_currentBatch.Count >= _options.MaxBatchSize ||
                    _batchAgeStopwatch.ElapsedMilliseconds >= _options.MaxWaitMs)
                {
                    FlushCore();
                }
                add.Tcs.TrySetResult();
                break;

            case FlushManualCmd<T> flush:
                FlushCore();
                flush.Tcs.TrySetResult();
                break;

            case FlushTickCmd<T>:
                FlushCore();
                break;

            case FlushGetSizeCmd<T> getSize:
                getSize.Tcs.TrySetResult(_currentBatch.Count);
                break;
        }
    }

    protected override void OnConsumerError(Exception ex)
    {
        _logger?.LogError(ex, "[FlushGate] 消费者异常");
    }

    /// <summary>
    /// 刷新核心逻辑（Consumer 线程独占，无需锁）
    /// </summary>
    private void FlushCore()
    {
        if (_currentBatch.Count == 0)
        {
            return;
        }

        var batchToFlush = new List<T>(_currentBatch);
        _currentBatch.Clear();
        _batchAgeStopwatch.Restart();

        _logger?.LogDebug("[FlushGate] 刷新批次，条目数: {Count}", batchToFlush.Count);

        BatchFlushed?.Invoke(this, new BatchFlushedEventArgs<T>(batchToFlush));
    }

    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1)
        {
            return;
        }

        _flushTimer.Change(Timeout.Infinite, Timeout.Infinite);
        _flushTimer.Dispose();

        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[FlushGate] Dispose 时停止失败");
        }

        await base.DisposeAsync().ConfigureAwait(false);
    }
}

// BatchFlushedEventArgs<T> 已迁移到 JoinCode.Transport.Bridge 命名空间 (Transport.Contracts)
