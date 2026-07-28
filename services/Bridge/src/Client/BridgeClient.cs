
namespace Core.Bridge;

using JoinCode.Abstractions.Attributes;

/// <summary>
/// Bridge 客户端 - 模仿 Claude Code 的 replBridge.ts 架构
/// 实现消息轮询循环、消息去重、Echo 过滤和重连逻辑
/// </summary>
[Register]
public sealed partial class BridgeClient : IAsyncDisposable
{
    private readonly ITransportManager _transportManager;
    private readonly MessageHandlerCoordinator _messageHandler;
    private readonly BoundedUUIDSet _processedMessageIds;
    [Inject] private readonly ILogger<BridgeClient>? _logger;
    private readonly IClockService _clock;
    private readonly BridgeClientOptions _options;
    private readonly BridgeJwtService? _jwtService;
    private readonly PollConfigManager? _pollConfigManager;
    private readonly BridgeSessionRunner? _sessionRunner;
    private readonly BridgeApiClient? _apiClient;
    private string? _authToken;

    private CancellationTokenSource? _pollingCts;
    private Task? _pollingTask;
    private volatile int _isRunning;
    private readonly SemaphoreSlim _stateLock;
    private int _isDisposed;

    // 统计信息
    private long _totalMessagesReceived;
    private long _totalMessagesProcessed;
    private long _totalEchoFiltered;
    private long _totalDuplicatesFiltered;
    private DateTime _startedAt;

    public bool IsRunning => Interlocked.CompareExchange(ref _isRunning, 0, 0) != 0;

