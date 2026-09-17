namespace Core.Bridge;

/// <summary>
/// Bridge 会话注册表 — 核心会话状态管理，持有所有以 sessionId 为 key 的会话状态
/// 提供注册、查找、遍历、原子更新等操作
/// </summary>
internal sealed class BridgeSessionRegistry
{
    private readonly ConcurrentDictionary<string, BridgeSessionState> _sessions = new();

    // ── 查询 ──

    /// <summary>当前活跃会话数</summary>
    public int Count => _sessions.Count;

    /// <summary>是否已有指定会话</summary>
    public bool Has(string sessionId) => _sessions.ContainsKey(sessionId);

    /// <summary>是否为 V2 会话</summary>
    public bool IsV2(string sessionId) => _sessions.TryGetValue(sessionId, out var s) && s.IsV2;

    /// <summary>是否已超时</summary>
    public bool IsTimedOut(string sessionId) => _sessions.TryGetValue(sessionId, out var s) && s.IsTimedOut;

    /// <summary>是否有 worktree</summary>
    public bool HasWorktree(string sessionId) => _sessions.TryGetValue(sessionId, out var s) && s.WorktreePath is not null;

    /// <summary>获取兼容 ID — 对齐 TS 端 sessionCompatIds.get(sessionId) ?? sessionId</summary>
    public string GetCompatId(string sessionId)
        => _sessions.TryGetValue(sessionId, out var s) && s.CompatId is not null ? s.CompatId : sessionId;

    /// <summary>获取会话句柄</summary>
    public BridgeSubprocessHandle? GetHandle(string sessionId)
        => _sessions.TryGetValue(sessionId, out var s) ? s.Handle : null;

    /// <summary>获取完整会话状态（内部访问，用于需要多个字段的场景）</summary>
    internal BridgeSessionState? GetState(string sessionId)
        => _sessions.TryGetValue(sessionId, out var s) ? s : null;

    /// <summary>获取入口令牌</summary>
    public string? GetIngressToken(string sessionId)
        => _sessions.TryGetValue(sessionId, out var s) ? s.IngressToken : null;

    /// <summary>获取 worktree 路径</summary>
    public bool TryGetWorktree(string sessionId, out string? worktreePath)
    {
        if (_sessions.TryGetValue(sessionId, out var s) && s.WorktreePath is not null)
        {
            worktreePath = s.WorktreePath;
            return true;
        }
        worktreePath = null;
        return false;
    }

    /// <summary>获取会话持续时间（毫秒）</summary>
    public long GetDurationMs(string sessionId, IClockService clock)
        => _sessions.TryGetValue(sessionId, out var s)
            ? (long)(clock.GetUtcNow() - s.StartTime).TotalMilliseconds
            : 0L;

    // ── 遍历 ──

    /// <summary>获取所有会话句柄</summary>
    public IEnumerable<BridgeSubprocessHandle> GetAllHandles()
        => _sessions.Values.Where(s => s.Handle is not null).Select(s => s.Handle!);

    /// <summary>获取所有会话 ID</summary>
    public IEnumerable<string> GetAllSessionIds() => _sessions.Keys;

    /// <summary>获取所有会话状态（sessionId → state）</summary>
    public IEnumerable<KeyValuePair<string, BridgeSessionState>> GetAllStates() => _sessions;

    /// <summary>获取所有工作 ID</summary>
    public IEnumerable<string> GetAllWorkIds() => _sessions.Values.Select(s => s.WorkId);

    /// <summary>获取所有有 worktree 的会话 ID</summary>
    public IEnumerable<string> GetAllWorktreeSessionIds()
        => _sessions.Where(kvp => kvp.Value.WorktreePath is not null).Select(kvp => kvp.Key);

    /// <summary>获取所有兼容 ID 映射（sessionId → compatId）</summary>
    public IEnumerable<KeyValuePair<string, string>> GetAllCompatIds()
        => _sessions
            .Where(kvp => kvp.Value.CompatId is not null)
            .Select(kvp => new KeyValuePair<string, string>(kvp.Key, kvp.Value.CompatId!));

    /// <summary>获取最近注册的会话（按注册时间最新）</summary>
    public KeyValuePair<string, BridgeSubprocessHandle?>? GetLastSession()
    {
        if (_sessions.Count == 0) return null;
        var latestSessionId = string.Empty;
        var latestTime = DateTime.MinValue;
        foreach (var (sessionId, state) in _sessions)
        {
            if (state.StartTime > latestTime)
            {
                latestTime = state.StartTime;
                latestSessionId = sessionId;
            }
        }
        return _sessions.TryGetValue(latestSessionId, out var s)
            ? new KeyValuePair<string, BridgeSubprocessHandle?>(latestSessionId, s.Handle)
            : null;
    }

    // ── 修改 ──

    /// <summary>注册或覆盖会话状态</summary>
    public void Register(string sessionId, BridgeSessionState state) => _sessions[sessionId] = state;

    /// <summary>标记会话已超时</summary>
    public void MarkTimedOut(string sessionId)
        => _sessions.AddOrUpdate(
            sessionId,
            _ => throw new InvalidOperationException($"Session {sessionId} not found"),
            (_, old) => old with { IsTimedOut = true });

    /// <summary>清除超时标记 — 返回是否曾被标记为超时</summary>
    public bool RemoveTimedOut(string sessionId)
    {
        while (_sessions.TryGetValue(sessionId, out var s))
        {
            if (!s.IsTimedOut) return false;
            if (_sessions.TryUpdate(sessionId, s with { IsTimedOut = false }, s)) return true;
        }
        return false;
    }

    /// <summary>更新入口令牌</summary>
    public void UpdateIngressToken(string sessionId, string token)
        => _sessions.AddOrUpdate(
            sessionId,
            _ => throw new InvalidOperationException($"Session {sessionId} not found"),
            (_, old) => old with { IngressToken = token });

    /// <summary>更新 worktree 路径</summary>
    public void UpdateWorktree(string sessionId, string path)
        => _sessions.AddOrUpdate(
            sessionId,
            _ => throw new InvalidOperationException($"Session {sessionId} not found"),
            (_, old) => old with { WorktreePath = path });

    /// <summary>更新会话句柄的 access token</summary>
    public async Task UpdateAccessTokenAsync(string sessionId, string token, CancellationToken ct)
    {
        if (_sessions.TryGetValue(sessionId, out var s) && s.Handle is not null)
        {
            await s.Handle.UpdateAccessTokenAsync(token, ct).ConfigureAwait(false);
        }
    }

    /// <summary>移除 worktree 记录并返回路径</summary>
    public bool RemoveWorktree(string sessionId, out string? worktreePath)
    {
        while (_sessions.TryGetValue(sessionId, out var s))
        {
            if (s.WorktreePath is null)
            {
                worktreePath = null;
                return false;
            }
            worktreePath = s.WorktreePath;
            if (_sessions.TryUpdate(sessionId, s with { WorktreePath = null }, s)) return true;
        }
        worktreePath = null;
        return false;
    }

    /// <summary>移除会话</summary>
    public void Remove(string sessionId) => _sessions.TryRemove(sessionId, out _);

    /// <summary>获取兼容 ID（内部，供 CleanupSession 跨 tracker 协作）</summary>
    internal bool TryGetCompatId(string sessionId, out string? compatId)
    {
        compatId = _sessions.TryGetValue(sessionId, out var s) ? s.CompatId : null;
        return compatId is not null;
    }

    /// <summary>清空所有会话</summary>
    public void Clear() => _sessions.Clear();
}
