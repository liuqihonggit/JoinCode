namespace McpClient.Transports;

/// <summary>
/// MCP 客户端传输降级链 — 按优先级依次尝试多个传输,运行时错误自动切换到下一个可用传输。
/// 集成熔断器、健康检查与降级指标,提供容错的连接管理。
/// </summary>
public sealed class McpTransportFallbackChain : IMcpTransport
{
    private readonly IMcpTransport[] _transports;
    private readonly ITransportHealthCheck[] _healthChecks;
    private readonly TransportFallbackConfig _config;
    private readonly UnifiedCircuitBreaker[] _circuitBreakers;
    private readonly TransportFallbackMetrics _metrics;
    private readonly ILogger? _logger;
    private IMcpTransport? _activeTransport;
    private int _activeIndex = -1;
    private readonly AsyncLock _switchLock = new();
    private int _disposed;

    /// <summary>收到 JSON-RPC 消息事件</summary>
    public event EventHandler<McpMessageReceivedEventArgs>? MessageReceived;
    /// <summary>传输错误事件</summary>
    public event EventHandler<McpTransportErrorEventArgs>? ErrorOccurred;
    /// <summary>降级切换事件 — 当活跃传输失败并切换到备用传输时触发</summary>
    public event EventHandler<TransportFallbackEventArgs>? FallbackOccurred;

    /// <summary>当前是否运行中</summary>
    public bool IsRunning => _activeTransport?.IsRunning ?? false;
    /// <summary>当前活跃传输的类型名</summary>
    public string ActiveTransportType => _activeTransport?.GetType().Name ?? "none";
    /// <summary>当前活跃传输在传输数组中的索引,无活跃传输时为 -1</summary>
    public int ActiveTransportIndex => _activeIndex;
    /// <summary>降级链配置</summary>
    public TransportFallbackConfig Config => _config;
    /// <summary>降级指标快照</summary>
    public TransportFallbackMetrics Metrics => _metrics;
    /// <summary>各传输对应的熔断器数组</summary>
    public UnifiedCircuitBreaker[] CircuitBreakers => _circuitBreakers;

    /// <summary>
    /// 创建传输降级链
    /// </summary>
    /// <param name="transports">按优先级降序排列的传输数组,至少一个</param>
    /// <param name="healthChecks">各传输对应的健康检查,可为空数组</param>
    /// <param name="config">降级链配置;null 则使用默认配置</param>
    /// <param name="logger">日志记录器,可为 null</param>
    public McpTransportFallbackChain(
        IMcpTransport[] transports,
        ITransportHealthCheck[] healthChecks,
        TransportFallbackConfig config,
        ILogger? logger = null)
    {
        _transports = transports ?? throw new ArgumentNullException(nameof(transports));
        _healthChecks = healthChecks ?? [];
        _config = config ?? new TransportFallbackConfig();
        _logger = logger;

        if (_transports.Length == 0)
            throw new ArgumentException("At least one transport is required", nameof(transports));

        _circuitBreakers = new UnifiedCircuitBreaker[_transports.Length];
        for (var i = 0; i < _transports.Length; i++)
        {
            _circuitBreakers[i] = new UnifiedCircuitBreaker(
                $"mcp-transport-{i}",
                _config.CircuitBreakerFailureThreshold,
                TimeSpan.FromMilliseconds(_config.CircuitBreakerCoolDownMs));
        }

        _metrics = new TransportFallbackMetrics(_transports.Length);
    }

