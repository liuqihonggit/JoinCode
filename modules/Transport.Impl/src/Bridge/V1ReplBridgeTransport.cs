namespace JoinCode.Transport.Bridge;

/// <summary>
/// v1 传输适配器 — 对齐 TS 端 HybridTransport + createV1ReplTransport
/// 基于 HybridTransport（WS 读 + HTTP POST 写到 Session-Ingress）
///
/// v1 特征:
/// - 读通道: WebSocket（Session-Ingress WS）
/// - 写通道: HTTP POST 到 Session-Ingress /session/{id}/events（通过 SerialBatchEventUploader）
/// - 认证: OAuth Token（refreshHeaders 回调动态刷新）
/// - 序列号: 不使用 SSE 序列号，GetLastSequenceNum() 始终返回 0
/// - reportState/reportMetadata/reportDelivery: no-op
/// - flush: 等待 SerialBatchEventUploader 排空
/// - autoReconnect: 指数退避重连，10 分钟预算
/// - stream_event 缓冲: 100ms 延迟合并
/// - close grace period: 3s 等待优雅关闭
/// </summary>
public sealed class V1ReplBridgeTransport : IReplBridgeTransport
{
    private const int BatchFlushIntervalMs = 100;
    private const int CloseGraceMs = 3000;
    private const int DefaultBaseReconnectDelayMs = 1000;
    private const int DefaultMaxReconnectDelayMs = 30000;
    private const int DefaultReconnectGiveUpMs = 600_000; // 10 分钟
    private const int SleepDetectionThresholdMs = 60000;
    private const int PostTimeoutMs = 15000;

    private readonly V1TransportOptions _options;
    private readonly ILogger? _logger;
    private readonly HttpClient _httpClient;
    private readonly WebSocketTransport _wsTransport;
    private readonly SerialBatchEventUploader _uploader;
    private readonly CancellationTokenSource _disposeCts;

    // stream_event 缓冲 — 对齐 TS 端 HybridTransport.streamEventBuffer
    private readonly List<string> _streamEventBuffer;
    private Timer? _streamEventTimer;

    // autoReconnect 状态 — 对齐 TS 端 WebSocketTransport
    private int _reconnectAttempts;
    private long _reconnectStartTime;
    private long _lastReconnectAttemptTime;
    private Timer? _reconnectTimer;

    // 连接状态
    private volatile int _isClosed;
    private volatile int _isConnected;

    // 回调
    private Action<string>? _onDataCallback;
    private Action<int?>? _onCloseCallback;
    private Action? _onConnectCallback;
    private Action<int, int>? _onBatchDroppedCallback;

    public int DroppedBatchCount => _uploader.DroppedBatchCount;

    private readonly IClockService _clock;

    public V1ReplBridgeTransport(V1TransportOptions options, ILogger? logger = null, IClockService? clock = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;
        _clock = clock ?? SystemClockService.Instance;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromMilliseconds(PostTimeoutMs) };
        _wsTransport = new WebSocketTransport(options.WebSocketEndpoint, logger);
        _disposeCts = new CancellationTokenSource();
        _streamEventBuffer = [];

        // 创建 SerialBatchEventUploader — 对齐 TS 端 HybridTransport.uploader
        _uploader = new SerialBatchEventUploader(
            _httpClient,
            options.PostEndpoint,
            new SerialBatchUploaderOptions
            {
                MaxBatchSize = 500,
                MaxQueueSize = 100_000,
                BaseDelayMs = 500,
                MaxDelayMs = 8000,
                JitterMs = 1000,
                MaxConsecutiveFailures = options.MaxConsecutiveFailures,
                OnBatchDropped = OnBatchDropped,
            },
            logger);

