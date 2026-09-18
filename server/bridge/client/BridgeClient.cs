using System.Threading;
namespace Core.Bridge;

/// <summary>
/// BridgeClient Actor 命令 — Channel 中的消息类型
/// </summary>
public interface IBridgeCommand;

internal sealed record StartCmd(CancellationToken Ct, TaskCompletionSource Tcs) : IBridgeCommand;
internal sealed record StopCmd(CancellationToken Ct, TaskCompletionSource Tcs) : IBridgeCommand;
internal sealed record GetStateCmd(CancellationToken Ct, TaskCompletionSource<BridgeClientState> Tcs) : IBridgeCommand;

/// <summary>
/// Bridge 客户端消息统计 — 5 个字段的聚合,用 <see cref="Interlocked"/> 保持线程安全
/// </summary>
/// <remarks>
/// 4 个 long 计数器在传输层事件回调线程递增,用 <see cref="Interlocked"/> 原子操作;
/// <see cref="StartedAt"/> 仅在 Actor Consumer 线程读写,无需原子。
/// </remarks>
internal struct BridgeClientStats
{
    /// <summary>累计接收消息数</summary>
    public long Received;
    /// <summary>累计处理消息数</summary>
    public long Processed;
    /// <summary>累计过滤 Echo 消息数</summary>
    public long EchoFiltered;
    /// <summary>累计过滤重复消息数</summary>
    public long DuplicatesFiltered;
    /// <summary>启动时间(Actor Consumer 线程独占)</summary>
    public DateTime StartedAt;