    /// <summary>
    /// 启动降级链 — 按优先级依次尝试传输,首个通过健康检查并连接成功的传输成为活跃传输。
    /// 全部失败时抛出 InvalidOperationException。
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步启动操作的任务</returns>
    public async Task StartAsync(CancellationToken ct = default)
    {
        if (IsRunning) return;

        if (!_config.Enabled)
        {
            _logger?.LogWarning("[TransportFallback] Fallback chain disabled, using first transport only");
            await _transports[0].StartAsync(ct).ConfigureAwait(false);
            _activeTransport = _transports[0];
            _activeIndex = 0;
            WireEvents(_activeTransport);
            return;
        }

        using var chainCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        chainCts.CancelAfter(_config.ChainTimeoutMs);

        var chainStart = DateTimeOffset.UtcNow;

        for (var i = 0; i < _transports.Length; i++)
        {
            if (chainCts.Token.IsCancellationRequested) break;

            if (_config.CircuitBreakerEnabled && !_circuitBreakers[i].TryProbe())
            {
                _logger?.LogWarning("[TransportFallback] Transport {Type} circuit breaker open, skipping (failures={Failures}, cooldown={CooldownMs}ms)",
                    _transports[i].GetType().Name, _circuitBreakers[i].ConsecutiveFailures, _config.CircuitBreakerCoolDownMs);
                continue;
            }

            if (_config.HealthCheckEnabled && i < _healthChecks.Length)
            {
                using var hcCts = CancellationTokenSource.CreateLinkedTokenSource(chainCts.Token);
                hcCts.CancelAfter(_config.HealthCheckTimeoutMs);

                TransportHealthResult health;
                try
                {
                    health = await _healthChecks[i].CheckAsync(hcCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    health = TransportHealthResult.Unavailable(
                        _healthChecks[i].TransportType,
                        TransportUnavailabilityCategory.NetworkUnreachable,
                        "Health check timed out", TimeSpan.FromMilliseconds(_config.HealthCheckTimeoutMs));
                }

                if (!health.IsAvailable)
                {
                    _logger?.LogWarning("[TransportFallback] Transport {Type} health check failed: {Reason} (category={Category}, duration={Duration}ms)",
                        _transports[i].GetType().Name, health.UnavailableReason, health.Category, health.CheckDuration.TotalMilliseconds);
                    _circuitBreakers[i].RecordFailure();
                    _metrics.RecordFailure(i);
                    continue;
                }
            }

            using var transportCts = CancellationTokenSource.CreateLinkedTokenSource(chainCts.Token);
            transportCts.CancelAfter(_config.ConnectTimeoutMs);

            try
            {
                await _transports[i].StartAsync(transportCts.Token).ConfigureAwait(false);
                _activeTransport = _transports[i];
                _activeIndex = i;
                _circuitBreakers[i].RecordSuccess();
                _metrics.RecordConnection(i);
                WireEvents(_activeTransport);

                var elapsed = (DateTimeOffset.UtcNow - chainStart).TotalMilliseconds;
                _logger?.LogInformation("[TransportFallback] Connected via {Type} (priority={Priority}, elapsed={Elapsed}ms)",
                    _transports[i].GetType().Name, i + 1, elapsed);
                return;
            }
            catch (OperationCanceledException) when (transportCts.Token.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                _logger?.LogWarning("[TransportFallback] Transport {Type} connect timeout ({TimeoutMs}ms), trying next",
                    _transports[i].GetType().Name, _config.ConnectTimeoutMs);
                _circuitBreakers[i].RecordFailure();
                _metrics.RecordFailure(i);
            }
            catch (Exception ex) when (i < _transports.Length - 1 && !ct.IsCancellationRequested)
            {
                _logger?.LogWarning(ex, "[TransportFallback] Transport {Type} connect failed, trying next",
                    _transports[i].GetType().Name);
                _circuitBreakers[i].RecordFailure();
                _metrics.RecordFailure(i);
            }
        }

        var openCount = CountCircuitOpen();
        throw new InvalidOperationException(
            $"All transports failed (attempted={_transports.Length}, circuitOpen={openCount}, chainTimeout={_config.ChainTimeoutMs}ms)");
    }

    /// <summary>
    /// 停止活跃传输并解绑事件
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步停止操作的任务</returns>
    public async Task StopAsync(CancellationToken ct = default)
    {
        if (_activeTransport is not null)
        {
            UnwireEvents(_activeTransport);
            await _activeTransport.StopAsync(ct).ConfigureAwait(false);
            _activeTransport = null;
            _activeIndex = -1;
        }
    }

    /// <summary>
    /// 通过活跃传输发送 JSON-RPC 消息
    /// </summary>
    /// <param name="message">JSON-RPC 消息</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>表示异步发送操作的任务</returns>
    public async Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        if (_activeTransport is null)
            throw new InvalidOperationException("No active transport");

        await _activeTransport.SendMessageAsync(message, cancellationToken).ConfigureAwait(false);
    }

