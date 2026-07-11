
namespace Core.Bridge;

/// <summary>
/// Bridge 会话状态枚举
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<BridgeSessionStatus>))]
public enum BridgeSessionStatus
{
    /// <summary>活跃状态 - 会话正在使用</summary>
    [EnumValue("active")] Active,

    /// <summary>空闲状态 - 会话无活动但未关闭</summary>
    [EnumValue("idle")] Idle,

    /// <summary>挂起状态 - 会话暂停，可恢复</summary>
    [EnumValue("suspended")] Suspended,

    /// <summary>已关闭状态 - 会话已终止</summary>
    [EnumValue("closed")] Closed
}

/// <summary>
/// 会话快照 - 用于崩溃恢复
/// </summary>
public sealed class BridgeSessionSnapshot
{
    public required string SessionId { get; init; }
    public required string ClientId { get; init; }
    public required BridgeSessionStatus State { get; init; }
    public required DateTimeOffset CapturedAt { get; init; }
}

/// <summary>
/// Bridge 会话模型 - 表示一个远程 Bridge 会话
/// 对标 Claude Code 的 Session 类型
/// </summary>
public sealed class BridgeSession
{
    /// <summary>会话唯一标识</summary>
    [JsonPropertyName("sessionId")]
    public required string SessionId { get; init; }

    /// <summary>客户端标识</summary>
    [JsonPropertyName("clientId")]
    public required string ClientId { get; init; }

    /// <summary>会话创建时间（UTC）</summary>
    [JsonPropertyName("createdAt")]
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>最后活跃时间（UTC）</summary>
    [JsonPropertyName("lastActiveAt")]
    public required DateTimeOffset LastActiveAt { get; set; }

    /// <summary>会话状态</summary>
    [JsonPropertyName("status")]
    public required BridgeSessionStatus Status { get; set; }

    /// <summary>会话元数据</summary>
    [JsonPropertyName("metadata")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? Metadata { get; init; }
}

/// <summary>
/// Bridge 会话配置
/// </summary>
[Register]
public sealed class BridgeSessionConfiguration
{
    /// <summary>会话超时时间（默认 30 分钟）</summary>
    public TimeSpan SessionTimeout { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>过期清理检查间隔（默认 5 分钟）</summary>
    public TimeSpan CleanupInterval { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>最大活跃会话数</summary>
    public int MaxActiveSessions { get; init; } = 100;

    public BridgeSessionConfiguration() { }

    public BridgeSessionConfiguration(BridgeConfig config)
    {
        SessionTimeout = TimeSpan.FromMinutes(config.SessionTimeoutMinutes);
        MaxActiveSessions = config.MaxSessions;
    }
}

/// <summary>
/// Bridge 会话工厂 - 创建新的会话实例
/// </summary>
[Register]
public sealed partial class BridgeSessionFactory
{
    private readonly TimeProvider _timeProvider;

    public BridgeSessionFactory(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// 创建新的 Bridge 会话
    /// </summary>
    /// <param name="clientId">客户端标识</param>
    /// <param name="metadata">可选元数据</param>
    /// <returns>新创建的 Bridge 会话</returns>
    public BridgeSession Create(string clientId, Dictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);

        var now = _timeProvider.GetUtcNow();
        return new BridgeSession
        {
            SessionId = Guid.NewGuid().ToString("N"),
            ClientId = clientId,
            CreatedAt = now,
            LastActiveAt = now,
            Status = BridgeSessionStatus.Active,
            Metadata = metadata
        };
    }
}

/// <summary>
/// Bridge 会话运行器 - 管理会话生命周期
/// 对标 Claude Code 的 sessionRunner.ts 和 createSession.ts
/// </summary>
[Register]
public sealed class BridgeSessionRunner : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, BridgeSession> _sessions;
    private readonly BridgeSessionFactory _sessionFactory;
    private readonly BridgeSessionConfiguration _configuration;
    private readonly ILogger? _logger;
    private readonly SemaphoreSlim _lock;
    private readonly TimeProvider _timeProvider;

    private CancellationTokenSource? _cleanupCts;
    private Task? _cleanupTask;
    private int _isDisposed;

    /// <summary>会话状态变更事件</summary>
    public event EventHandler<BridgeSessionStateChangedEventArgs>? SessionStateChanged;

    /// <summary>会话过期事件</summary>
    public event EventHandler<BridgeSessionExpiredEventArgs>? SessionExpired;

    public BridgeSessionRunner(
        BridgeSessionFactory sessionFactory,
        BridgeSessionConfiguration? configuration = null,
        ILogger? logger = null,
        TimeProvider? timeProvider = null)
    {
        _sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
        _configuration = configuration ?? new BridgeSessionConfiguration();
        _logger = logger;
        _sessions = new ConcurrentDictionary<string, BridgeSession>();
        _lock = new SemaphoreSlim(1, 1);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// 启动会话运行器（启动过期清理后台任务）
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_cleanupTask is { IsCompleted: false })
        {
            _logger?.LogWarning("[SessionRunner] 会话运行器已在运行");
            return Task.CompletedTask;
        }

        _cleanupCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _cleanupTask = RunCleanupLoopAsync(_cleanupCts.Token);
        _logger?.LogInformation("[SessionRunner] 会话运行器已启动，超时: {Timeout}", _configuration.SessionTimeout);

        return Task.CompletedTask;
    }