    /// <summary>记录接收到一条消息(线程安全)</summary>
    public void RecordReceived() => Interlocked.Increment(ref Received);
    /// <summary>记录处理完成一条消息(线程安全)</summary>
    public void RecordProcessed() => Interlocked.Increment(ref Processed);
    /// <summary>记录过滤一条 Echo 消息(线程安全)</summary>
    public void RecordEchoFiltered() => Interlocked.Increment(ref EchoFiltered);
    /// <summary>记录过滤一条重复消息(线程安全)</summary>
    public void RecordDuplicatesFiltered() => Interlocked.Increment(ref DuplicatesFiltered);
    /// <summary>设置启动时间(Actor Consumer 线程独占,无需原子)</summary>
    public void MarkStarted(DateTime now) => StartedAt = now;
}

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

    // 统计信息(5 字段聚合为 BridgeClientStats,用 Interlocked 保持线程安全)
    private BridgeClientStats _stats;

    /// <summary>客户端是否正在运行（原子读取）</summary>
    public bool IsRunning => Interlocked.CompareExchange(ref _isRunning, 0, 0) != 0;


    /// <summary>
    /// 获取客户端当前状态快照（发命令到 Actor Consumer 串行执行）
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>客户端状态快照</returns>
    public async ValueTask<BridgeClientState> GetStateAsync(CancellationToken ct = default)
    {
        var tcs = TcsFactory.Create<BridgeClientState>();
        await SendAsync(new GetStateCmd(ct, tcs), ct).ConfigureAwait(false);
        return await AskAwait(tcs, ct);
    }

    /// <summary>接收到原始消息事件（去重/Echo 过滤前）</summary>
    public event EventHandler<BridgeMessageReceivedEventArgs>? MessageReceived;
    /// <summary>消息处理完成事件</summary>
    public event EventHandler<BridgeMessageProcessedEventArgs>? MessageProcessed;
    /// <summary>客户端错误事件</summary>
    public event EventHandler<BridgeClientErrorEventArgs>? ErrorOccurred;
    /// <summary>连接状态变更事件</summary>
    public event EventHandler<StateChangedEventArgs<TransportConnectionState>>? ConnectionStateChanged;
    /// <summary>客户端已启动事件</summary>
    public event EventHandler? Started;
    /// <summary>客户端已停止事件</summary>
    public event EventHandler? Stopped;

    /// <summary>
    /// 构造 BridgeClient — 订阅传输层事件，初始化去重集合与可选依赖
    /// </summary>
    /// <param name="transportManager">传输管理器</param>
    /// <param name="messageHandler">消息处理协调器</param>
    /// <param name="clientSession">可选客户端会话聚合（提供 JWT/轮询/会话/API 依赖）</param>
    /// <param name="options">可选客户端选项，默认使用默认配置</param>
    /// <param name="logger">可选日志记录器</param>
    /// <param name="clock">可选时钟服务，默认系统时钟</param>
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
    public async Task StartAsync(CancellationToken ct = default)
    {
        var tcs = TcsFactory.Create();
        await SendAsync(new StartCmd(ct, tcs), ct).ConfigureAwait(false);
        await AskAwait(tcs, ct);
    }

    /// <summary>标记客户端为已停止（原子操作，无需锁）</summary>
    private void MarkStopped()
    {
        Interlocked.Exchange(ref _isRunning, 0);
    }

    /// <summary>
    /// 停止 Bridge 客户端 — 发命令到 Consumer，由 Consumer 线程串行执行。
    /// </summary>
    public async Task StopAsync(CancellationToken ct = default)
    {
        var tcs = TcsFactory.Create();
        await SendAsync(new StopCmd(ct, tcs), ct).ConfigureAwait(false);
        await AskAwait(tcs, ct);
    }

    /// <summary>
    /// 发送消息到服务器
    /// </summary>
    public async Task SendMessageAsync(BridgeMessage message, CancellationToken ct = default)
    {
        await _transportManager.SendMessageAsync(message, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 通过 API 客户端检查远程健康状态
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>是否健康，无 API 客户端时返回 false</returns>
    public async Task<bool> CheckRemoteHealthAsync(CancellationToken ct = default)
    {
        if (_apiClient == null) return false;
        try
        {
            return await _apiClient.HealthCheckAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 发送请求并等待响应
    /// </summary>
    public async Task<BridgeMessage?> SendRequestAsync(BridgeMessage request, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        using var scope = new BridgeRequestScope(this, request.Id, timeout ?? _options.DefaultRequestTimeout, ct);
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

        /// <summary>
        /// 构造 BridgeRequestScope — 创建链接取消令牌、注册消息事件订阅
        /// </summary>
        /// <param name="client">所属 BridgeClient</param>
        /// <param name="requestId">请求标识，用于匹配响应</param>
        /// <param name="timeout">请求超时</param>
        /// <param name="ct">外部取消令牌</param>
        public BridgeRequestScope(BridgeClient client, string requestId, TimeSpan timeout, CancellationToken ct)
        {
            _client = client;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
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

        /// <summary>
        /// 释放作用域 — 注销事件订阅并释放全部取消令牌
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
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
    private async Task RunPollingLoopAsync(CancellationToken ct)
    {
        _logger?.LogDebug("[BridgeClient] 消息轮询循环已启动");

        while (!ct.IsCancellationRequested && IsRunning)
        {
            if (await ExecutePollCycleAsync(ct).ConfigureAwait(false))
                break;
        }

        _logger?.LogDebug("[BridgeClient] 消息轮询循环已停止");
    }

    /// <summary>
    /// 执行单次轮询周期 — 返回 true 表示应退出循环(取消),false 表示继续
    /// </summary>
    private async Task<bool> ExecutePollCycleAsync(CancellationToken ct)
    {
        try
        {
            if (!_transportManager.IsConnected)
            {
                _logger?.LogDebug("[BridgeClient] 等待连接...");
                var waitInterval = _pollConfigManager != null
                    ? await _pollConfigManager.CalculateNextIntervalAsync(hasError: false).ConfigureAwait(false)
                    : _options.PollingIntervalMs;
                await Task.Delay(waitInterval, ct).ConfigureAwait(false);
                return false;
            }

            if (ShouldSendHeartbeat())
            {
                await SendHeartbeatAsync(ct).ConfigureAwait(false);
            }

            var pollInterval = _pollConfigManager != null
                ? await _pollConfigManager.CalculateNextIntervalAsync(hasError: false).ConfigureAwait(false)
                : _options.PollingIntervalMs;
            await Task.Delay(pollInterval, ct).ConfigureAwait(false);
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

            await CheckApiHealthSafelyAsync(ct).ConfigureAwait(false);

            return await WaitForRetryOrCancelAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 安全执行 API 健康检查 — 失败仅记录调试日志
    /// </summary>
    private async Task CheckApiHealthSafelyAsync(CancellationToken ct)
    {
        if (_apiClient is null) return;
        try
        {
            var apiHealthy = await _apiClient.HealthCheckAsync(ct).ConfigureAwait(false);
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
    private async Task<bool> WaitForRetryOrCancelAsync(CancellationToken ct)
    {
        try
        {
            var retryDelay = _pollConfigManager != null
                ? await _pollConfigManager.CalculateNextIntervalAsync(hasError: true).ConfigureAwait(false)
                : _options.ErrorRetryDelayMs;
            await Task.Delay(retryDelay, ct).ConfigureAwait(false);
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
        _stats.RecordReceived();

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
                _stats.RecordDuplicatesFiltered();
                _logger?.LogDebug("[BridgeClient] 忽略重复消息: {MessageId}", message.Id);
                return;
            }

            // 2. Echo 消息过滤
            if (message is EchoMessage)
            {
                _stats.RecordEchoFiltered();
                _logger?.LogDebug("[BridgeClient] 过滤 Echo 消息: {MessageId}", message.Id);
                return;
            }

            // 3. 处理消息
            var response = await _messageHandler.HandleAsync(message).ConfigureAwait(false);

            _stats.RecordProcessed();
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

    private async Task SendHeartbeatAsync(CancellationToken ct)
    {
        try
        {
            var ping = new PingMessage();
            await SendMessageAsync(ping, ct).ConfigureAwait(false);

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
    /// Actor Consumer — 线程独占 _pollingCts/_pollingTask/_authToken/_stats.StartedAt，串行处理命令，无需锁。
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
                    _stats.MarkStarted(_clock.GetUtcNow());

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
                    TotalMessagesReceived = _stats.Received,
                    TotalMessagesProcessed = _stats.Processed,
                    TotalEchoFiltered = _stats.EchoFiltered,
                    TotalDuplicatesFiltered = _stats.DuplicatesFiltered,
                    Uptime = _clock.GetUtcNow() - _stats.StartedAt,
                    HasJwtToken = _authToken != null,
                    HasActiveSession = _sessionRunner?.GetActiveSessions().Count > 0,
                };
                cmd.Tcs.TrySetResult(state);
                break;
            }
        }
    }

    /// <summary>
    /// Actor Consumer 异常回调 — 记录日志
    /// </summary>
    /// <param name="ex">异常</param>
    protected override void OnConsumerError(Exception ex)
    {
        _logger?.LogError(ex, "[BridgeClient] Actor Consumer 异常");
    }

    /// <summary>
    /// 异步释放资源 — 停止客户端、释放轮询令牌并调用基类释放
    /// </summary>
    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        try
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
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
    /// <summary>默认轮询间隔（毫秒）</summary>
    public const int DefaultPollingIntervalMs = 100;
    /// <summary>默认错误重试延迟（毫秒）</summary>
    public const int DefaultErrorRetryDelayMs = 1000;
    /// <summary>默认心跳间隔（毫秒）</summary>
    public const int DefaultHeartbeatIntervalMs = 30000;
    /// <summary>默认消息去重容量</summary>
    public const int DefaultMessageDeduplicationCapacity = 1000;
    /// <summary>默认请求超时（秒）</summary>
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
    /// <summary>是否正在运行</summary>
    public bool IsRunning { get; init; }
    /// <summary>传输连接状态</summary>
    public TransportConnectionState ConnectionState { get; init; }
    /// <summary>累计接收消息数</summary>
    public long TotalMessagesReceived { get; init; }
    /// <summary>累计处理消息数</summary>
    public long TotalMessagesProcessed { get; init; }
    /// <summary>累计过滤 Echo 消息数</summary>
    public long TotalEchoFiltered { get; init; }
    /// <summary>累计过滤重复消息数</summary>
    public long TotalDuplicatesFiltered { get; init; }
    /// <summary>运行时长</summary>
    public TimeSpan Uptime { get; init; }
    /// <summary>是否持有 JWT Token</summary>
    public bool HasJwtToken { get; init; }
    /// <summary>是否有活跃会话</summary>
    public bool HasActiveSession { get; init; }

    /// <summary>
    /// 返回状态摘要字符串
    /// </summary>
    /// <returns>状态摘要</returns>
    public override string ToString()
    {
        return $"BridgeClientState[Running={IsRunning}, Connection={ConnectionState}, " +
               $"Received={TotalMessagesReceived}, Processed={TotalMessagesProcessed}, " +
               $"EchoFiltered={TotalEchoFiltered}, DuplicatesFiltered={TotalDuplicatesFiltered}, " +
               $"Uptime={Uptime}]";
    }
}

#region 事件参数

/// <summary>
/// 消息处理完成事件参数
/// </summary>
public partial class BridgeMessageProcessedEventArgs : EventArgs
{
    /// <summary>原始消息</summary>
    public BridgeMessage Message { get; }
    /// <summary>响应消息，无响应为 null</summary>
    public BridgeMessage? Response { get; }
    /// <summary>处理耗时（毫秒）</summary>
    public long ProcessingTimeMs { get; }

    /// <summary>
    /// 构造消息处理完成事件参数
    /// </summary>
    /// <param name="message">原始消息</param>
    /// <param name="response">响应消息，无响应为 null</param>
    /// <param name="processingTimeMs">处理耗时（毫秒）</param>
    public BridgeMessageProcessedEventArgs(BridgeMessage message, BridgeMessage? response, long processingTimeMs)
    {
        Message = message;
        Response = response;
        ProcessingTimeMs = processingTimeMs;
    }
}

/// <summary>
/// 客户端错误事件参数
/// </summary>
public partial class BridgeClientErrorEventArgs : EventArgs
{
    /// <summary>异常对象</summary>
    public Exception Exception { get; }
    /// <summary>错误消息</summary>
    public string Message { get; }

    /// <summary>
    /// 构造客户端错误事件参数
    /// </summary>
    /// <param name="exception">异常对象</param>
    /// <param name="message">错误消息</param>
    public BridgeClientErrorEventArgs(Exception exception, string message)
    {
        Exception = exception;
        Message = message;
    }
}

#endregion
