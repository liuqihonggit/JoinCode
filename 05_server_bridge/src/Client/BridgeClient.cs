namespace Core.Bridge;

/// <summary>
/// BridgeClient Actor 命令 — Channel 中的消息类型
/// </summary>
public interface IBridgeCommand;

internal sealed record StartCmd(CancellationToken Ct, TaskCompletionSource Tcs) : IBridgeCommand;
internal sealed record StopCmd(CancellationToken Ct, TaskCompletionSource Tcs) : IBridgeCommand;
internal sealed record GetStateCmd(CancellationToken Ct, TaskCompletionSource<BridgeClientState> Tcs) : IBridgeCommand;

/// <summary>
/// Bridge 客户端 - 参考 TS 原版 的 replBridge.ts 架构
/// 实现消息轮询循环、消息去重、Echo 过滤和重连逻辑
/// Actor 化：继承 ActorBase，Start/Stop/GetState 发命令串行执行，消除 AsyncLock 锁内长 await（StopAsync >5s）。
/// </summary>
[Register(typeof(BridgeClient), ServiceLifetime.Singleton)]
public sealed partial class BridgeClient : ActorBase<IBridgeCommand, Unit>, IAsyncDisposable
{
    private readonly ITransportManager _transportManager;
    private readonly MessageHandlerCoordinator _messageHandler;
    private readonly BoundedUUIDSet _processedMessageIds;
    private readonly ILogger<BridgeClient>? _logger;
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
    private int _isDisposed;

    // 统计信息
    private long _totalMessagesReceived;
    private long _totalMessagesProcessed;
    private long _totalEchoFiltered;
    private long _totalDuplicatesFiltered;
    private DateTime _startedAt;

    public bool IsRunning => Interlocked.CompareExchange(ref _isRunning, 0, 0) != 0;

    private static TaskCompletionSource<T> CreateTcs<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static TaskCompletionSource CreateTcs() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async ValueTask<BridgeClientState> GetStateAsync(CancellationToken ct = default)
    {
        var tcs = CreateTcs<BridgeClientState>();
        await SendAsync(new GetStateCmd(ct, tcs), ct).ConfigureAwait(false);
        return await tcs.Task.ConfigureAwait(false);
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
        : base()
    {
        _transportManager = transportManager ?? throw new ArgumentNullException(nameof(transportManager));
        _messageHandler = messageHandler ?? throw new ArgumentNullException(nameof(messageHandler));
        _options = options ?? new BridgeClientOptions();
        _logger = logger;
        _clock = clock ?? SystemClockService.Instance;
        _processedMessageIds = new BoundedUUIDSet(_options.MessageDeduplicationCapacity);

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
    /// 启动 Bridge 客户端 — 发命令到 Consumer，由 Consumer 线程串行执行。
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var tcs = CreateTcs();
        await SendAsync(new StartCmd(cancellationToken, tcs), cancellationToken).ConfigureAwait(false);
        await tcs.Task.ConfigureAwait(false);
    }

    /// <summary>标记客户端为已停止（原子操作，无需锁）</summary>
    private void MarkStopped()
    {
        Interlocked.Exchange(ref _isRunning, 0);
    }

    /// <summary>
    /// 停止 Bridge 客户端 — 发命令到 Consumer，由 Consumer 线程串行执行。
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        var tcs = CreateTcs();
        await SendAsync(new StopCmd(cancellationToken, tcs), cancellationToken).ConfigureAwait(false);
        await tcs.Task.ConfigureAwait(false);
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
        using var scope = new BridgeRequestScope(this, request.Id, timeout ?? _options.DefaultRequestTimeout, cancellationToken);
        await SendMessageAsync(request, scope.Token).ConfigureAwait(false);
        return await scope.ResponseTask.WaitAsync(scope.Token).ConfigureAwait(false);
    }

