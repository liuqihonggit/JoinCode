namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 会话上下文 — AsyncLocal 存储当前会话 ObjectId，Entity 构造时自动继承
/// 用法: using (SessionContext.EnterScope(session.ObjectId)) { ... new Agent(...); }
/// 作用域内创建的 Entity 自动继承 sessionId，无需显式传参
/// </summary>
public static class SessionContext
{
    private static readonly AsyncLocal<ObjectId?> _current = new();

    /// <summary>当前会话 ObjectId — null 表示未设置</summary>
    public static ObjectId? Current => _current.Value;

    /// <summary>
    /// 进入会话作用域 — 返回 IDisposable，离开作用域自动恢复
    /// </summary>
    public static IDisposable EnterScope(ObjectId sessionId)
    {
        if (sessionId.IsEmpty)
            throw new ArgumentException("SessionId 不能为空", nameof(sessionId));
        return AsyncLocalScope<ObjectId?>.Enter(_current, sessionId);
    }
}
