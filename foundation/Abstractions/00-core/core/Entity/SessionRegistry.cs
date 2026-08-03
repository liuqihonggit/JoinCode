namespace JoinCode.Abstractions.Entity;

/// <summary>
/// Session 注册器 — 基于 MapRegistry，内部字典，对外暴露遍历器 + 字典视图
/// </summary>
public sealed class SessionRegistry : MapRegistry<ObjectId, Session>
{
    /// <summary>注册会话（internal，Session构造时自动调用）</summary>
    internal void Add(ObjectId id, Session session) => AddCore(id, session);

    /// <summary>注销会话（internal，Session.Dispose时自动调用）</summary>
    internal bool Remove(ObjectId id) => RemoveCore(id);

    /// <summary>获取活跃会话（最近有活动的）</summary>
    public IEnumerable<Session> GetActive(TimeSpan inactivityThreshold)
        => Where(s => DateTime.UtcNow - s.LastActivityAt < inactivityThreshold);
}
