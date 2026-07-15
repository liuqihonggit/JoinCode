namespace JoinCode.Transport.Bridge;

/// <summary>
/// 可重试错误 — 对齐 TS 端 SerialBatchEventUploader.RetryableError
/// 从 send 回调抛出以触发指数退避重试；retryAfterMs 覆盖默认退避
/// </summary>
public sealed class RetryableError : Exception
{
    /// <summary>服务端建议的重试等待时间（毫秒），覆盖指数退避</summary>
    public int? RetryAfterMs { get; }

    public RetryableError(string message, int? retryAfterMs = null)
        : base(message)
    {
        RetryAfterMs = retryAfterMs;
    }
}

/// <summary>
/// 序列化批次事件上传器 — 对齐 TS 端 SerialBatchEventUploader
/// 同一时间只有一个 POST 在飞，支持延迟缓冲、背压、指数退避重试、maxConsecutiveFailures
/// </summary>
public sealed class SerialBatchEventUploader : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _endpoint;
    private readonly ILogger? _logger;
    private readonly SerialBatchUploaderOptions _options;
    private readonly Func<IReadOnlyList<string>, Task> _sendFunc;

    private readonly List<string> _pending;
    private readonly SemaphoreSlim _drainLock;
    private readonly List<TaskCompletionSource> _flushResolvers;
    private readonly List<Action> _backpressureResolvers;
    private CancellationTokenSource? _sleepCts;
    private int _pendingAtClose;
    private bool _draining;
    private bool _closed;
    private int _droppedBatches;
    private int _isDisposed;

    /// <summary>因 maxConsecutiveFailures 丢弃的批次计数</summary>
    public int DroppedBatchCount => _droppedBatches;

    /// <summary>待发送队列深度</summary>
    public int PendingCount => _closed ? _pendingAtClose : _pending.Count;

    /// <summary>
    /// 序列化批次事件上传器构造函数
    /// </summary>
    /// <param name="httpClient">HTTP 客户端</param>
    /// <param name="endpoint">POST 目标端点 URL</param>
    /// <param name="options">配置选项，为 null 时使用默认值</param>
    /// <param name="logger">日志记录器（JCC9003 过滤）</param>
    public SerialBatchEventUploader(
        HttpClient httpClient,
        string endpoint,
        SerialBatchUploaderOptions? options = null,
        ILogger? logger = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        _options = options ?? new SerialBatchUploaderOptions();
        _logger = logger;
        _sendFunc = SendViaHttpPostAsync;

        _pending = new List<string>();
        _drainLock = new SemaphoreSlim(1, 1);
        _flushResolvers = [];
        _backpressureResolvers = [];
    }

    /// <summary>入队事件 — 满队列时阻塞（背压）</summary>
    public async Task EnqueueAsync(string message, CancellationToken ct = default)
    {
        if (_closed) return;

        // 背压: 等待队列有空间
        while (_pending.Count >= _options.MaxQueueSize && !_closed)
        {
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _backpressureResolvers.Add(() => tcs.TrySetResult());

            using var linkedCts = TimeoutHelper.CreateLinkedTimeout(ct, TimeSpan.FromMilliseconds(5000));
            try
            {
                await tcs.Task.WaitAsync(linkedCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // 超时，再检查一次
            }
        }

        if (_closed) return;

        _pending.Add(message);
        _ = DrainAsync();
    }

    /// <summary>批量入队</summary>
    public async Task EnqueueRangeAsync(IReadOnlyList<string> messages, CancellationToken ct = default)
    {
        foreach (var msg in messages)
        {
            await EnqueueAsync(msg, ct).ConfigureAwait(false);
        }
    }

    /// <summary>排空写队列 — 阻塞直到所有事件发送完毕</summary>
    public async Task FlushAsync(CancellationToken ct = default)
    {
        if (_pending.Count == 0 && !_draining) return;

        _ = DrainAsync();

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _flushResolvers.Add(tcs);

        try
        {
            await tcs.Task.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // flush 被取消不算错误
        }
    }

    /// <summary>关闭上传器 — 丢弃待处理事件，释放所有等待者</summary>
    public void Close()
    {
        if (_closed) return;
        _closed = true;
        _pendingAtClose = _pending.Count;
        _pending.Clear();

        // 中断正在进行的睡眠
        _sleepCts?.Cancel();

        // 释放背压等待者
        foreach (var resolve in _backpressureResolvers) resolve();
        _backpressureResolvers.Clear();

        // 释放 flush 等待者
        foreach (var tcs in _flushResolvers) tcs.TrySetResult();
        _flushResolvers.Clear();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1) return;
        Close();
        _drainLock.Dispose();
        _sleepCts?.Dispose();
    }

    /// <summary>
    /// 排空循环 — 同一时间仅一个实例运行
    /// 串行发送批次，失败时指数退避重试
    /// </summary>
    private async Task DrainAsync()
    {
        await _drainLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_draining || _closed) return;
            _draining = true;
        }
        finally
        {
            _drainLock.Release();
        }

        var failures = 0;

        try
        {
            while (_pending.Count > 0 && !_closed)
            {
                var batch = TakeBatch();
                if (batch.Count == 0) continue;

                try
                {
                    await _sendFunc(batch).ConfigureAwait(false);
                    failures = 0;
                }
                catch (Exception ex)
                {
                    failures++;

                    if (_options.MaxConsecutiveFailures.HasValue && failures >= _options.MaxConsecutiveFailures.Value)
                    {
                        _droppedBatches++;
                        _options.OnBatchDropped?.Invoke(batch.Count, failures);
                        failures = 0;
                        ReleaseBackpressure();
                        continue;
                    }

                    // 重新入队到队首
                    _pending.InsertRange(0, batch);

                    var retryAfterMs = ex is RetryableError re ? re.RetryAfterMs : null;
                    var delay = ComputeRetryDelay(failures, retryAfterMs);
                    await SleepAsync(delay).ConfigureAwait(false);
                    continue;
                }

                ReleaseBackpressure();
            }
        }
        finally
        {
            _draining = false;

            // 队列为空时通知 flush 等待者
            if (_pending.Count == 0)
            {
                foreach (var tcs in _flushResolvers) tcs.TrySetResult();
                _flushResolvers.Clear();
            }
        }
    }

    /// <summary>从队列中提取一批事件 — 尊重 maxBatchSize 和 maxBatchBytes</summary>
    private List<string> TakeBatch()
    {
        var count = Math.Min(_pending.Count, _options.MaxBatchSize);

        if (_options.MaxBatchBytes <= 0)
        {
            var batch = _pending.GetRange(0, count);
            _pending.RemoveRange(0, count);
            return batch;
        }

        // 按字节大小截取
        var totalBytes = 0;
        var takeCount = 0;
        for (var i = 0; i < _pending.Count && i < _options.MaxBatchSize; i++)
        {
            var itemBytes = Encoding.UTF8.GetByteCount(_pending[i]);
            if (i > 0 && totalBytes + itemBytes > _options.MaxBatchBytes) break;
            totalBytes += itemBytes;
            takeCount++;
        }

        if (takeCount == 0 && _pending.Count > 0) takeCount = 1; // 第一条始终发送

        var result = _pending.GetRange(0, takeCount);
        _pending.RemoveRange(0, takeCount);
        return result;
    }

    /// <summary>计算重试延迟 — 指数退避 + 抖动</summary>
    private int ComputeRetryDelay(int failures, int? retryAfterMs)
    {
        var jitter = Random.Shared.Next(0, _options.JitterMs + 1);

        if (retryAfterMs.HasValue)
        {
            var clamped = Math.Max(_options.BaseDelayMs, Math.Min(retryAfterMs.Value, _options.MaxDelayMs));
            return clamped + jitter;
        }

        var exponential = Math.Min(_options.BaseDelayMs * (1 << (failures - 1)), _options.MaxDelayMs);
        return exponential + jitter;
    }

    /// <summary>可中断的睡眠</summary>
    private async Task SleepAsync(int ms)
    {
        _sleepCts?.Cancel();
        _sleepCts?.Dispose();
        _sleepCts = new CancellationTokenSource();

        try
        {
            await Task.Delay(ms, _sleepCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 睡眠被中断（Close 调用）
        }
    }

    /// <summary>释放背压等待者</summary>
    private void ReleaseBackpressure()
    {
        if (_backpressureResolvers.Count == 0) return;
        var resolvers = _backpressureResolvers.ToArray();
        _backpressureResolvers.Clear();
        foreach (var resolve in resolvers) resolve();
    }

    /// <summary>内置 HTTP POST 发送 — 对齐 TS 端 HybridTransport.postOnce</summary>
    private async Task SendViaHttpPostAsync(IReadOnlyList<string> batch)
    {
        var json = batch.Count == 1 ? batch[0] : "[" + string.Join(",", batch) + "]";

        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(_endpoint, content).ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            // 429 — 可重试，从 Retry-After 头获取等待时间
            var retryAfter = response.Headers.RetryAfter;
            var retryAfterMs = retryAfter?.Delta?.TotalMilliseconds is double ms and > 0
                ? (int?)ms
                : null;
            throw new RetryableError($"429 Too Many Requests", retryAfterMs);
        }

        if ((int)response.StatusCode >= 500)
        {
            // 5xx — 可重试
            throw new RetryableError($"Server error: {(int)response.StatusCode}");
        }

        if ((int)response.StatusCode is >= 400 and < 500)
        {
            // 4xx 非 429 — 永久错误，不重试，直接返回（丢弃）
            _logger?.LogWarning("[SerialBatchEventUploader] POST 返回 {StatusCode}（永久错误），丢弃", (int)response.StatusCode);
            return;
        }

        response.EnsureSuccessStatusCode();
    }
}