    private async Task OnActiveTransportErrorAsync(Exception ex)
    {
        if (_activeIndex < 0 || _activeIndex >= _transports.Length - 1) return;

        IMcpTransport? oldTransport;
        int nextIndex;
        int oldIndex;

        using (var guard = await _switchLock.TryLockAsync(CancellationToken.None).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_switchLock.Name}' 等待超时"))
        {
            if (_activeIndex >= _transports.Length - 1) return;

            nextIndex = FindNextAvailableTransport(_activeIndex + 1);
            if (nextIndex < 0)
            {
                _logger?.LogWarning("[TransportFallback] No available fallback transport (all circuit breakers open)");
                return;
            }

            oldTransport = _activeTransport;
            oldIndex = _activeIndex;
        }

        _logger?.LogWarning(ex, "[TransportFallback] Transport {FromType} connection lost, falling back to {ToType}",
            _transports[oldIndex].GetType().Name, _transports[nextIndex].GetType().Name);

        var fallbackStart = DateTimeOffset.UtcNow;

        try
        {
            if (oldTransport is not null)
            {
                UnwireEvents(oldTransport);
                await oldTransport.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }

            await _transports[nextIndex].StartAsync(CancellationToken.None).ConfigureAwait(false);

            string fromType;
            using (var guard = await _switchLock.TryLockAsync(CancellationToken.None).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_switchLock.Name}' 等待超时"))
            {
                fromType = _transports[oldIndex].GetType().Name;
                _activeTransport = _transports[nextIndex];
                _activeIndex = nextIndex;
                WireEvents(_activeTransport);
            }

            _circuitBreakers[nextIndex].RecordSuccess();
            var duration = (DateTimeOffset.UtcNow - fallbackStart).TotalMilliseconds;
            _metrics.RecordFallback(oldIndex, nextIndex, (long)duration);

            FallbackOccurred?.Invoke(this, new TransportFallbackEventArgs
            {
                FromTransportType = fromType,
                ToTransportType = _transports[nextIndex].GetType().Name,
                Reason = ex.Message,
                IsServerSide = false,
                FromPriority = oldIndex + 1,
                ToPriority = nextIndex + 1,
            });

            _logger?.LogInformation("[TransportFallback] Fallback to {Type} succeeded (duration={Duration}ms)",
                _transports[nextIndex].GetType().Name, duration);
        }
        catch (Exception fallbackEx)
        {
            _logger?.LogWarning(fallbackEx, "[TransportFallback] Fallback to {Type} also failed",
                _transports[nextIndex].GetType().Name);
            _circuitBreakers[nextIndex].RecordFailure();
        }

    }

    private int FindNextAvailableTransport(int startIndex)
    {
        for (var i = startIndex; i < _transports.Length; i++)
        {
            if (!_config.CircuitBreakerEnabled || !_circuitBreakers[i].IsOpen)
                return i;
        }
        return -1;
    }

    private int CountCircuitOpen()
    {
        var count = 0;
        for (var i = 0; i < _circuitBreakers.Length; i++)
        {
            if (_circuitBreakers[i].IsOpen) count++;
        }
        return count;
    }

    private void WireEvents(IMcpTransport transport)
    {
        transport.MessageReceived += OnTransportMessageReceived;
        transport.ErrorOccurred += OnTransportError;
    }

    private void UnwireEvents(IMcpTransport transport)
    {
        transport.MessageReceived -= OnTransportMessageReceived;
        transport.ErrorOccurred -= OnTransportError;
    }

    private void OnTransportMessageReceived(object? sender, McpMessageReceivedEventArgs e)
    {
        MessageReceived?.Invoke(this, e);
    }

    private void OnTransportError(object? sender, McpTransportErrorEventArgs e)
    {
        ErrorOccurred?.Invoke(this, e);

        if (IsRunning)
        {
            _ = OnActiveTransportErrorAsync(e.Exception);
        }
    }

    /// <summary>
    /// 异步释放资源 — 停止活跃传输、释放切换锁、释放全部传输
    /// </summary>
    /// <returns>表示异步释放操作的任务</returns>
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return ValueTask.CompletedTask;

        _ = StopAsync(CancellationToken.None);
        _switchLock.Dispose();

        foreach (var transport in _transports)
        {
            _ = transport.DisposeAsync();
        }

        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}