    /// <summary>
    /// Bridge 请求作用域 — 封装 SendRequestAsync 的"前-后"配对(CTS×3+事件订阅)
    /// 构造时进入(创建CTS+注册消息事件),Dispose 时退出(注销事件+释放CTS)
    /// 用 using var scope = new BridgeRequestScope(...) 管理生命周期,消除散落的 try-finally 配对
    /// </summary>
    private sealed class BridgeRequestScope : IDisposable
    {
        private readonly BridgeClient _client;
        private readonly CancellationTokenSource _cts;
        private readonly CancellationTokenSource _timeoutCts;
        private readonly CancellationTokenSource _linkedCts;
        private readonly TaskCompletionSource<BridgeMessage?> _tcs;
        private readonly EventHandler<BridgeMessageProcessedEventArgs> _onMessageReceived;
        private int _disposed;

        /// <summary>链接取消令牌 — 传给 SendMessageAsync 和 WaitAsync</summary>
        public CancellationToken Token => _linkedCts.Token;

        /// <summary>响应任务 — await 此 Task 获取响应</summary>
        public Task<BridgeMessage?> ResponseTask => _tcs.Task;

        public BridgeRequestScope(BridgeClient client, string requestId, TimeSpan timeout, CancellationToken cancellationToken)
        {
            _client = client;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _timeoutCts = new CancellationTokenSource(timeout);
            _linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, _timeoutCts.Token);
            _tcs = new TaskCompletionSource<BridgeMessage?>();
            _onMessageReceived = (_, e) =>
            {
                if (e.Response is ControlResponse controlResponse && controlResponse.RequestId == requestId)
                    _tcs.TrySetResult(e.Response);
                else if (e.Response is ToolsCallResponse toolsResponse && toolsResponse.ToolCallId == requestId)
                    _tcs.TrySetResult(e.Response);
            };
            client.MessageProcessed += _onMessageReceived;
        }

