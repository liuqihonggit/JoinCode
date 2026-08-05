namespace Core.Bridge;

/// <summary>
/// Bridge 会话状态跟踪器 — 封装 BridgeMain 中 7 个 Dictionary + 3 个 HashSet 的状态管理
/// 对齐 TS 端 runBridgeLoop 中的 Map/Set 集合
/// 所有集合使用并发安全类型，支持多连接并发访问
/// </summary>
public sealed class BridgeSessionTracker
{
    private readonly ConcurrentDictionary<string, BridgeSubprocessHandle> _activeSessions = new();
    private readonly ConcurrentDictionary<string, DateTime> _sessionStartTimes = new();
    private readonly ConcurrentDictionary<string, string> _sessionWorkIds = new();
    private readonly ConcurrentDictionary<string, string> _sessionIngressTokens = new();
    private readonly ConcurrentDictionary<string, string> _sessionWorktrees = new();
    private readonly ConcurrentDictionary<string, byte> _completedWorkIds = new();
    private readonly ConcurrentDictionary<string, byte> _timedOutSessions = new();
    private readonly ConcurrentDictionary<string, byte> _v2Sessions = new();
    private readonly ConcurrentDictionary<string, byte> _titledSessions = new();
    private readonly ConcurrentDictionary<string, string> _sessionCompatIds = new();

    /// <summary>当前活跃会话数</summary>
    public int ActiveSessionCount => _activeSessions.Count;

    /// <summary>是否已完成指定工作项</summary>
    public bool IsWorkCompleted(string workId) => _completedWorkIds.ContainsKey(workId);

    /// <summary>是否已有指定会话</summary>
    public bool HasSession(string sessionId) => _activeSessions.ContainsKey(sessionId);

    /// <summary>获取兼容 ID — 对齐 TS 端 sessionCompatIds.get(sessionId) ?? sessionId</summary>
    public string GetCompatId(string sessionId)
        => _sessionCompatIds.TryGetValue(sessionId, out var compatId) ? compatId : sessionId;

    /// <summary>获取活跃会话句柄</summary>
    public BridgeSubprocessHandle? GetSession(string sessionId)
        => _activeSessions.TryGetValue(sessionId, out var handle) ? handle : null;

    /// <summary>获取会话 ingress token</summary>
    public string? GetIngressToken(string sessionId)
        => _sessionIngressTokens.TryGetValue(sessionId, out var token) ? token : null;

    /// <summary>获取会话工作目录</summary>
    public bool TryGetWorktree(string sessionId, out string? worktreePath)
        => _sessionWorktrees.TryGetValue(sessionId, out worktreePath);

    /// <summary>是否为 V2 会话</summary>
    public bool IsV2Session(string sessionId) => _v2Sessions.ContainsKey(sessionId);

    /// <summary>是否已获取标题</summary>
    public bool HasTitle(string compatId) => _titledSessions.ContainsKey(compatId);

    /// <summary>标记已获取标题</summary>
    public void MarkTitled(string compatId) => _titledSessions.TryAdd(compatId, 0);

    /// <summary>注册新会话</summary>
    public void RegisterSession(string sessionId, BridgeSubprocessHandle handle, string workId,
        string? ingressToken = null, string? worktreePath = null, string? compatId = null,
        bool isV2 = false)
    {
        _activeSessions[sessionId] = handle;
        _sessionStartTimes[sessionId] = DateTime.UtcNow;
        _sessionWorkIds[sessionId] = workId;

        if (ingressToken is not null)
            _sessionIngressTokens[sessionId] = ingressToken;

        if (worktreePath is not null)
            _sessionWorktrees[sessionId] = worktreePath;

        if (compatId is not null)
            _sessionCompatIds[sessionId] = compatId;

        if (isV2)
            _v2Sessions.TryAdd(sessionId, 0);
    }

    /// <summary>标记工作项已完成</summary>
    public void MarkWorkCompleted(string workId) => _completedWorkIds.TryAdd(workId, 0);

    /// <summary>标记会话已超时</summary>
    public void MarkTimedOut(string sessionId) => _timedOutSessions.TryAdd(sessionId, 0);

    /// <summary>检查并移除超时标记 — 返回是否曾被标记为超时</summary>
    public bool RemoveTimedOut(string sessionId) => _timedOutSessions.TryRemove(sessionId, out _);

    /// <summary>更新会话 ingress token</summary>
    public void UpdateIngressToken(string sessionId, string token)
        => _sessionIngressTokens[sessionId] = token;