    public async ValueTask<BridgeClientState> GetStateAsync(CancellationToken ct = default)
    {
        await _stateLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return new BridgeClientState
            {
                IsRunning = IsRunning,
                ConnectionState = _transportManager.ConnectionState,
                TotalMessagesReceived = _totalMessagesReceived,
                TotalMessagesProcessed = _totalMessagesProcessed,
                TotalEchoFiltered = _totalEchoFiltered,
                TotalDuplicatesFiltered = _totalDuplicatesFiltered,
                Uptime = _clock.GetUtcNow() - _startedAt,
                HasJwtToken = _authToken != null,
                HasActiveSession = _sessionRunner?.GetActiveSessions().Count > 0,
            };
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public event EventHandler<BridgeMessageReceivedEventArgs>? MessageReceived;
    public event EventHandler<BridgeMessageProcessedEventArgs>? MessageProcessed;
    public event EventHandler<BridgeClientErrorEventArgs>? ErrorOccurred;
    public event EventHandler<StateChangedEventArgs<TransportConnectionState>>? ConnectionStateChanged;
    public event EventHandler? Started;
    public event EventHandler? Stopped;

    public BridgeClient(
        ITransportManager transportManager,
        MessageHandlerCoordinator messageHandler,
        BridgeClientSession? clientSession = null,
        BridgeClientOptions? options = null,
        ILogger<BridgeClient>? logger = null,
        IClockService? clock = null)
    {
        _transportManager = transportManager ?? throw new ArgumentNullException(nameof(transportManager));
        _messageHandler = messageHandler ?? throw new ArgumentNullException(nameof(messageHandler));
        _options = options ?? new BridgeClientOptions();
        _logger = logger;
        _clock = clock ?? SystemClockService.Instance;
        _processedMessageIds = new BoundedUUIDSet(_options.MessageDeduplicationCapacity);
        _stateLock = new SemaphoreSlim(1, 1);
        _jwtService = clientSession?.JwtService;
        _pollConfigManager = clientSession?.PollConfigManager;
        _sessionRunner = clientSession?.SessionRunner;
        _apiClient = clientSession?.ApiClient;

        // 订阅传输层事件
        _transportManager.MessageReceived += OnTransportMessageReceived;
        _transportManager.ConnectionStateChanged += OnConnectionStateChanged;
        _transportManager.ErrorOccurred += OnTransportError;
        _transportManager.Reconnecting += OnReconnecting;
        _transportManager.Reconnected += OnReconnected;
    }

    #region 公共方法

    /// <summary>
    /// 启动 Bridge 客户端
    /// 开始消息轮询循环
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsRunning)
            {
                _logger?.LogWarning("[BridgeClient] 客户端已在运行");
                return;
            }
            Interlocked.Exchange(ref _isRunning, 1);
            _startedAt = _clock.GetUtcNow();
        }
        finally
        {
            _stateLock.Release();
        }

        try
        {
            _logger?.LogInformation("[BridgeClient] 启动客户端...");

            // 启动传输层
            await _transportManager.StartAsync(cancellationToken).ConfigureAwait(false);

            // Generate JWT token if service is available
            if (_jwtService != null)
            {
                _authToken = _jwtService.GenerateToken("bridge-client", _options.HeartbeatIntervalMs / 1000 * 300);
                _logger?.LogInformation("[BridgeClient] JWT Token 已生成");
            }

            // Create session if runner is available
            if (_sessionRunner != null)
            {
                await _sessionRunner.StartSessionAsync("bridge-client", new Dictionary<string, string> { ["transport"] = "websocket" }).ConfigureAwait(false);
                _logger?.LogInformation("[BridgeClient] Bridge 会话已创建");
            }

            // 启动消息轮询循环
            _pollingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _pollingTask = RunPollingLoopAsync(_pollingCts.Token);

            Started?.Invoke(this, EventArgs.Empty);
            _logger?.LogInformation("[BridgeClient] 客户端已启动");
        }
        catch (Exception ex)
        {
            await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Interlocked.Exchange(ref _isRunning, 0);
            }
            finally
            {
                _stateLock.Release();
            }
            _logger?.LogError(ex, "[BridgeClient] 启动失败");
            ErrorOccurred?.Invoke(this, new BridgeClientErrorEventArgs(ex, "启动失败"));
            throw;
        }
    }

    /// <summary>
    /// 停止 Bridge 客户端
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!IsRunning)
            {
                return;
            }
            Interlocked.Exchange(ref _isRunning, 0);
        }
        finally
        {
            _stateLock.Release();
        }

        _logger?.LogInformation("[BridgeClient] 停止客户端...");

        // 取消轮询循环
        await (_pollingCts?.CancelAsync() ?? Task.CompletedTask).ConfigureAwait(false);

        if (_pollingTask is not null)
        {
            try
            {
                await _pollingTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        // Close session if runner is available
        if (_sessionRunner != null)
        {
            var activeSessions = _sessionRunner.GetActiveSessions();
            await Task.WhenAll(activeSessions.Select(session => _sessionRunner.StopSessionAsync(session.SessionId))).ConfigureAwait(false);
            _logger?.LogInformation("[BridgeClient] Bridge 会话已关闭");
        }

        // 停止传输层
        await _transportManager.StopAsync(cancellationToken).ConfigureAwait(false);

        Stopped?.Invoke(this, EventArgs.Empty);
        _logger?.LogInformation("[BridgeClient] 客户端已停止");
    }

    /// <summary>
    /// 发送消息到服务器
    /// </summary>
    public async Task SendMessageAsync(BridgeMessage message, CancellationToken cancellationToken = default)
    {
        await _transportManager.SendMessageAsync(message, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 通过 API 客户端检查远程健康状态
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否健康，无 API 客户端时返回 false</returns>
    public async Task<bool> CheckRemoteHealthAsync(CancellationToken cancellationToken = default)
    {
        if (_apiClient == null) return false;
        try
        {
            return await _apiClient.HealthCheckAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 发送请求并等待响应
    /// </summary>
    public async Task<BridgeMessage?> SendRequestAsync(BridgeMessage request, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<BridgeMessage?>();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var timeoutCts = new CancellationTokenSource(timeout ?? _options.DefaultRequestTimeout);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, timeoutCts.Token);

        void OnMessageReceived(object? sender, BridgeMessageProcessedEventArgs e)
        {
            // 检查是否是请求的响应
            if (e.Response is ControlResponse controlResponse && controlResponse.RequestId == request.Id)
            {
                tcs.TrySetResult(e.Response);
            }
            else if (e.Response is ToolsCallResponse toolsResponse && toolsResponse.ToolCallId == request.Id)
            {
                tcs.TrySetResult(e.Response);
            }
        }

        MessageProcessed += OnMessageReceived;

        try
        {
            await SendMessageAsync(request, linkedCts.Token).ConfigureAwait(false);
            return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            MessageProcessed -= OnMessageReceived;
            cts.Dispose();
            timeoutCts.Dispose();
        }
    }

    #endregion

    #region 消息轮询循环 (pollForWork)

    /// <summary>
    /// 消息轮询循环 - 模仿 Claude Code 的 pollForWork
    /// </summary>
    private async Task RunPollingLoopAsync(CancellationToken cancellationToken)
    {
        _logger?.LogDebug("[BridgeClient] 消息轮询循环已启动");

        while (!cancellationToken.IsCancellationRequested && IsRunning)
        {
            try
            {
                // 检查连接状态
                if (!_transportManager.IsConnected)
                {
                    _logger?.LogDebug("[BridgeClient] 等待连接...");
                    var waitInterval = _pollConfigManager != null
                        ? await _pollConfigManager.CalculateNextIntervalAsync(hasError: false).ConfigureAwait(false)
                        : _options.PollingIntervalMs;
                    await Task.Delay(waitInterval, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                // 发送心跳以保持连接
                if (ShouldSendHeartbeat())
                {
                    await SendHeartbeatAsync(cancellationToken).ConfigureAwait(false);
                }

                // 等待一段时间再检查
                var pollInterval = _pollConfigManager != null
                    ? await _pollConfigManager.CalculateNextIntervalAsync(hasError: false).ConfigureAwait(false)
                    : _options.PollingIntervalMs;
                await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[BridgeClient] 轮询循环错误");
                ErrorOccurred?.Invoke(this, new BridgeClientErrorEventArgs(ex, "轮询循环错误"));

                // 通过 API 客户端检查远程健康状态
                if (_apiClient != null)
                {
                    try
                    {
                        var apiHealthy = await _apiClient.HealthCheckAsync(cancellationToken).ConfigureAwait(false);
                        _logger?.LogDebug("[BridgeClient] API 健康检查: {Status}", apiHealthy ? "正常" : "异常");
                    }
                    catch (Exception healthEx)
                    {
                        _logger?.LogDebug(healthEx, "[BridgeClient] API 健康检查失败");
                    }
                }

                // 短暂延迟后继续
                try
                {
                    var retryDelay = _pollConfigManager != null
                        ? await _pollConfigManager.CalculateNextIntervalAsync(hasError: true).ConfigureAwait(false)
                        : _options.ErrorRetryDelayMs;
                    await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        _logger?.LogDebug("[BridgeClient] 消息轮询循环已停止");
    }

    #endregion

    #region 消息处理

    /// <summary>
    /// 处理从传输层接收到的消息
    /// </summary>
    private void OnTransportMessageReceived(object? sender, BridgeMessageReceivedEventArgs e)
    {
        _ = ProcessReceivedMessageAsync(e.Message);
    }

    private async Task ProcessReceivedMessageAsync(BridgeMessage message)
    {
        Interlocked.Increment(ref _totalMessagesReceived);

        _logger?.LogDebug("[BridgeClient] 收到消息: {MessageType} (ID: {MessageId})", message.Type, message.Id);

        // 触发原始消息接收事件
        MessageReceived?.Invoke(this, new BridgeMessageReceivedEventArgs(message));

        // 处理消息
        await ProcessMessageAsync(message).ConfigureAwait(false);
    }

    /// <summary>
    /// 处理单个消息
    /// </summary>
    private async Task ProcessMessageAsync(BridgeMessage message)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // 1. 消息去重检查
            if (!await _processedMessageIds.AddAsync(message.Id).ConfigureAwait(false))
            {
                Interlocked.Increment(ref _totalDuplicatesFiltered);
                _logger?.LogDebug("[BridgeClient] 忽略重复消息: {MessageId}", message.Id);
                return;
            }

            // 2. Echo 消息过滤
            if (message is EchoMessage)
            {
                Interlocked.Increment(ref _totalEchoFiltered);
                _logger?.LogDebug("[BridgeClient] 过滤 Echo 消息: {MessageId}", message.Id);
                return;
            }

            // 3. 处理消息
            var response = await _messageHandler.HandleAsync(message).ConfigureAwait(false);

            Interlocked.Increment(ref _totalMessagesProcessed);
            stopwatch.Stop();

            // 4. 发送响应（如果有）
            if (response != null)
            {
                await SendMessageAsync(response).ConfigureAwait(false);
            }

            // 5. 触发处理完成事件
            MessageProcessed?.Invoke(this, new BridgeMessageProcessedEventArgs(
                message,
                response,
                stopwatch.ElapsedMilliseconds));

            _logger?.LogDebug("[BridgeClient] 消息处理完成: {MessageType} ({ElapsedMs}ms)",
                message.Type, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger?.LogError(ex, "[BridgeClient] 处理消息失败: {MessageType}", message.Type);
            ErrorOccurred?.Invoke(this, new BridgeClientErrorEventArgs(ex, $"处理消息失败: {message.Type}"));
        }
    }

    #endregion

    #region 心跳管理

    private DateTime _lastHeartbeatTime = DateTime.MinValue;

    private bool ShouldSendHeartbeat()
    {
        return (_clock.GetUtcNow() - _lastHeartbeatTime).TotalMilliseconds > _options.HeartbeatIntervalMs;
    }

    private async Task SendHeartbeatAsync(CancellationToken cancellationToken)
    {
        try
        {
            var ping = new PingMessage();
            await SendMessageAsync(ping, cancellationToken).ConfigureAwait(false);

            // Refresh JWT token if approaching refresh window
            if (_jwtService != null && _authToken != null)
            {
                var refreshResult = _jwtService.RefreshToken(_authToken);
                if (refreshResult.Success && refreshResult.NewToken != _authToken)
                {
                    _authToken = refreshResult.NewToken;
                    _logger?.LogDebug("[BridgeClient] JWT Token 已刷新");
                }
            }

            // Keep alive session
            if (_sessionRunner != null)
            {
                var activeSessions = _sessionRunner.GetActiveSessions();
                await Task.WhenAll(activeSessions.Select(session => _sessionRunner.KeepAliveAsync(session.SessionId))).ConfigureAwait(false);
            }

            _lastHeartbeatTime = _clock.GetUtcNow();
            _logger?.LogDebug("[BridgeClient] 心跳已发送");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[BridgeClient] 发送心跳失败");
        }
    }

    #endregion

    #region 事件处理

    private void OnConnectionStateChanged(object? sender, StateChangedEventArgs<TransportConnectionState> e)
    {
        _logger?.LogInformation("[BridgeClient] 连接状态变更: {OldState} -> {NewState}", e.OldState, e.NewState);
        ConnectionStateChanged?.Invoke(this, e);
    }

    private void OnTransportError(object? sender, TransportErrorEventArgs e)
    {
        _logger?.LogError(e.Exception, "[BridgeClient] 传输错误: {Message}", e.Message);
        ErrorOccurred?.Invoke(this, new BridgeClientErrorEventArgs(e.Exception, e.Message ?? e.Exception.Message));
    }

    private void OnReconnecting(object? sender, EventArgs e)
    {
        _logger?.LogInformation("[BridgeClient] 正在重连...");
    }

    private void OnReconnected(object? sender, EventArgs e)
    {
        _logger?.LogInformation("[BridgeClient] 重连成功");
    }

    #endregion

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1)
        {
            return;
        }

        await StopAsync().ConfigureAwait(false);
        _pollingCts?.Dispose();
        _stateLock.Dispose();
    }
}

/// <summary>
/// Bridge 客户端选项
/// </summary>
[Register]
public partial class BridgeClientOptions
{
    // 默认配置常量
    public const int DefaultPollingIntervalMs = 100;
    public const int DefaultErrorRetryDelayMs = 1000;
    public const int DefaultHeartbeatIntervalMs = 30000;
    public const int DefaultMessageDeduplicationCapacity = 1000;
    public const int DefaultRequestTimeoutSeconds = 30;

    /// <summary>轮询间隔（毫秒）</summary>
    public int PollingIntervalMs { get; init; } = DefaultPollingIntervalMs;

    /// <summary>错误重试延迟（毫秒）</summary>
    public int ErrorRetryDelayMs { get; init; } = DefaultErrorRetryDelayMs;

    /// <summary>心跳间隔（毫秒）</summary>
    public int HeartbeatIntervalMs { get; init; } = DefaultHeartbeatIntervalMs;

    /// <summary>消息去重容量</summary>
    public int MessageDeduplicationCapacity { get; init; } = DefaultMessageDeduplicationCapacity;

    /// <summary>默认请求超时</summary>
    public TimeSpan DefaultRequestTimeout { get; init; } = TimeSpan.FromSeconds(DefaultRequestTimeoutSeconds);

    /// <summary>
    /// 创建默认配置的选项实例
    /// </summary>
    public static BridgeClientOptions CreateDefault() => new();
}

/// <summary>
/// Bridge 客户端状态
/// </summary>
public partial class BridgeClientState
{
    public bool IsRunning { get; init; }
    public TransportConnectionState ConnectionState { get; init; }
    public long TotalMessagesReceived { get; init; }
    public long TotalMessagesProcessed { get; init; }
    public long TotalEchoFiltered { get; init; }
    public long TotalDuplicatesFiltered { get; init; }
    public TimeSpan Uptime { get; init; }
    public bool HasJwtToken { get; init; }
    public bool HasActiveSession { get; init; }

    public override string ToString()
    {
        return $"BridgeClientState[Running={IsRunning}, Connection={ConnectionState}, " +
               $"Received={TotalMessagesReceived}, Processed={TotalMessagesProcessed}, " +
               $"EchoFiltered={TotalEchoFiltered}, DuplicatesFiltered={TotalDuplicatesFiltered}, " +
               $"Uptime={Uptime}]";
    }
}

#region 事件参数

public partial class BridgeMessageProcessedEventArgs : EventArgs
{
    public BridgeMessage Message { get; }
    public BridgeMessage? Response { get; }
    public long ProcessingTimeMs { get; }

    public BridgeMessageProcessedEventArgs(BridgeMessage message, BridgeMessage? response, long processingTimeMs)
    {
        Message = message;
        Response = response;
        ProcessingTimeMs = processingTimeMs;
    }
}

public partial class BridgeClientErrorEventArgs : EventArgs
{
    public Exception Exception { get; }
    public string Message { get; }

    public BridgeClientErrorEventArgs(Exception exception, string message)
    {
        Exception = exception;
        Message = message;
    }
}

#endregion