        public void Dispose()
        {
            if (!DisposableHelper.TryMarkDisposed(ref _disposed)) return;
            _client.MessageProcessed -= _onMessageReceived;
            _cts.Dispose();
            _timeoutCts.Dispose();
            _linkedCts.Dispose();
        }
    }

    #endregion

    #region 消息轮询循环 (pollForWork)

    /// <summary>
    /// 消息轮询循环 - 参考 TS 原版 的 pollForWork
    /// </summary>
    private async Task RunPollingLoopAsync(CancellationToken cancellationToken)
    {
        _logger?.LogDebug("[BridgeClient] 消息轮询循环已启动");

        while (!cancellationToken.IsCancellationRequested && IsRunning)
        {
            if (await ExecutePollCycleAsync(cancellationToken).ConfigureAwait(false))
                break;
        }

        _logger?.LogDebug("[BridgeClient] 消息轮询循环已停止");
    }

    /// <summary>
    /// 执行单次轮询周期 — 返回 true 表示应退出循环(取消),false 表示继续
    /// </summary>
    private async Task<bool> ExecutePollCycleAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!_transportManager.IsConnected)
            {
                _logger?.LogDebug("[BridgeClient] 等待连接...");
                var waitInterval = _pollConfigManager != null
                    ? await _pollConfigManager.CalculateNextIntervalAsync(hasError: false).ConfigureAwait(false)
                    : _options.PollingIntervalMs;
                await Task.Delay(waitInterval, cancellationToken).ConfigureAwait(false);
                return false;
            }

            if (ShouldSendHeartbeat())
            {
                await SendHeartbeatAsync(cancellationToken).ConfigureAwait(false);
            }

            var pollInterval = _pollConfigManager != null
                ? await _pollConfigManager.CalculateNextIntervalAsync(hasError: false).ConfigureAwait(false)
                : _options.PollingIntervalMs;
            await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);
            return false;
        }
        catch (OperationCanceledException)
        {
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[BridgeClient] 轮询循环错误");
            ErrorOccurred?.Invoke(this, new BridgeClientErrorEventArgs(ex, "轮询循环错误"));

            await CheckApiHealthSafelyAsync(cancellationToken).ConfigureAwait(false);

            return await WaitForRetryOrCancelAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 安全执行 API 健康检查 — 失败仅记录调试日志
    /// </summary>
    private async Task CheckApiHealthSafelyAsync(CancellationToken cancellationToken)
    {
        if (_apiClient is null) return;
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

    /// <summary>
    /// 等待重试延迟 — 取消时返回 true(应退出),否则 false(继续)
    /// </summary>
    private async Task<bool> WaitForRetryOrCancelAsync(CancellationToken cancellationToken)
    {
        try
        {
            var retryDelay = _pollConfigManager != null
                ? await _pollConfigManager.CalculateNextIntervalAsync(hasError: true).ConfigureAwait(false)
                : _options.ErrorRetryDelayMs;
            await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
            return false;
        }
        catch (OperationCanceledException)
        {
            return true;
        }
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

    /// <summary>
    /// Actor Consumer — 线程独占 _pollingCts/_pollingTask/_authToken/_startedAt，串行处理命令，无需锁。
    /// </summary>
    protected override async ValueTask HandleAsync(IBridgeCommand command, CancellationToken ct)
    {
        switch (command)
        {
            case StartCmd cmd:
            {
                if (IsRunning)
                {
                    _logger?.LogWarning("[BridgeClient] 客户端已在运行");
                    cmd.Tcs.TrySetResult();
                    break;
                }

                try
                {
                    _logger?.LogInformation("[BridgeClient] 启动客户端...");

                    Interlocked.Exchange(ref _isRunning, 1);
                    _startedAt = _clock.GetUtcNow();

                    await _transportManager.StartAsync(cmd.Ct).ConfigureAwait(false);

                    if (_jwtService != null)
                    {
                        _authToken = _jwtService.GenerateToken("bridge-client", _options.HeartbeatIntervalMs / 1000 * 300);
                        _logger?.LogInformation("[BridgeClient] JWT Token 已生成");
                    }

                    if (_sessionRunner != null)
                    {
                        await _sessionRunner.StartSessionAsync("bridge-client", new Dictionary<string, string> { ["transport"] = "websocket" }).ConfigureAwait(false);
                        _logger?.LogInformation("[BridgeClient] Bridge 会话已创建");
                    }

                    _pollingCts = CancellationTokenSource.CreateLinkedTokenSource(cmd.Ct);
                    _pollingTask = RunPollingLoopAsync(_pollingCts.Token);

                    Started?.Invoke(this, EventArgs.Empty);
                    _logger?.LogInformation("[BridgeClient] 客户端已启动");
                    cmd.Tcs.TrySetResult();
                }
                catch (Exception ex)
                {
                    MarkStopped();
                    _logger?.LogError(ex, "[BridgeClient] 启动失败");
                    ErrorOccurred?.Invoke(this, new BridgeClientErrorEventArgs(ex, "启动失败"));
                    cmd.Tcs.TrySetException(ex);
                }

                break;
            }

            case StopCmd cmd:
            {
                if (!IsRunning)
                {
                    cmd.Tcs.TrySetResult();
                    break;
                }
                Interlocked.Exchange(ref _isRunning, 0);

                _logger?.LogInformation("[BridgeClient] 停止客户端...");

                await (_pollingCts?.CancelAsync() ?? Task.CompletedTask).ConfigureAwait(false);

                if (_pollingTask is not null)
                {
                    try
                    {
                        await _pollingTask.WaitAsync(cmd.Ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                }

                if (_sessionRunner != null)
                {
                    var activeSessions = _sessionRunner.GetActiveSessions();
                    await Task.WhenAll(activeSessions.Select(session => _sessionRunner.StopSessionAsync(session.SessionId))).ConfigureAwait(false);
                    _logger?.LogInformation("[BridgeClient] Bridge 会话已关闭");
                }

                await _transportManager.StopAsync(cmd.Ct).ConfigureAwait(false);

                Stopped?.Invoke(this, EventArgs.Empty);
                _logger?.LogInformation("[BridgeClient] 客户端已停止");
                cmd.Tcs.TrySetResult();
                break;
            }

            case GetStateCmd cmd:
            {
                var state = new BridgeClientState
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
                cmd.Tcs.TrySetResult(state);
                break;
            }
        }
    }

    protected override void OnConsumerError(Exception ex)
    {
        _logger?.LogError(ex, "[BridgeClient] Actor Consumer 异常");
    }

    public async override ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1)
        {
            return;
        }

        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[BridgeClient] Dispose 时停止异常");
        }

        _pollingCts?.Dispose();
        await base.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>
/// Bridge 客户端选项
/// </summary>
[Register(typeof(BridgeClientOptions), ServiceLifetime.Singleton)]
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