    /// <summary>
    /// 停止会话运行器
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _cleanupCts?.Cancel();

        if (_cleanupTask is not null)
        {
            try
            {
                await _cleanupTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 预期中的取消，忽略
            }
        }

        _cleanupCts?.Dispose();
        _cleanupCts = null;
        _cleanupTask = null;

        _logger?.LogInformation("[SessionRunner] 会话运行器已停止");
    }

    /// <summary>
    /// 创建并启动一个新会话
    /// </summary>
    /// <param name="clientId">客户端标识</param>
    /// <param name="metadata">可选元数据</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>新创建的会话</returns>
    /// <exception cref="InvalidOperationException">活跃会话数已达上限</exception>
    public async Task<BridgeSession> StartSessionAsync(
        string clientId,
        Dictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var activeCount = _sessions.Values.Count(s => s.Status == BridgeSessionStatus.Active);
            if (activeCount >= _configuration.MaxActiveSessions)
            {
                throw new InvalidOperationException(
                    $"活跃会话数已达上限 ({_configuration.MaxActiveSessions})，无法创建新会话");
            }

            var session = _sessionFactory.Create(clientId, metadata);
            _sessions[session.SessionId] = session;

            _logger?.LogInformation(
                "[SessionRunner] 会话已创建: {SessionId}, 客户端: {ClientId}",
                session.SessionId,
                clientId);

            SessionStateChanged?.Invoke(this, new BridgeSessionStateChangedEventArgs(
                session.SessionId,
                BridgeSessionStatus.Active,
                previousStatus: null));

            return session;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 优雅地停止一个会话
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <exception cref="KeyNotFoundException">会话不存在</exception>
    public async Task StopSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
            {
                throw new KeyNotFoundException($"会话不存在: {sessionId}");
            }

            if (session.Status == BridgeSessionStatus.Closed)
            {
                _logger?.LogWarning("[SessionRunner] 会话已关闭: {SessionId}", sessionId);
                return;
            }

            var previousStatus = session.Status;
            session.Status = BridgeSessionStatus.Closed;
            session.LastActiveAt = _timeProvider.GetUtcNow();

            _logger?.LogInformation("[SessionRunner] 会话已停止: {SessionId}", sessionId);

            SessionStateChanged?.Invoke(this, new BridgeSessionStateChangedEventArgs(
                sessionId,
                BridgeSessionStatus.Closed,
                previousStatus));
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 挂起一个会话（暂停但可恢复）
    /// </summary>
    public async Task SuspendSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
            {
                throw new KeyNotFoundException($"会话不存在: {sessionId}");
            }

            if (session.Status != BridgeSessionStatus.Active && session.Status != BridgeSessionStatus.Idle)
            {
                _logger?.LogWarning("[SessionRunner] 无法挂起非活跃/空闲会话: {SessionId}, 状态: {Status}", sessionId, session.Status);
                return;
            }

            var previousStatus = session.Status;
            session.Status = BridgeSessionStatus.Suspended;
            session.LastActiveAt = _timeProvider.GetUtcNow();

            _logger?.LogInformation("[SessionRunner] 会话已挂起: {SessionId}", sessionId);

            SessionStateChanged?.Invoke(this, new BridgeSessionStateChangedEventArgs(
                sessionId,
                BridgeSessionStatus.Suspended,
                previousStatus));
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 恢复一个挂起的会话
    /// </summary>
    public async Task ResumeSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
            {
                throw new KeyNotFoundException($"会话不存在: {sessionId}");
            }

            if (session.Status != BridgeSessionStatus.Suspended)
            {
                _logger?.LogWarning("[SessionRunner] 无法恢复非挂起会话: {SessionId}, 状态: {Status}", sessionId, session.Status);
                return;
            }

            var previousStatus = session.Status;
            session.Status = BridgeSessionStatus.Active;
            session.LastActiveAt = _timeProvider.GetUtcNow();

            _logger?.LogInformation("[SessionRunner] 会话已恢复: {SessionId}", sessionId);

            SessionStateChanged?.Invoke(this, new BridgeSessionStateChangedEventArgs(
                sessionId,
                BridgeSessionStatus.Active,
                previousStatus));
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 根据 ID 获取会话
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <returns>会话实例，不存在则返回 null</returns>
    public BridgeSession? GetSession(string sessionId)
    {
        return _sessions.TryGetValue(sessionId, out var session) ? session : null;
    }

    /// <summary>
    /// 创建会话快照（用于崩溃恢复）
    /// </summary>
    public BridgeSessionSnapshot? CreateSnapshot(string sessionId)
    {
        var session = GetSession(sessionId);
        if (session is null) return null;

        return new BridgeSessionSnapshot
        {
            SessionId = session.SessionId,
            ClientId = session.ClientId,
            State = session.Status,
            CapturedAt = _timeProvider.GetUtcNow()
        };
    }

    /// <summary>
    /// 从快照恢复会话状态
    /// </summary>
    public async ValueTask<BridgeSession?> RestoreFromSnapshotAsync(
        BridgeSessionSnapshot snapshot,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var session = GetSession(snapshot.SessionId);
        if (session is null) return null;

        if (session.Status == BridgeSessionStatus.Closed)
        {
            return null;
        }

        // 快照记录为活跃但当前挂起 → 恢复
        if (snapshot.State == BridgeSessionStatus.Active && session.Status == BridgeSessionStatus.Suspended)
        {
            await ResumeSessionAsync(snapshot.SessionId, ct).ConfigureAwait(false);
        }
        else if (session.Status != snapshot.State)
        {
            // 状态不一致 → 激活
            await _lock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var previousStatus = session.Status;
                session.Status = BridgeSessionStatus.Active;
                session.LastActiveAt = _timeProvider.GetUtcNow();

                SessionStateChanged?.Invoke(this, new BridgeSessionStateChangedEventArgs(
                    session.SessionId,
                    BridgeSessionStatus.Active,
                    previousStatus));
            }
            finally
            {
                _lock.Release();
            }
        }

        return session;
    }

