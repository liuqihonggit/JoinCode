namespace JoinCode.Transport.Bridge;

/// <summary>
/// v2 传输适配器 — 对齐 TS 端 createV2ReplTransport
/// 基于 SSETransport（读）+ CCRClient（写到 CCR v2 /worker/*）
///
/// v2 特征:
/// - 读通道: SSE（/worker/events/stream）
/// - 写通道: HTTP POST 到 CCR /worker/events（通过 BridgeApiClient）
/// - 认证: JWT（worker_jwt，包含 session_id claim + worker role）
/// - 序列号: 使用 SSE Last-Event-ID / from_sequence_num
/// - 心跳: 独立心跳定时器（POST /worker/heartbeat）
/// - Epoch 管理: 409 Conflict = epoch 被更新 worker 取代
/// - reportState/reportMetadata/reportDelivery: 有实际实现
/// - flush: 等待写队列排空
/// </summary>
public sealed class V2ReplBridgeTransport : IReplBridgeTransport
{
    private readonly V2TransportOptions _options;
    private readonly ILogger? _logger;
    private readonly HttpClient _writeClient;
    private readonly HttpClient _sseClient;
    private readonly SemaphoreSlim _writeLock;
    private readonly SerialBatchEventUploader _eventUploader;
    private readonly SerialBatchEventUploader _deliveryUploader;
    private readonly CancellationTokenSource _heartbeatCts;
    private Task? _heartbeatTask;
    private Task? _sseReadTask;
    private int _lastSequenceNum;
    private volatile int _isClosed;
    private volatile int _isInitialized;
    private volatile int _epoch;

    private Action<string>? _onDataCallback;
    private Action<int?>? _onCloseCallback;
    private Action? _onConnectCallback;

    public int DroppedBatchCount => 0; // v2 写路径不设置 maxConsecutiveFailures

