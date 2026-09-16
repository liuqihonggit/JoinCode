namespace McpClient.Transports;

/// <summary>
/// MCP 服务端传输降级链 — 按优先级依次尝试多个服务端传输,
/// 运行时错误时降级到下一个传输(无熔断器/健康检查,轻量版)。
/// </summary>
public sealed class McpServerTransportFallbackChain : IMcpTransport
{
    private readonly IMcpTransport[] _transports;
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

    /// <summary>
    /// 创建服务端传输降级链
    /// </summary>
    /// <param name="transports">按优先级降序排列的传输数组,至少一个</param>
    /// <param name="logger">日志记录器,可为 null</param>
    public McpServerTransportFallbackChain(
        IMcpTransport[] transports,
        ILogger? logger = null)
    {
        _transports = transports ?? throw new ArgumentNullException(nameof(transports));
        _logger = logger;

        if (_transports.Length == 0)
            throw new ArgumentException("At least one transport is required", nameof(transports));
    }

    /// <summary>
    /// 启动服务端降级链 — 按优先级依次尝试传输,首个启动成功的传输成为活跃传输。
    /// 全部失败时抛出 InvalidOperationException。
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步启动操作的任务</returns>
    public async Task StartAsync(CancellationToken ct = default)
    {
        if (IsRunning) return;

        for (var i = 0; i < _transports.Length; i++)
        {
            try
            {
                _logger?.LogInformation("[ServerFallback] Starting transport {Type} (priority={Priority})",
                    _transports[i].GetType().Name, i + 1);

                await _transports[i].StartAsync(ct).ConfigureAwait(false);
                _activeTransport = _transports[i];
                _activeIndex = i;
                WireEvents(_activeTransport);

                _logger?.LogInformation("[ServerFallback] Server started on {Type} (priority={Priority})",
                    _transports[i].GetType().Name, i + 1);
                return;
            }
            catch (Exception ex) when (i < _transports.Length - 1)
            {
                _logger?.LogWarning(ex, "[ServerFallback] Transport {Type} start failed, falling back to next",
                    _transports[i].GetType().Name);
            }
        }

        throw new InvalidOperationException("All server transports failed to start");
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

        using var guard = await _switchLock.TryLockAsync(CancellationToken.None).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_switchLock.Name}' 等待超时");

        if (_activeIndex >= _transports.Length - 1) return;

        var fromType = _transports[_activeIndex].GetType().Name;
        var nextIndex = _activeIndex + 1;

        _logger?.LogWarning(ex, "[ServerFallback] Transport {FromType} runtime error, degrading to {ToType}",
            fromType, _transports[nextIndex].GetType().Name);

        if (_activeTransport is not null)
        {
            UnwireEvents(_activeTransport);
            await _activeTransport.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }

        try
        {
            await _transports[nextIndex].StartAsync(CancellationToken.None).ConfigureAwait(false);
            _activeTransport = _transports[nextIndex];
            _activeIndex = nextIndex;
            WireEvents(_activeTransport);

            FallbackOccurred?.Invoke(this, new TransportFallbackEventArgs
            {
                FromTransportType = fromType,
                ToTransportType = _transports[nextIndex].GetType().Name,
                Reason = ex.Message,
                IsServerSide = true,
                FromPriority = _activeIndex,
                ToPriority = nextIndex + 1,
            });

            _logger?.LogInformation("[ServerFallback] Degraded to {Type} successfully",
                _transports[nextIndex].GetType().Name);
        }
        catch (Exception fallbackEx)
        {
            _logger?.LogError(fallbackEx, "[ServerFallback] Degradation to {Type} also failed",
                _transports[nextIndex].GetType().Name);
        }
    
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
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;

        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        _switchLock.Dispose();

        foreach (var transport in _transports)
        {
            await transport.DisposeAsync().ConfigureAwait(false);
        }

        GC.SuppressFinalize(this);
    }
}