    /// <summary>
    /// 获取所有活跃会话
    /// </summary>
    /// <returns>活跃会话列表</returns>
    public IReadOnlyList<BridgeSession> GetActiveSessions()
    {
        return _sessions.Values
            .Where(s => s.Status == BridgeSessionStatus.Active)
            .OrderByDescending(s => s.LastActiveAt)
            .ToList();
    }

    /// <summary>
    /// 刷新会话超时时间（Keep-Alive 心跳）
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>刷新是否成功</returns>
    public async Task<bool> KeepAliveAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
            {
                _logger?.LogWarning("[SessionRunner] KeepAlive 失败，会话不存在: {SessionId}", sessionId);
                return false;
            }

            if (session.Status == BridgeSessionStatus.Closed)
            {
                _logger?.LogWarning("[SessionRunner] KeepAlive 失败，会话已关闭: {SessionId}", sessionId);
                return false;
            }

            var previousStatus = session.Status;
            session.LastActiveAt = _timeProvider.GetUtcNow();
            session.Status = BridgeSessionStatus.Active;

            _logger?.LogDebug("[SessionRunner] KeepAlive 成功: {SessionId}", sessionId);

            if (previousStatus != BridgeSessionStatus.Active)
            {
                SessionStateChanged?.Invoke(this, new BridgeSessionStateChangedEventArgs(
                    sessionId,
                    BridgeSessionStatus.Active,
                    previousStatus));
            }

            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 清理过期会话
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>被清理的会话数量</returns>
    public async Task<int> CleanupExpiredSessionsAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = _timeProvider.GetUtcNow();
            var timeout = _configuration.SessionTimeout;
            var expiredSessionIds = _sessions.Values
                .Where(s => s.Status != BridgeSessionStatus.Closed
                    && now - s.LastActiveAt > timeout)
                .Select(s => s.SessionId)
                .ToList();

