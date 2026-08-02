namespace JoinCode.Abstractions.Entity;

/// <summary>
/// Session 注册器 — 独立类，与 AgentRegistry 同套路
/// 管理所有 Session 的注册/查询
/// 全局唯一实例通过 Session.Registry 静态属性获取
/// </summary>
public sealed class SessionRegistry
{
    private readonly ConcurrentDictionary<ObjectId, Session> _sessions = new();

    /// <summary>
    /// 注册会话（internal，Session构造时自动调用）
    /// </summary>
    internal void Add(ObjectId id, Session session)
    {
        _sessions.TryAdd(id, session);
    }

    /// <summary>
    /// 注销会话（internal，Session.Dispose时自动调用）
    /// </summary>
    internal bool Remove(ObjectId id)
    {
        return _sessions.TryRemove(id, out _);
    }

    /// <summary>
    /// 按ObjectId获取会话
    /// </summary>
    public Session? Get(ObjectId id) => _sessions.GetValueOrDefault(id);

    /// <summary>
    /// 获取所有会话
    /// </summary>
    public IReadOnlyList<Session> GetAll() => [.. _sessions.Values];

    /// <summary>
    /// 获取活跃会话（最近有活动的）
    /// </summary>
    public IReadOnlyList<Session> GetActive(TimeSpan inactivityThreshold)
        => [.. _sessions.Values.Where(s => DateTime.UtcNow - s.LastActivityAt < inactivityThreshold)];

    /// <summary>
    /// 当前注册的会话总数
    /// </summary>
    public int Count => _sessions.Count;

    /// <summary>
    /// 清空所有注册（测试用）
    /// </summary>
    public void Clear()
    {
        _sessions.Clear();
    }
}
