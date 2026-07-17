
namespace Core.Bridge;

/// <summary>
/// 对等会话状态枚举
/// </summary>
public enum PeerSessionStatus
{
    /// <summary>正在连接</summary>
    [EnumValue("connecting")] Connecting,
    /// <summary>已连接</summary>
    [EnumValue("connected")] Connected,
    /// <summary>已断开</summary>
    [EnumValue("disconnected")] Disconnected
}

/// <summary>
/// 对等会话 - 表示两个 Bridge 节点之间的 P2P 连接
/// </summary>
public sealed partial class PeerSession
{
    [JsonPropertyName("sessionId")]
    public required string SessionId { get; init; }

    [JsonPropertyName("localPeerId")]
    public required string LocalPeerId { get; init; }

    [JsonPropertyName("remotePeerId")]
    public required string RemotePeerId { get; init; }

    [JsonPropertyName("status")]
    public PeerSessionStatus Status { get; set; } = PeerSessionStatus.Connecting;

    [JsonPropertyName("createdAt")]
    public required long CreatedAt { get; init; }
}

/// <summary>
/// 对等会话管理器 - 管理 Bridge 节点间的 P2P 会话
/// </summary>
[Register]
public sealed partial class PeerSessionManager : IAsyncDisposable
{
    [Inject] private readonly ILogger<PeerSessionManager>? _logger;
    private readonly ConcurrentDictionary<string, PeerSession> _sessions;
    private readonly AsyncLock _stateLock = new();
    private int _isDisposed;

    public event EventHandler<PeerSessionEventArgs>? PeerSessionConnected;
    public event EventHandler<PeerSessionEventArgs>? PeerSessionDisconnected;
    public event EventHandler<PeerMessageEventArgs>? PeerMessageSent;

    public PeerSessionManager(ILogger<PeerSessionManager>? logger = null)
    {
        _logger = logger;
        _sessions = new ConcurrentDictionary<string, PeerSession>();
    }

    /// <summary>
    /// 创建对等会话
    /// </summary>
    /// <param name="localPeerId">本地节点 ID</param>
    /// <param name="remotePeerId">远程节点 ID</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>创建的对等会话</returns>
    public async Task<PeerSession> CreatePeerSessionAsync(
        string localPeerId,
        string remotePeerId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(localPeerId);
        ArgumentNullException.ThrowIfNull(remotePeerId);

                using (await _stateLock.LockAsync(ct).ConfigureAwait(false))
        {
            ObjectDisposedException.ThrowIf(_isDisposed != 0, this);

            var session = new PeerSession
            {
                SessionId = Guid.NewGuid().ToString("N"),
                LocalPeerId = localPeerId,
                RemotePeerId = remotePeerId,
                Status = PeerSessionStatus.Connecting,
                CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            _sessions[session.SessionId] = session;

            _logger?.LogInformation(
                "[PeerSessionManager] 创建对等会话: {SessionId}, 本地: {LocalPeerId}, 远程: {RemotePeerId}",
                session.SessionId, localPeerId, remotePeerId);

            return session;
        }
    }

    /// <summary>
    /// 关闭对等会话
    /// </summary>
    /// <param name="sessionId">会话 ID</param>
    /// <param name="ct">取消令牌</param>
    public async Task ClosePeerSessionAsync(string sessionId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sessionId);

                using (await _stateLock.LockAsync(ct).ConfigureAwait(false))
        {
            if (_sessions.TryRemove(sessionId, out var session))
            {
                session.Status = PeerSessionStatus.Disconnected;

                _logger?.LogInformation(
                    "[PeerSessionManager] 关闭对等会话: {SessionId}", sessionId);

                PeerSessionDisconnected?.Invoke(this, new PeerSessionEventArgs(session));
            }
        }
    }

    /// <summary>
    /// 获取对等会话
    /// </summary>
    /// <param name="sessionId">会话 ID</param>
    /// <returns>对等会话，不存在则返回 null</returns>
    public PeerSession? GetPeerSession(string sessionId)
    {
        _sessions.TryGetValue(sessionId, out var session);
        return session;
    }