            foreach (var sessionId in expiredSessionIds)
            {
                if (_sessions.TryGetValue(sessionId, out var session))
                {
                    var previousStatus = session.Status;
                    session.Status = BridgeSessionStatus.Closed;
                    session.LastActiveAt = now;

                    _logger?.LogInformation(
                        "[SessionRunner] 会话已过期并清理: {SessionId}, 空闲时长: {IdleDuration}",
                        sessionId,
                        now - session.LastActiveAt);

                    SessionStateChanged?.Invoke(this, new BridgeSessionStateChangedEventArgs(
                        sessionId,
                        BridgeSessionStatus.Closed,
                        previousStatus));

                    SessionExpired?.Invoke(this, new BridgeSessionExpiredEventArgs(sessionId, session.ClientId));
                }
            }

            // 移除已关闭的会话
            var closedSessionIds = _sessions.Values
                .Where(s => s.Status == BridgeSessionStatus.Closed)
                .Select(s => s.SessionId)
                .ToList();

            foreach (var sessionId in closedSessionIds)
            {
                _sessions.TryRemove(sessionId, out _);
            }

            var totalCleaned = expiredSessionIds.Count + closedSessionIds.Count;
            if (totalCleaned > 0)
            {
                _logger?.LogInformation(
                    "[SessionRunner] 清理完成: {ExpiredCount} 个过期, {ClosedCount} 个已关闭移除",
                    expiredSessionIds.Count,
                    closedSessionIds.Count);
            }

            return totalCleaned;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 过期清理后台循环
    /// </summary>
    private async Task RunCleanupLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_configuration.CleanupInterval, _timeProvider, cancellationToken).ConfigureAwait(false);
                await CleanupExpiredSessionsAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[SessionRunner] 清理循环发生错误");
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1)
        {
            return;
        }

        await StopAsync().ConfigureAwait(false);
        _lock.Dispose();
    }
}

/// <summary>
/// 会话状态变更事件参数
/// </summary>
public sealed class BridgeSessionStateChangedEventArgs : EventArgs
{
    public string SessionId { get; }
    public BridgeSessionStatus NewStatus { get; }
    public BridgeSessionStatus? PreviousStatus { get; }

    public BridgeSessionStateChangedEventArgs(
        string sessionId,
        BridgeSessionStatus newStatus,
        BridgeSessionStatus? previousStatus)
    {
        SessionId = sessionId;
        NewStatus = newStatus;
        PreviousStatus = previousStatus;
    }
}

/// <summary>
/// 会话过期事件参数
/// </summary>
public sealed class BridgeSessionExpiredEventArgs : EventArgs
{
    public string SessionId { get; }
    public string ClientId { get; }

    public BridgeSessionExpiredEventArgs(string sessionId, string clientId)
    {
        SessionId = sessionId;
        ClientId = clientId;
    }
}