        // WS 传输消息转发到 onData 回调
        _wsTransport.MessageReceived += OnWsMessageReceived;
        _wsTransport.ErrorOccurred += OnWsError;
    }

    #region IReplBridgeTransport

    public async Task WriteAsync(string message, CancellationToken ct = default)
    {
        if (_isClosed != 0) return;

        // 对齐 TS 端 HybridTransport.write: stream_event 延迟缓冲
        if (IsStreamEvent(message))
        {
            _streamEventBuffer.Add(message);
            if (_streamEventTimer is null)
            {
                _streamEventTimer = new Timer(
                    _ => FlushStreamEvents(),
                    null,
                    BatchFlushIntervalMs,
                    Timeout.Infinite);
            }
            return;
        }

        // 非 stream_event: 先刷出缓冲的 stream_event（保序），再入队当前消息
        var buffered = TakeStreamEvents();
        if (buffered.Count > 0)
        {
            await _uploader.EnqueueRangeAsync(buffered, ct).ConfigureAwait(false);
        }
        await _uploader.EnqueueAsync(message, ct).ConfigureAwait(false);
        await _uploader.FlushAsync(ct).ConfigureAwait(false);
    }

    public async Task WriteBatchAsync(IReadOnlyList<string> messages, CancellationToken ct = default)
    {
        if (_isClosed != 0) return;

        // 先刷出缓冲的 stream_event（保序）
        var buffered = TakeStreamEvents();
        if (buffered.Count > 0)
        {
            await _uploader.EnqueueRangeAsync(buffered, ct).ConfigureAwait(false);
        }

        foreach (var msg in messages)
        {
            if (_isClosed != 0) break;
            await _uploader.EnqueueAsync(msg, ct).ConfigureAwait(false);
        }

        await _uploader.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 异步关闭传输 — P1-4: 消除 Close() 内 sync-over-async 阻塞
    /// 调用方在 async 上下文应优先 await 此方法
    /// </summary>
    public async Task CloseAsync(CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _isClosed, 1) == 1) return;

        _logger?.LogDebug("[V1Transport] 关闭传输");
        Interlocked.Exchange(ref _isConnected, 0);

        // 停止 stream_event 缓冲定时器
        _streamEventTimer?.Dispose();
        _streamEventTimer = null;
        _streamEventBuffer.Clear();

        // 停止重连定时器
        _reconnectTimer?.Dispose();
        _reconnectTimer = null;

        // 优雅关闭: 给 uploader 3s 排空时间 — 对齐 TS 端 HybridTransport.close()
        var uploader = _uploader;
        _ = Task.Run(async () =>
        {
            try
            {
                using var cts = new CancellationTokenSource(CloseGraceMs);
                await uploader.FlushAsync(cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // 优雅关闭超时，忽略
                System.Diagnostics.Trace.WriteLine($"[V1ReplBridgeTransport] Graceful flush timeout: {ex.Message}");
            }
            finally
            {
                uploader.Close();
                uploader.Dispose();
            }
        });

        // 关闭 WS — P1-4: 改为 await，消除 .GetAwaiter().GetResult() 同步阻塞
        try
        {
            await _wsTransport.StopAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 关闭时忽略异常
            System.Diagnostics.Trace.WriteLine($"[V1ReplBridgeTransport] WS stop failed on close: {ex.Message}");
        }

        _httpClient.Dispose();
    }

    /// <summary>
    /// 关闭传输（同步兼容版）— 已弃用，调用方应改为 await CloseAsync
    /// </summary>
    public void Close()
        => CloseAsync(_disposeCts.Token).GetAwaiter().GetResult();

    public bool IsConnectedStatus() => _isConnected != 0;

    public string GetStateLabel()
    {
        if (_isClosed != 0) return "closed";
        if (_isConnected != 0) return "connected";
        if (_reconnectAttempts > 0) return "reconnecting";
        return "disconnected";
    }

    public void SetOnData(Action<string> callback) => _onDataCallback = callback;
    public void SetOnClose(Action<int?> callback) => _onCloseCallback = callback;
    public void SetOnConnect(Action callback) => _onConnectCallback = callback;
    public void SetOnBatchDropped(Action<int, int> callback) => _onBatchDroppedCallback = callback;

    public void Connect()
    {
        _ = ConnectAsync();
    }

    /// <summary>v1 不使用 SSE 序列号，始终返回 0</summary>
    public int GetLastSequenceNum() => 0;

    /// <summary>v1: no-op</summary>
    public Task ReportStateAsync(BridgeSessionActivity state, CancellationToken ct = default) => Task.CompletedTask;

    /// <summary>v1: no-op</summary>
    public Task ReportMetadataAsync(Dictionary<string, JsonElement> metadata, CancellationToken ct = default) => Task.CompletedTask;

    /// <summary>v1: no-op</summary>
    public Task ReportDeliveryAsync(string eventId, string status, CancellationToken ct = default) => Task.CompletedTask;

    /// <summary>排空写队列 — 对齐 TS 端 HybridTransport.flush()</summary>
    public async Task FlushAsync(CancellationToken ct = default)
    {
        var buffered = TakeStreamEvents();
        if (buffered.Count > 0)
        {
            await _uploader.EnqueueRangeAsync(buffered, ct).ConfigureAwait(false);
        }
        await _uploader.FlushAsync(ct).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        Close();
        _disposeCts.Cancel();
        _disposeCts.Dispose();
        try
        {
            await _wsTransport.StopAsync(_disposeCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Dispose 时忽略异常
            System.Diagnostics.Trace.WriteLine($"[V1ReplBridgeTransport] WS stop failed on dispose: {ex.Message}");
        }
    }

    #endregion

    #region WS 连接与重连

    private async Task ConnectAsync()
    {
        try
        {
            // 刷新 headers — 对齐 TS 端 WebSocketTransport 重连前 refreshHeaders
            RefreshAuthHeaders();

            await _wsTransport.StartAsync(_disposeCts.Token).ConfigureAwait(false);
            Interlocked.Exchange(ref _isConnected, 1);
            _reconnectAttempts = 0;
            _reconnectStartTime = 0;
            _lastReconnectAttemptTime = 0;
            _logger?.LogInformation("[V1Transport] WS 连接已建立");
            _onConnectCallback?.Invoke();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[V1Transport] WS 连接失败");
            HandleConnectionError(null);
        }
    }

    /// <summary>
    /// 处理连接错误 — 对齐 TS 端 WebSocketTransport.handleConnectionError
    /// 永久关闭码不重连；否则指数退避重连，10 分钟预算
    /// </summary>
    private void HandleConnectionError(int? closeCode)
    {
        Interlocked.Exchange(ref _isConnected, 0);

        // 永久关闭码: 不重连 — 对齐 TS 端 PERMANENT_CLOSE_CODES (1002, 4001, 4003)
        if (closeCode is 1002 or 4001)
        {
            _logger?.LogError("[V1Transport] 永久关闭码 {CloseCode}，不重连", closeCode);
            _onCloseCallback?.Invoke(closeCode);
            return;
        }

        // 4003 (unauthorized): 尝试刷新 headers — 对齐 TS 端 4003 + refreshHeaders 路径
        if (closeCode == 4003 && _options.RefreshHeaders is not null)
        {
            var freshHeader = _options.RefreshHeaders();
            if (freshHeader != _options.AuthHeader)
            {
                _logger?.LogInformation("[V1Transport] 4003 但 headers 已刷新，将重连");
                // 继续重连流程
            }
            else
            {
                _logger?.LogError("[V1Transport] 4003 且 headers 未变化，不重连");
                _onCloseCallback?.Invoke(closeCode);
                return;
            }
        }

        // 指数退避重连 — 对齐 TS 端 WebSocketTransport autoReconnect
        var now = _clock.GetUtcNowOffset().ToUnixTimeMilliseconds();

        if (_reconnectStartTime == 0)
        {
            _reconnectStartTime = now;
        }

        // 系统休眠检测 — 对齐 TS 端 SLEEP_DETECTION_THRESHOLD_MS
        if (_lastReconnectAttemptTime > 0 && now - _lastReconnectAttemptTime > SleepDetectionThresholdMs)
        {
            _logger?.LogInformation("[V1Transport] 检测到系统休眠，重置重连预算");
            _reconnectStartTime = now;
            _reconnectAttempts = 0;
        }
        _lastReconnectAttemptTime = now;

        var elapsed = now - _reconnectStartTime;
        if (elapsed >= DefaultReconnectGiveUpMs)
        {
            _logger?.LogError("[V1Transport] 重连预算耗尽（{Elapsed}ms），放弃", elapsed);
            _onCloseCallback?.Invoke(closeCode);
            return;
        }

        _reconnectAttempts++;
        var baseDelay = Math.Min(
            DefaultBaseReconnectDelayMs * (1 << Math.Min(_reconnectAttempts - 1, 10)),
            DefaultMaxReconnectDelayMs);
        // ±25% 抖动 — 对齐 TS 端
        var jitter = baseDelay * 0.25 * (2.0 * Random.Shared.NextDouble() - 1.0);
        var delay = Math.Max(0, (int)(baseDelay + jitter));

        _logger?.LogInformation(
            "[V1Transport] 将在 {Delay}ms 后重连（第 {Attempt} 次，已过 {Elapsed}ms）",
            delay, _reconnectAttempts, elapsed);

        _reconnectTimer?.Dispose();
        _reconnectTimer = new Timer(
            _ => _ = ReconnectAsync(),
            null,
            delay,
            Timeout.Infinite);
    }

    private async Task ReconnectAsync()
    {
        if (_isClosed != 0) return;

        try
        {
            // 重连前刷新 headers — 对齐 TS 端
            RefreshAuthHeaders();

            await _wsTransport.StopAsync(_disposeCts.Token).ConfigureAwait(false);
            await _wsTransport.StartAsync(_disposeCts.Token).ConfigureAwait(false);

            Interlocked.Exchange(ref _isConnected, 1);
            _logger?.LogInformation("[V1Transport] WS 重连成功");
            _onConnectCallback?.Invoke();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[V1Transport] 重连失败");
            HandleConnectionError(null);
        }
    }

    /// <summary>刷新认证头 — 对齐 TS 端 refreshHeaders 回调</summary>
    private void RefreshAuthHeaders()
    {
        if (_options.RefreshHeaders is null) return;

        var freshHeader = _options.RefreshHeaders();
        if (freshHeader is not null)
        {
            _httpClient.DefaultRequestHeaders.Remove("Authorization");
            _httpClient.DefaultRequestHeaders.Add("Authorization", freshHeader);
        }
    }

    #endregion

    #region stream_event 缓冲

    /// <summary>判断是否为 stream_event 类型消息 — 对齐 TS 端 HybridTransport</summary>
    private static bool IsStreamEvent(string message)
    {
        // TS 端: message.type === 'stream_event'
        // JSON 消息中包含 "type":"stream_event" 即视为流事件
        return message.Contains("\"stream_event\"", StringComparison.Ordinal)
            && message.Contains("\"type\"", StringComparison.Ordinal);
    }

    /// <summary>取出并清空 stream_event 缓冲</summary>
    private List<string> TakeStreamEvents()
    {
        _streamEventTimer?.Dispose();
        _streamEventTimer = null;

        if (_streamEventBuffer.Count == 0) return [];

        var result = new List<string>(_streamEventBuffer);
        _streamEventBuffer.Clear();
        return result;
    }

    /// <summary>stream_event 延迟定时器到期 — 入队缓冲的事件</summary>
    private void FlushStreamEvents()
    {
        _streamEventTimer?.Dispose();
        _streamEventTimer = null;

        var buffered = TakeStreamEvents();
        if (buffered.Count == 0) return;

        foreach (var msg in buffered)
        {
            _ = _uploader.EnqueueAsync(msg, _disposeCts.Token);
        }
    }

    #endregion

    #region 回调

    private void OnBatchDropped(int batchSize, int failures)
    {
        _logger?.LogError(
            "[V1Transport] 批次丢弃（{BatchSize} 条，连续 {Failures} 次失败）— 通知上层",
            batchSize, failures);

        // 对齐 TS 端 onBatchDropped: 通知上层状态变为 reconnecting + 唤醒轮询
        _onBatchDroppedCallback?.Invoke(batchSize, failures);
    }

    private void OnWsMessageReceived(object? sender, TransportMessageReceivedEventArgs e)
    {
        _onDataCallback?.Invoke(e.Message);
    }

    private void OnWsError(object? sender, TransportErrorEventArgs e)
    {
        _logger?.LogError(e.Exception, "[V1Transport] WS 传输错误: {Message}", e.Message);
        HandleConnectionError(null);
    }

    #endregion
}