    /// <summary>
    /// 获取所有活跃的对等会话
    /// </summary>
    /// <returns>活跃会话列表</returns>
    public IReadOnlyList<PeerSession> GetActivePeerSessions()
    {
        return _sessions.Values
            .Where(s => s.Status == PeerSessionStatus.Connected)
            .ToList();
    }

    /// <summary>
    /// 标记会话为已连接状态
    /// </summary>
    /// <param name="sessionId">会话 ID</param>
    /// <param name="ct">取消令牌</param>
    public async Task MarkConnectedAsync(string sessionId, CancellationToken ct = default)
    {
                using (await _stateLock.LockAsync(ct).ConfigureAwait(false))
        {
            if (_sessions.TryGetValue(sessionId, out var session))
            {
                session.Status = PeerSessionStatus.Connected;

                _logger?.LogInformation(
                    "[PeerSessionManager] 对等会话已连接: {SessionId}", sessionId);

                PeerSessionConnected?.Invoke(this, new PeerSessionEventArgs(session));
            }
        }
    }

    /// <summary>
    /// 向对等节点发送消息
    /// </summary>
    /// <param name="sessionId">会话 ID</param>
    /// <param name="message">Bridge 消息</param>
    /// <param name="ct">取消令牌</param>
    public async Task SendMessageToPeerAsync(
        string sessionId,
        BridgeMessage message,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            _logger?.LogWarning("[PeerSessionManager] 会话不存在: {SessionId}", sessionId);
            return;
        }

        if (session.Status != PeerSessionStatus.Connected)
        {
            _logger?.LogWarning("[PeerSessionManager] 会话未连接: {SessionId}, 状态: {Status}", sessionId, session.Status);
            return;
        }

        _logger?.LogDebug(
            "[PeerSessionManager] 向对等节点发送消息: {SessionId}, 类型: {MessageType}",
            sessionId, message.Type);

        // 触发消息发送事件，由 BridgeServer 订阅并转发到 WebSocket 客户端
        PeerMessageSent?.Invoke(this, new PeerMessageEventArgs(sessionId, message));

        await Task.CompletedTask.ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1)
        {
            return;
        }

        foreach (var session in _sessions.Values)
        {
            session.Status = PeerSessionStatus.Disconnected;
        }

        _sessions.Clear();
        _stateLock.Dispose();

        _logger?.LogInformation("[PeerSessionManager] 已释放所有对等会话");
    }
}

/// <summary>
/// 对等会话事件参数
/// </summary>
public sealed partial class PeerSessionEventArgs : EventArgs
{
    public PeerSession Session { get; }

    public PeerSessionEventArgs(PeerSession session)
    {
        Session = session;
    }
}

/// <summary>
/// 对等消息事件参数 - P2P 消息发送时触发
/// </summary>
public sealed partial class PeerMessageEventArgs : EventArgs
{
    /// <summary>会话 ID</summary>
    public string SessionId { get; }

    /// <summary>发送的 Bridge 消息</summary>
    public BridgeMessage Message { get; }

    public PeerMessageEventArgs(string sessionId, BridgeMessage message)
    {
        SessionId = sessionId;
        Message = message;
    }
}

/// <summary>
/// 对等节点路由表 - 管理节点 ID 到端点的映射
/// </summary>
public sealed partial class PeerSessionRouter
{
    private readonly ConcurrentDictionary<string, string> _routes = new(StringComparer.Ordinal);

    public int RouteCount => _routes.Count;

    public void RegisterRoute(string peerId, string endpoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(peerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        _routes[peerId] = endpoint;
    }

    public void UnregisterRoute(string peerId)
    {
        _routes.TryRemove(peerId, out _);
    }

    public string? GetRoute(string peerId)
    {
        _routes.TryGetValue(peerId, out var endpoint);
        return endpoint;
    }

    public bool HasRoute(string peerId) => _routes.ContainsKey(peerId);

    public IReadOnlyList<string> GetAllPeerIds() => _routes.Keys.ToList();

    public void Clear() => _routes.Clear();
}
