namespace McpClient.Transports;

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

    public event EventHandler<McpMessageReceivedEventArgs>? MessageReceived;
    public event EventHandler<McpTransportErrorEventArgs>? ErrorOccurred;
    public event EventHandler<TransportFallbackEventArgs>? FallbackOccurred;

    public bool IsRunning => _activeTransport?.IsRunning ?? false;
    public string ActiveTransportType => _activeTransport?.GetType().Name ?? "none";
    public int ActiveTransportIndex => _activeIndex;
    public TransportFallbackConfig Config => _config;
    public TransportFallbackMetrics Metrics => _metrics;
    public UnifiedCircuitBreaker[] CircuitBreakers => _circuitBreakers;

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

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;

        await StopAsync().ConfigureAwait(false);
        _switchLock.Dispose();

        foreach (var transport in _transports)
        {
            await transport.DisposeAsync().ConfigureAwait(false);
        }

        GC.SuppressFinalize(this);
    }
}
