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
/// 刷新门控 - 批量收集条目并定期或满批时刷新
/// 用于消息批处理，减少频繁 IO 操作
/// </summary>
public sealed class FlushGate<T> : IFlushGate<T>
{
    private readonly FlushGateOptions _options;
    private readonly ILogger? _logger;
    private readonly SemaphoreSlim _batchLock;
    private readonly List<T> _currentBatch;
    private readonly Stopwatch _batchAgeStopwatch;
    private readonly TimeProvider _timeProvider;

    private CancellationTokenSource? _flushCts;
    private Task? _flushLoopTask;
    private int _isRunning;
    private int _isDisposed;

    public event EventHandler<BatchFlushedEventArgs<T>>? BatchFlushed;

    public FlushGate(
        FlushGateOptions? options = null,
        ILogger? logger = null,
        TimeProvider? timeProvider = null)
    {
        _options = options ?? FlushGateOptions.CreateDefault();
        _logger = logger;
        _batchLock = new SemaphoreSlim(1, 1);
        _currentBatch = new List<T>(_options.MaxBatchSize);
        _batchAgeStopwatch = new Stopwatch();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// 当前批次中的条目数量
    /// </summary>
    public async Task<int> GetCurrentBatchSizeAsync(CancellationToken ct = default)
    {
        await _batchLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return _currentBatch.Count;
        }
        finally
        {
            _batchLock.Release();
        }
    }

    /// <summary>
    /// 启动定时刷新循环
    /// </summary>
    /// <param name="ct">取消令牌</param>
    public async Task StartAsync(CancellationToken ct = default)
    {
        await _batchLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0)
            {
                _logger?.LogWarning("[FlushGate] 已在运行中");
                return;
            }

            _flushCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _flushLoopTask = RunFlushLoopAsync(_flushCts.Token);
            _batchAgeStopwatch.Start();

            _logger?.LogInformation(
                "[FlushGate] 已启动，刷新间隔: {IntervalMs}ms，最大批次: {MaxBatch}",
                _options.FlushIntervalMs, _options.MaxBatchSize);
        }
        finally
        {
            _batchLock.Release();
        }
    }

    /// <summary>
    /// 停止定时刷新循环，并刷新剩余条目
    /// </summary>
    /// <param name="ct">取消令牌</param>
    public async Task StopAsync(CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _isRunning, 0) == 0)
        {
            return;
        }

        await (_flushCts?.CancelAsync() ?? Task.CompletedTask).ConfigureAwait(false);

        if (_flushLoopTask is not null)
        {
            try
            {
                await _flushLoopTask.WaitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        // 刷新剩余条目
        await FlushAsync(ct).ConfigureAwait(false);

        _batchAgeStopwatch.Stop();
        _logger?.LogInformation("[FlushGate] 已停止");
    }

    /// <summary>
    /// 添加条目到当前批次
    /// 当批次满或超过最大等待时间时自动触发刷新
    /// </summary>
    /// <param name="item">要添加的条目</param>
    /// <param name="ct">取消令牌</param>
    public async Task AddAsync(T item, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed != 0, this);

        bool shouldFlush = false;

        await _batchLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _currentBatch.Add(item);

            // 批次满或超过最大等待时间，标记需要刷新
            if (_currentBatch.Count >= _options.MaxBatchSize ||
                _batchAgeStopwatch.ElapsedMilliseconds >= _options.MaxWaitMs)
            {
                shouldFlush = true;
            }
        }
        finally
        {
            _batchLock.Release();
        }

        if (shouldFlush)
        {
            await FlushAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 手动触发刷新
    /// </summary>
    /// <param name="ct">取消令牌</param>
    public async Task FlushAsync(CancellationToken ct = default)
    {
        List<T> batchToFlush;

        await _batchLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_currentBatch.Count == 0)
            {
                return;
            }

            batchToFlush = new List<T>(_currentBatch);
            _currentBatch.Clear();
            _batchAgeStopwatch.Restart();
        }
        finally
        {
            _batchLock.Release();
        }

        _logger?.LogDebug("[FlushGate] 刷新批次，条目数: {Count}", batchToFlush.Count);

        BatchFlushed?.Invoke(this, new BatchFlushedEventArgs<T>(batchToFlush));
    }

    /// <summary>
    /// 定时刷新循环
    /// </summary>
    private async Task RunFlushLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(_options.FlushIntervalMs), _timeProvider, cancellationToken).ConfigureAwait(false);
                await FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[FlushGate] 定时刷新失败");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1)
        {
            return;
        }

        await StopAsync().ConfigureAwait(false);
        _flushCts?.Dispose();
        _batchLock.Dispose();
    }
}

// BatchFlushedEventArgs<T> 已迁移到 JoinCode.Transport.Bridge 命名空间 (Transport.Contracts)
