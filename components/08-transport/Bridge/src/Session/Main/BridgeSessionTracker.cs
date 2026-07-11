namespace Core.Bridge;

/// <summary>
/// Bridge 会话状态跟踪器 — 封装 BridgeMain 中 7 个 Dictionary + 3 个 HashSet 的状态管理
/// 对齐 TS 端 runBridgeLoop 中的 Map/Set 集合
/// </summary>
public sealed class BridgeSessionTracker
{
    private readonly Dictionary<string, BridgeSubprocessHandle> _activeSessions = new();
    private readonly Dictionary<string, DateTime> _sessionStartTimes = new();
    private readonly Dictionary<string, string> _sessionWorkIds = new();
    private readonly Dictionary<string, string> _sessionIngressTokens = new();
    private readonly Dictionary<string, string> _sessionWorktrees = new();
    private readonly HashSet<string> _completedWorkIds = new();
    private readonly HashSet<string> _timedOutSessions = new();
    private readonly HashSet<string> _v2Sessions = new();
    private readonly HashSet<string> _titledSessions = new();
    private readonly Dictionary<string, string> _sessionCompatIds = new();

    /// <summary>当前活跃会话数</summary>
    public int ActiveSessionCount => _activeSessions.Count;

    /// <summary>是否已完成指定工作项</summary>
    public bool IsWorkCompleted(string workId) => _completedWorkIds.Contains(workId);

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
    public bool IsV2Session(string sessionId) => _v2Sessions.Contains(sessionId);

    /// <summary>是否已获取标题</summary>
    public bool HasTitle(string compatId) => _titledSessions.Contains(compatId);

    /// <summary>标记已获取标题</summary>
    public void MarkTitled(string compatId) => _titledSessions.Add(compatId);

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
            _v2Sessions.Add(sessionId);
    }

    /// <summary>标记工作项已完成</summary>
    public void MarkWorkCompleted(string workId) => _completedWorkIds.Add(workId);

    /// <summary>标记会话已超时</summary>
    public void MarkTimedOut(string sessionId) => _timedOutSessions.Add(sessionId);

    /// <summary>检查并移除超时标记 — 返回是否曾被标记为超时</summary>
    public bool RemoveTimedOut(string sessionId) => _timedOutSessions.Remove(sessionId);

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

    /// <summary>获取所有活跃会话句柄</summary>
    public IReadOnlyList<BridgeSubprocessHandle> GetAllHandles() => _activeSessions.Values.ToList();

    /// <summary>获取所有会话 ID</summary>
    public IReadOnlyList<string> GetAllSessionIds() => _activeSessions.Keys.ToList();

    /// <summary>获取所有工作 ID</summary>
    public IReadOnlyList<string> GetAllWorkIds() => _sessionWorkIds.Values.ToList();

    /// <summary>获取最后一个会话</summary>
    public KeyValuePair<string, BridgeSubprocessHandle>? GetLastSession()
        => _activeSessions.Count > 0 ? _activeSessions.Last() : null;

    /// <summary>清理单个会话的跟踪状态</summary>
    public void CleanupSession(string sessionId, Action<string>? onRemoveCompatId = null)
    {
        _activeSessions.Remove(sessionId);
        _sessionStartTimes.Remove(sessionId);
        _sessionWorkIds.Remove(sessionId);
        _sessionIngressTokens.Remove(sessionId);
        _timedOutSessions.Remove(sessionId);
        _v2Sessions.Remove(sessionId);

        if (_sessionCompatIds.TryGetValue(sessionId, out var compatId))
        {
            _titledSessions.Remove(compatId);
            onRemoveCompatId?.Invoke(compatId);
        }
        _sessionCompatIds.Remove(sessionId);
    }

    /// <summary>移除 worktree 记录并返回路径</summary>
    public bool RemoveWorktree(string sessionId, out string? worktreePath)
        => _sessionWorktrees.Remove(sessionId, out worktreePath);

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
    internal Dictionary<string, BridgeSubprocessHandle> ActiveSessions => _activeSessions;
    internal Dictionary<string, DateTime> SessionStartTimes => _sessionStartTimes;
    internal Dictionary<string, string> SessionWorkIds => _sessionWorkIds;
    internal Dictionary<string, string> SessionIngressTokens => _sessionIngressTokens;
    internal Dictionary<string, string> SessionWorktrees => _sessionWorktrees;
    internal HashSet<string> CompletedWorkIds => _completedWorkIds;
    internal HashSet<string> V2Sessions => _v2Sessions;
    internal HashSet<string> TimedOutSessions => _timedOutSessions;
    internal HashSet<string> TitledSessions => _titledSessions;
    internal Dictionary<string, string> SessionCompatIds => _sessionCompatIds;
}