    /// <summary>更新会话句柄的 access token</summary>
    public async Task UpdateSessionAccessTokenAsync(string sessionId, string token, CancellationToken ct)
    {
        if (_activeSessions.TryGetValue(sessionId, out var handle))
        {
            await handle.UpdateAccessTokenAsync(token, ct).ConfigureAwait(false);
        }
    }

    /// <summary>获取会话持续时间（毫秒）</summary>
    public long GetSessionDurationMs(string sessionId, IClockService clock)
        => _sessionStartTimes.TryGetValue(sessionId, out var startTime)
            ? (long)(clock.GetUtcNow() - startTime).TotalMilliseconds
            : 0L;

    /// <summary>获取所有活跃会话句柄（遍历器，返回快照避免枚举异常）</summary>
    public IEnumerable<BridgeSubprocessHandle> GetAllHandles() => _activeSessions.Values;

    /// <summary>获取所有会话 ID（遍历器，返回快照避免枚举异常）</summary>
    public IEnumerable<string> GetAllSessionIds() => _activeSessions.Keys;

    /// <summary>获取所有工作 ID（遍历器，返回快照避免枚举异常）</summary>
    public IEnumerable<string> GetAllWorkIds() => _sessionWorkIds.Values;

    /// <summary>获取最近注册的会话（按注册时间最新）</summary>
    public KeyValuePair<string, BridgeSubprocessHandle>? GetLastSession()
    {
        if (_activeSessions.Count == 0) return null;
        var latestSessionId = string.Empty;
        var latestTime = DateTime.MinValue;
        foreach (var (sessionId, startTime) in _sessionStartTimes)
        {
            if (startTime > latestTime)
            {
                latestTime = startTime;
                latestSessionId = sessionId;
            }
        }
        return _activeSessions.TryGetValue(latestSessionId, out var handle)
            ? new KeyValuePair<string, BridgeSubprocessHandle>(latestSessionId, handle)
            : null;
    }

    /// <summary>清理单个会话的跟踪状态</summary>
    public void CleanupSession(string sessionId, Action<string>? onRemoveCompatId = null)
    {
        _activeSessions.TryRemove(sessionId, out _);
        _sessionStartTimes.TryRemove(sessionId, out _);
        _sessionWorkIds.TryRemove(sessionId, out _);
        _sessionIngressTokens.TryRemove(sessionId, out _);
        _timedOutSessions.TryRemove(sessionId, out _);
        _v2Sessions.TryRemove(sessionId, out _);

        if (_sessionCompatIds.TryGetValue(sessionId, out var compatId))
        {
            _titledSessions.TryRemove(compatId, out _);
            onRemoveCompatId?.Invoke(compatId);
        }
        _sessionCompatIds.TryRemove(sessionId, out _);
    }

    /// <summary>移除 worktree 记录并返回路径</summary>
    public bool RemoveWorktree(string sessionId, out string? worktreePath)
        => _sessionWorktrees.TryRemove(sessionId, out worktreePath);

    /// <summary>清理所有跟踪状态</summary>
    public void ClearAll()
    {
        _activeSessions.Clear();
        _sessionStartTimes.Clear();
        _sessionWorkIds.Clear();
        _sessionIngressTokens.Clear();
        _sessionWorktrees.Clear();
        _completedWorkIds.Clear();
        _timedOutSessions.Clear();
        _v2Sessions.Clear();
        _titledSessions.Clear();
        _sessionCompatIds.Clear();
    }

    /// <summary>暴露给 HandleWorkContext 的中间件兼容属性</summary>
    internal ConcurrentDictionary<string, BridgeSubprocessHandle> ActiveSessions => _activeSessions;
    internal ConcurrentDictionary<string, DateTime> SessionStartTimes => _sessionStartTimes;
    internal ConcurrentDictionary<string, string> SessionWorkIds => _sessionWorkIds;
    internal ConcurrentDictionary<string, string> SessionIngressTokens => _sessionIngressTokens;
    internal ConcurrentDictionary<string, string> SessionWorktrees => _sessionWorktrees;
    internal ConcurrentDictionary<string, byte> CompletedWorkIds => _completedWorkIds;
    internal ConcurrentDictionary<string, byte> V2Sessions => _v2Sessions;
    internal ConcurrentDictionary<string, byte> TimedOutSessions => _timedOutSessions;
    internal ConcurrentDictionary<string, byte> TitledSessions => _titledSessions;
    internal ConcurrentDictionary<string, string> SessionCompatIds => _sessionCompatIds;
}
