using JoinCode.Abstractions.Attributes;

namespace JoinCode.Transport.Bridge;

// TransportProtocol, TransportConnectionState 已迁移到 JoinCode.Transport.Bridge 命名空间 (Transport.Contracts)

/// <summary>
/// 连接管理器 - 管理传输连接生命周期和重连逻辑
/// </summary>
[Register]
public sealed partial class ConnectionManager : IConnectionManager
{
    private readonly ILogger? _logger;
    private readonly TransportConfiguration _config;
    private readonly SemaphoreSlim _stateLock;

    private IBridgeTransport? _currentTransport;
    private TransportProtocol _currentProtocol;
    private TransportConnectionState _connectionState;

    private CancellationTokenSource? _reconnectCts;
    private Task? _reconnectTask;
    private int _reconnectAttemptCount;
    private int _isDisposed;

    public TransportConnectionState ConnectionState => _connectionState;

    public async ValueTask<TransportConnectionState> GetConnectionStateAsync(CancellationToken ct = default)
    {
        await _stateLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return _connectionState;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    private async Task SetConnectionStateAsync(TransportConnectionState value, CancellationToken ct = default)
    {
        await _stateLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var oldState = _connectionState;
            _connectionState = value;
            if (oldState != value)
            {
                _logger?.LogDebug("[ConnectionManager] 连接状态变更: {OldState} -> {NewState}", oldState, value);
                ConnectionStateChanged?.Invoke(this, new TransportStateChangedEventArgs(oldState, value));
            }
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public TransportProtocol CurrentProtocol => _currentProtocol;
    public bool IsConnected => _connectionState == TransportConnectionState.Connected;
    public int ReconnectAttemptCount => _reconnectAttemptCount;

    public event EventHandler<TransportStateChangedEventArgs>? ConnectionStateChanged;
    public event EventHandler? Reconnecting;
    public event EventHandler? Reconnected;
    public event EventHandler<TransportErrorEventArgs>? ErrorOccurred;

    public ConnectionManager(
        TransportConfiguration config,
        ILogger? logger = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger;
        _connectionState = TransportConnectionState.Disconnected;
        _currentProtocol = config.PreferredProtocol;
        _stateLock = new SemaphoreSlim(1, 1);
    }

    /// <summary>
    /// 启动连接
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var currentState = await GetConnectionStateAsync(cancellationToken).ConfigureAwait(false);
        if (IsConnected || currentState == TransportConnectionState.Connecting)
        {
            _logger?.LogWarning("[ConnectionManager] 传输已在运行或正在连接");
            return;
        }

        await SetConnectionStateAsync(TransportConnectionState.Connecting, cancellationToken).ConfigureAwait(false);

        try
        {
            await InitializeTransportAsync(cancellationToken).ConfigureAwait(false);
            await SetConnectionStateAsync(TransportConnectionState.Connected, cancellationToken).ConfigureAwait(false);
            _reconnectAttemptCount = 0;
            _logger?.LogInformation("[ConnectionManager] 传输已启动，协议: {Protocol}", _currentProtocol);
        }
        catch (Exception ex)
        {
            await SetConnectionStateAsync(TransportConnectionState.Error, cancellationToken).ConfigureAwait(false);
            _logger?.LogError(ex, "[ConnectionManager] 启动传输失败");
            ErrorOccurred?.Invoke(this, new TransportErrorEventArgs(ex, "启动传输失败"));

            if (_config.AutoReconnect)
            {
                StartReconnectLoop(cancellationToken);
            }
            throw;
        }
    }

    /// <summary>
    /// 停止连接
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _reconnectCts?.Cancel();

        if (_reconnectTask is not null)
        {
            try
            {
                await _reconnectTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        if (_currentTransport is not null)
        {
            await _currentTransport.StopAsync(cancellationToken).ConfigureAwait(false);
            _currentTransport.ErrorOccurred -= OnTransportError;
            _currentTransport = null;
        }

        await SetConnectionStateAsync(TransportConnectionState.Disconnected, cancellationToken).ConfigureAwait(false);
        _logger?.LogInformation("[ConnectionManager] 传输已停止");
    }

    /// <summary>
    /// 切换传输协议
    /// </summary>
    public async Task SwitchProtocolAsync(TransportProtocol protocol, CancellationToken cancellationToken = default)
    {
        if (_currentProtocol == protocol)
        {
            return;
        }

        _logger?.LogInformation("[ConnectionManager] 切换协议: {OldProtocol} -> {NewProtocol}", _currentProtocol, protocol);

        await StopAsync(cancellationToken).ConfigureAwait(false);
        _currentProtocol = protocol;
        await StartAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 发送消息
    /// </summary>
    public async Task SendMessageAsync(string messageJson, CancellationToken cancellationToken = default)
    {
        if (_currentTransport is null || !IsConnected)
        {
            throw new InvalidOperationException("传输未连接");
        }

        await _currentTransport.SendAsync(messageJson, cancellationToken).ConfigureAwait(false);
        _logger?.LogDebug("[ConnectionManager] 消息已发送");
    }

    /// <summary>
    /// 注册消息接收回调
    /// </summary>
    public void OnMessageReceived(Func<string, Task> handler)
    {
        if (_currentTransport is not null)
        {
            _currentTransport.MessageReceived += async (_, e) => await handler(e.Message).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 初始化传输
    /// </summary>
    private async Task InitializeTransportAsync(CancellationToken cancellationToken)
    {
        _currentTransport = _currentProtocol switch
        {
            TransportProtocol.WebSocket => new WebSocketTransport(_config.WebSocketEndpoint, _logger),
            TransportProtocol.Sse => new SseBridgeTransport(_config.SseEndpoint, _logger),
            _ => throw new NotSupportedException($"不支持的协议: {_currentProtocol}")
        };

        _currentTransport.ErrorOccurred += OnTransportError;

        await _currentTransport.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 处理传输层错误
    /// </summary>
    private void OnTransportError(object? sender, TransportErrorEventArgs e)
    {
        _logger?.LogError(e.Exception, "[ConnectionManager] 传输错误: {Message}", e.Message);
        ErrorOccurred?.Invoke(this, e);

        if (IsConnected && _config.AutoReconnect)
        {
            _ = HandleTransportErrorAsync(_reconnectCts?.Token ?? CancellationToken.None);
        }
    }

    private async Task HandleTransportErrorAsync(CancellationToken cancellationToken = default)
    {
        await SetConnectionStateAsync(TransportConnectionState.Error, cancellationToken).ConfigureAwait(false);
        StartReconnectLoop();
    }

    /// <summary>
    /// 启动重连循环
    /// </summary>
    private void StartReconnectLoop(CancellationToken cancellationToken = default)
    {
        if (_reconnectTask is { IsCompleted: false })
        {
            return;
        }

        _reconnectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _reconnectTask = RunReconnectLoopAsync(_reconnectCts.Token);
    }

    /// <summary>
    /// 重连循环
    /// </summary>
    private async Task RunReconnectLoopAsync(CancellationToken cancellationToken)
    {
        await SetConnectionStateAsync(TransportConnectionState.Reconnecting, cancellationToken).ConfigureAwait(false);
        Reconnecting?.Invoke(this, EventArgs.Empty);

        while (!cancellationToken.IsCancellationRequested && _reconnectAttemptCount < _config.MaxReconnectAttempts)
        {
            _reconnectAttemptCount++;
            var delay = CalculateReconnectDelay(_reconnectAttemptCount);

            _logger?.LogInformation(
                "[ConnectionManager] 第 {Attempt} 次重连尝试，等待 {Delay}ms",
                _reconnectAttemptCount,
                delay.TotalMilliseconds);

            try
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

                await InitializeTransportAsync(cancellationToken).ConfigureAwait(false);

                await SetConnectionStateAsync(TransportConnectionState.Connected, cancellationToken).ConfigureAwait(false);
                _reconnectAttemptCount = 0;
                _logger?.LogInformation("[ConnectionManager] 重连成功");
                Reconnected?.Invoke(this, EventArgs.Empty);
                return;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[ConnectionManager] 重连失败");
            }
        }

        await SetConnectionStateAsync(TransportConnectionState.Error, cancellationToken).ConfigureAwait(false);
        _logger?.LogError("[ConnectionManager] 重连次数耗尽，放弃重连");
    }

    /// <summary>
    /// 计算重连延迟（指数退避）
    /// </summary>
    private TimeSpan CalculateReconnectDelay(int attempt)
    {
        return TimeSpan.FromMilliseconds(
            Math.Min(
                _config.ReconnectDelayMs * Math.Pow(2, attempt - 1),
                _config.MaxReconnectDelayMs));
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1)
        {
            return;
        }

        await StopAsync().ConfigureAwait(false);
        _reconnectCts?.Dispose();
        _stateLock.Dispose();
    }
}
