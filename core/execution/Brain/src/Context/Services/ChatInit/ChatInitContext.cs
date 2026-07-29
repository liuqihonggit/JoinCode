namespace Core.Context;

/// <summary>
/// 聊天初始化上下文 — 在初始化管道中间件之间传递数据
/// </summary>
public sealed class ChatInitContext
{
    // === 输入 ===

    /// <summary>
    /// 工具执行上下文 — 内容替换状态等
    /// </summary>
    public required ToolUseContext ToolUseContext { get; init; }

    /// <summary>
    /// 聊天上下文管理器 — 加载上下文、获取消息列表
    /// </summary>
    public required IChatContextManager ContextManager { get; init; }

    // === 中间件输出 ===

    /// <summary>
    /// 会话 ID — 由 ContextLoadMiddleware 设置
    /// </summary>
    public string SessionId { get; set; } = "default";

    /// <summary>
    /// 会话成本持久化 — 由 CostRestoreMiddleware 设置，供 ChatInitializer 后续使用
    /// </summary>
    public ISessionCostPersistence? SessionCostPersistence { get; set; }
}
