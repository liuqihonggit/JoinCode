namespace Core.Context;

/// <summary>
/// 查询作用域（ADR 0165）— Singleton ChatService 的会话级服务解耦：
/// 每次查询独立 queryId / ToolUseContext / ConversationTurn，借 AsyncLocal 随 ExecutionContext 流动。
/// </summary>
public sealed class ChatQueryScope : IDisposable {
    private static readonly AsyncLocal<ChatQueryScope?> s_current = new();
    private readonly AsyncLocalScope<ChatQueryScope> _scope;
    private bool _disposed;

    private ChatQueryScope(string queryId, int conversationTurn, string? sessionId) {
        QueryId = queryId;
        ConversationTurn = conversationTurn;
        SessionId = sessionId;
        ToolUseContext = new ToolUseContext();
        _scope = AsyncLocalScope<ChatQueryScope>.Enter(s_current, this);
    }

    /// <summary>无作用域时抛错 — 显式作用域内的代码用它取当前查询身份。</summary>
    public static ChatQueryScope Current => s_current.Value
        ?? throw new InvalidOperationException("当前没有 ChatQueryScope；请先调用 ChatQueryScope.Enter()。");

    /// <summary>无作用域返回 null — ChatService 用它判断是否需要创建隐式 scope。</summary>
    public static ChatQueryScope? CurrentOrNull => s_current.Value;

    /// <summary>当前查询身份。</summary>
    public string QueryId { get; }

    /// <summary>本查询专属工具上下文 — 不再由 Singleton ChatService 跨查询共享。</summary>
    public ToolUseContext ToolUseContext { get; }

    /// <summary>本查询在会话中的轮次序号 — 由 ChatService 在查询开始时从会话级计数器原子分配
    /// （并发查询各拿不同序号；不再随 scope 归零，否则每个查询都自称「第 0 轮」）。</summary>
    public int ConversationTurn { get; }

    /// <summary>本查询所属的会话代次（ADR 0167）— ChatService 在查询开始时快照 <c>IChatContextManager.SessionId</c>。
    /// `/clear` 换 sessionId 之后，在途查询的读写仍按这个代次解析，不会被搬到新代；null = 跟随当前指针。</summary>
    public string? SessionId { get; }

    /// <summary>进入查询作用域（queryId 空时生成；conversationTurn 与 sessionId 由调用方提供）。Dispose 恢复外层作用域。</summary>
    public static ChatQueryScope Enter(string? queryId = null, int conversationTurn = 0, string? sessionId = null) {
        var effective = string.IsNullOrWhiteSpace(queryId) ? $"q-{Guid.NewGuid():N}" : queryId;
        return new ChatQueryScope(effective, conversationTurn, string.IsNullOrWhiteSpace(sessionId) ? null : sessionId);
    }

    /// <summary>退出作用域（幂等）。</summary>
    public void Dispose() {
        if (_disposed) return;
        _disposed = true;
        _scope.Dispose();
    }
}
