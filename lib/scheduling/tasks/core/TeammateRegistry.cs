namespace Core.Scheduling.Tasks;

/// <summary>
/// Teammate 注册表 — 合并 _activeTeammates + _pendingMessages 两个字典
/// 从 InProcessTeammateTaskExecutor 提取,降低大类字段数和字典操作复杂度
/// 两个字典都以 teammateId 为 key,Register/Stop/TryCleanup 时同时操作
/// </summary>
internal sealed class TeammateRegistry
{
    private readonly ConcurrentDictionary<string, TeammateState> _activeTeammates = new();
    private readonly ConcurrentDictionary<string, Channel<CoordinatorMessage>> _pendingMessages = new();

    /// <summary>活跃队友状态表 — 暴露给 pipeline context 直接访问</summary>
    public ConcurrentDictionary<string, TeammateState> ActiveTeammates => _activeTeammates;

    /// <summary>待处理消息通道表 — 暴露给 pipeline context 直接访问</summary>
    public ConcurrentDictionary<string, Channel<CoordinatorMessage>> PendingMessages => _pendingMessages;

    /// <summary>注册 teammate — 同时写入状态和消息通道</summary>
    public void Register(string teammateId, TeammateState state, Channel<CoordinatorMessage> channel)
    {
        _activeTeammates[teammateId] = state;
        _pendingMessages[teammateId] = channel;
    }

    /// <summary>注销 teammate — 仅移除状态,不清理资源</summary>
    public void Unregister(string teammateId)
    {
        _activeTeammates.TryRemove(teammateId, out _);
    }

    /// <summary>移除 teammate — 同时移除状态和通道,返回是否成功</summary>
    public bool TryRemove(string teammateId, [MaybeNullWhen(false)] out TeammateState state, out Channel<CoordinatorMessage>? channel)
    {
        if (_activeTeammates.TryRemove(teammateId, out state!))
        {
            _pendingMessages.TryRemove(teammateId, out channel);
            return true;
        }

        channel = null;
        return false;
    }

    /// <summary>尝试获取 teammate 状态</summary>
    public bool TryGetState(string teammateId, [MaybeNullWhen(false)] out TeammateState state)
        => _activeTeammates.TryGetValue(teammateId, out state!);

    /// <summary>尝试获取 teammate 消息通道</summary>
    public bool TryGetChannel(string teammateId, [MaybeNullWhen(false)] out Channel<CoordinatorMessage> channel)
        => _pendingMessages.TryGetValue(teammateId, out channel!);

    /// <summary>是否包含指定 teammate</summary>
    public bool Contains(string teammateId)
        => _activeTeammates.ContainsKey(teammateId);

    /// <summary>所有活跃 teammate ID</summary>
    public IEnumerable<string> Keys => _activeTeammates.Keys;

    /// <summary>生成所有活跃 teammate 的状态快照 — 供 GUI 渲染子会话树</summary>
    public IEnumerable<TeammateStateSnapshot> GetSnapshots()
        => _activeTeammates.Select(kv => new TeammateStateSnapshot(
            kv.Key, kv.Value.TeammateMeta.ParentSessionId, kv.Value.Task,
            kv.Value.IsIdle, kv.Value.TurnCount, kv.Value.LastResult)).ToList();
}