    public V2ReplBridgeTransport(V2TransportOptions options, ILogger? logger = null, HttpClient? writeClient = null, HttpClient? sseClient = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;
        _lastSequenceNum = options.InitialSequenceNum;
        _epoch = options.Epoch ?? 0;
        _writeLock = new SemaphoreSlim(1, 1);
        _heartbeatCts = new CancellationTokenSource();

        // P1-12: 兜底 HttpClient 添加 SocketsHttpHandler 配置解决 DNS 不刷新
        // 决策: 保留 ?? 兜底模式（测试场景可注入自定义 client），仅修改兜底 handler 配置
        _writeClient = writeClient ?? new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(1),
        });
        SetAuthHeaders(_writeClient, options.IngressToken);

        _sseClient = sseClient ?? new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(1),
        });

        // 事件上传器（100ms 延迟缓冲，最大批次 100）
        _eventUploader = new SerialBatchEventUploader(
            _writeClient,
            $"{options.ApiBaseUrl}/worker/events",
            new SerialBatchUploaderOptions
            {
                MaxBatchSize = 100,
                MaxQueueSize = 10_000,
                BaseDelayMs = 100,
                JitterMs = 500,
            },
            logger);

        // 投递确认上传器
        _deliveryUploader = new SerialBatchEventUploader(
            _writeClient,
            $"{options.ApiBaseUrl}/worker/events/delivery",
            new SerialBatchUploaderOptions
            {
                MaxBatchSize = 100,
                MaxQueueSize = 10_000,
                BaseDelayMs = 50,
                JitterMs = 500,
            },
            logger);
    }

    public async Task WriteAsync(string message, CancellationToken ct = default)
    {
        if (_isClosed != 0)
        {
            _logger?.LogDebug("[V2Transport] 已关闭，丢弃写入");
            return;
        }

        if (_isInitialized == 0)
        {
            throw new InvalidOperationException("[V2Transport] CCRClient 尚未初始化，无法写入");
        }

        await _eventUploader.EnqueueAsync(message, ct).ConfigureAwait(false);
    }

    public async Task WriteBatchAsync(IReadOnlyList<string> messages, CancellationToken ct = default)
    {
        foreach (var msg in messages)
        {
            if (_isClosed != 0) break;
            await _eventUploader.EnqueueAsync(msg, ct).ConfigureAwait(false);
        }
    }

    public void Close()
    {
        if (Interlocked.Exchange(ref _isClosed, 1) == 1)
        {
            return;
        }

        _logger?.LogDebug("[V2Transport] 关闭传输");
        _heartbeatCts.Cancel();
        _eventUploader.Dispose();
        _deliveryUploader.Dispose();
        _writeClient.Dispose();
        _sseClient.Dispose();
    }

    /// <summary>
    /// 异步关闭传输 — P1-4: V2 的关闭全为同步操作，直接委托 Close
    /// </summary>
    public Task CloseAsync(CancellationToken ct = default)
    {
        Close();
        return Task.CompletedTask;
    }

    public bool IsConnectedStatus()
    {
        // 写就绪状态，非读就绪 — 对齐 TS 端 ccrInitialized
        return _isInitialized != 0;
    }

    public string GetStateLabel()
    {
        if (_isClosed != 0) return "closed";
        if (_isInitialized != 0) return "connected";
        return "init";
    }

    public void SetOnData(Action<string> callback) => _onDataCallback = callback;
    public void SetOnClose(Action<int?> callback) => _onCloseCallback = callback;
    public void SetOnConnect(Action callback) => _onConnectCallback = callback;
    public void SetOnBatchDropped(Action<int, int> callback) { /* v2 不使用 maxConsecutiveFailures，no-op */ }

    public void Connect()
    {
        // 对齐 TS 端: SSE 读流和 CCRClient 初始化并行启动

        // 非 outboundOnly 模式: 启动 SSE 读流
        if (!_options.OutboundOnly)
        {
            _sseReadTask = RunSseReadLoopAsync();
        }

        // 初始化 CCRClient（注册 worker epoch）
        _ = InitializeCcrAsync();
    }

    public int GetLastSequenceNum() => _lastSequenceNum;

    public async Task ReportStateAsync(BridgeSessionActivity state, CancellationToken ct = default)
    {
        if (_isClosed != 0 || _isInitialized == 0) return;

        try
        {
            var payload = $"{{\"state\":\"{state.ToValue()}\"}}";
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await _writeClient.PutAsync($"{_options.ApiBaseUrl}/worker", content, ct).ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                HandleEpochMismatch("ReportState");
                return;
            }

            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[V2Transport] ReportState 失败");
        }
    }

    public async Task ReportMetadataAsync(Dictionary<string, JsonElement> metadata, CancellationToken ct = default)
    {
        if (_isClosed != 0 || _isInitialized == 0) return;

        try
        {
            var json = JsonSerializer.Serialize(metadata, TransportBridgeJsonContext.Default.DictionaryStringJsonElement);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _writeClient.PutAsync($"{_options.ApiBaseUrl}/worker", content, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[V2Transport] ReportMetadata 失败");
        }
    }

    public async Task ReportDeliveryAsync(string eventId, string status, CancellationToken ct = default)
    {
        if (_isClosed != 0 || _isInitialized == 0) return;

        var payload = $"{{\"event_id\":\"{eventId}\",\"status\":\"{status}\"}}";
        await _deliveryUploader.EnqueueAsync(payload, ct).ConfigureAwait(false);
    }

    public async Task FlushAsync(CancellationToken ct = default)
    {
        // 对齐 TS 端: 等待事件上传器排空
        await _eventUploader.FlushAsync(ct).ConfigureAwait(false);
        await _deliveryUploader.FlushAsync(ct).ConfigureAwait(false);
    }

    #region CCRClient 初始化

    private async Task InitializeCcrAsync()
    {
        try
        {
            // 如果没有 epoch，需要调用 registerWorker 获取
            if (_options.Epoch is null)
            {
                var epoch = await RegisterWorkerAsync().ConfigureAwait(false);
                Interlocked.Exchange(ref _epoch, epoch);
            }

            Interlocked.Exchange(ref _isInitialized, 1);
            _logger?.LogInformation(
                "[V2Transport] CCRClient 已初始化 (epoch={Epoch}, sessionId={SessionId})",
                _epoch, _options.SessionId);

            // 启动心跳
            _heartbeatTask = RunHeartbeatLoopAsync(_heartbeatCts.Token);

            // 通知连接成功
            _onConnectCallback?.Invoke();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[V2Transport] CCRClient 初始化失败");
            Close();
            _onCloseCallback?.Invoke(4091); // 4091 = 初始化失败
        }
    }

    /// <summary>
    /// 注册 Worker — 对齐 TS 端 registerWorker
    /// POST /worker/register → 获取 epoch
    /// </summary>
    private async Task<int> RegisterWorkerAsync()
    {
        var payload = $"{{\"session_id\":\"{_options.SessionId}\"}}";
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var response = await _writeClient.PostAsync($"{_options.ApiBaseUrl}/worker/register", content).ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            throw new TransportFatalError("Worker epoch conflict during registration", (int)response.StatusCode, "epoch_conflict");
        }

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var result = JsonSerializer.Deserialize(json, TransportBridgeJsonContext.Default.DictionaryStringJsonElement);
        if (result?.TryGetValue("epoch", out var epochValue) == true && epochValue.ValueKind == JsonValueKind.Number)
        {
            return epochValue.GetInt32();
        }

        return 0;
    }

    #endregion

    #region SSE 读流

    /// <summary>
    /// SSE 读循环 — 对齐 TS 端 SSETransport
    /// 解析 SSE 帧，提取事件数据和序列号
    /// </summary>
    private async Task RunSseReadLoopAsync()
    {
        while (!_heartbeatCts.IsCancellationRequested && _isClosed == 0)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, _options.SseUrl);
                request.Headers.Add("Accept", "text/event-stream");
                request.Headers.Add("Cache-Control", "no-cache");

                // 携带序列号以避免全量回放
                if (_lastSequenceNum > 0)
                {
                    request.Headers.Add("Last-Event-ID", _lastSequenceNum.ToString());
                }

                SetAuthHeaders(_sseClient, GetEffectiveToken());

                using var response = await _sseClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    _heartbeatCts.Token).ConfigureAwait(false);

                // 永久拒绝码: 401/403/404 — 对齐 TS 端 SSETransport
                if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized
                    or System.Net.HttpStatusCode.Forbidden
                    or System.Net.HttpStatusCode.NotFound)
                {
                    _logger?.LogError("[V2Transport] SSE 永久拒绝: {StatusCode}", response.StatusCode);
                    _onCloseCallback?.Invoke((int)response.StatusCode);
                    return;
                }

                response.EnsureSuccessStatusCode();

                await using var stream = await response.Content.ReadAsStreamAsync(_heartbeatCts.Token).ConfigureAwait(false);
                using var reader = new StreamReader(stream, Encoding.UTF8);

                var currentEventType = string.Empty;
                var currentData = new StringBuilder();

                while (!_heartbeatCts.IsCancellationRequested && _isClosed == 0)
                {
                    var line = await reader.ReadLineAsync(_heartbeatCts.Token).ConfigureAwait(false);
                    if (line is null) break;

                    if (string.IsNullOrEmpty(line))
                    {
                        // 空行 = 事件结束
                        if (currentData.Length > 0)
                        {
                            var data = currentData.ToString();
                            _onDataCallback?.Invoke(data);

                            // 同时上报 received + processed — 对齐 TS 端 v2 适配器的 ACK 策略
                            _ = ReportDeliveryFromEventAsync(data);
                        }

                        currentData.Clear();
                        currentEventType = string.Empty;
                        continue;
                    }

                    if (line.StartsWith("id: "))
                    {
                        // SSE 事件 ID（序列号）
                        if (int.TryParse(line[4..], out var seqNum))
                        {
                            _lastSequenceNum = seqNum;
                        }
                    }
                    else if (line.StartsWith("event: "))
                    {
                        currentEventType = line[7..];
                    }
                    else if (line.StartsWith("data: "))
                    {
                        currentData.AppendLine(line[6..]);
                    }
                    else if (line.StartsWith("data:"))
                    {
                        currentData.AppendLine(line[5..]);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[V2Transport] SSE 读流错误，将重连");
                await Task.Delay(1000, _heartbeatCts.Token).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// 从 SSE 事件数据中提取 event_id 并上报投递状态
    /// </summary>
    private async Task ReportDeliveryFromEventAsync(string data)
    {
        try
        {
            // 尝试从 JSON 中提取 event_id
            var jsonDoc = JsonDocument.Parse(data);
            if (jsonDoc.RootElement.TryGetProperty("event_id", out var eventIdProp))
            {
                var eventId = eventIdProp.GetString();
                if (eventId is not null)
                {
                    await ReportDeliveryAsync(eventId, "received").ConfigureAwait(false);
                    await ReportDeliveryAsync(eventId, "processed").ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            // 非致命: 解析失败不影响主流程
            System.Diagnostics.Trace.WriteLine($"[V2ReplBridgeTransport] Parse SSE event failed: {ex.Message}");
        }
    }

    #endregion

    #region 心跳循环

    /// <summary>
    /// 心跳循环 — 对齐 TS 端 CCRClient heartbeat
    /// POST /worker/heartbeat 延长工作租约
    /// </summary>
    private async Task RunHeartbeatLoopAsync(CancellationToken ct)
    {
        var intervalMs = _options.HeartbeatIntervalMs;

        while (!ct.IsCancellationRequested && _isClosed == 0)
        {
            try
            {
                // 添加抖动 — 对齐 TS 端 heartbeatJitterFraction
                var jitterMs = _options.HeartbeatJitterFraction > 0
                    ? (int)(intervalMs * _options.HeartbeatJitterFraction * (Random.Shared.NextDouble() * 2 - 1))
                    : 0;

                await Task.Delay(intervalMs + jitterMs, ct).ConfigureAwait(false);

                if (_isClosed != 0 || _isInitialized == 0) continue;

                var response = await _writeClient.PostAsync(
                    $"{_options.ApiBaseUrl}/worker/heartbeat",
                    null, ct).ConfigureAwait(false);

                if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
                {
                    HandleEpochMismatch("Heartbeat");
                    return;
                }

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    _logger?.LogError("[V2Transport] 心跳认证失败");
                    _onCloseCallback?.Invoke((int)response.StatusCode);
                    return;
                }

                response.EnsureSuccessStatusCode();
                _logger?.LogDebug("[V2Transport] 心跳已发送");
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[V2Transport] 心跳发送失败");
            }
        }
    }

    #endregion

    #region Epoch 管理

    /// <summary>
    /// Epoch 不匹配处理 — 对齐 TS 端 onEpochMismatch
    /// 409 Conflict = 当前 worker 被另一个 worker 取代
    /// </summary>
    private void HandleEpochMismatch(string source)
    {
        _logger?.LogWarning("[V2Transport] Epoch 被取代 (来源: {Source})，关闭传输以触发轮询恢复", source);
        Close();
        _onCloseCallback?.Invoke(4090); // 4090 = epoch 不匹配
    }

    #endregion

    #region 认证

    private string? GetEffectiveToken()
    {
        return _options.GetAuthToken?.Invoke() ?? _options.IngressToken;
    }

    private static void SetAuthHeaders(HttpClient client, string? token)
    {
        client.DefaultRequestHeaders.Remove("Authorization");
        if (!string.IsNullOrEmpty(token))
        {
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
        }
    }

    #endregion

    public async ValueTask DisposeAsync()
    {
        Close();
        _heartbeatCts.Dispose();
        _writeLock.Dispose();
        await (_sseReadTask ?? Task.CompletedTask).ConfigureAwait(false);
        await (_heartbeatTask ?? Task.CompletedTask).ConfigureAwait(false);
    }
}
