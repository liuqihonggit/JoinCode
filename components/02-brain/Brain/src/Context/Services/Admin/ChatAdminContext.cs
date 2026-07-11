namespace Core.Context;

/// <summary>
/// 管理操作上下文 — 在管理管道中间件之间传递数据
/// </summary>
public sealed class ChatAdminContext
{
    // === 输入 ===

    /// <summary>
    /// 管理操作类型
    /// </summary>
    public required ChatAdminOperation Operation { get; init; }

    /// <summary>
    /// 聊天上下文管理器 — 几乎所有管理操作都需要
    /// </summary>
    public required IChatContextManager ContextManager { get; init; }

    /// <summary>
    /// 压缩摘要 — CompactHistory 操作使用
    /// </summary>
    public string? Summary { get; init; }

    /// <summary>
    /// 消息索引 — RewindToMessageIndex 操作使用
    /// </summary>
    public int? MessageIndex { get; init; }

    /// <summary>
    /// 提醒 ID — Add/RemoveSystemReminder 操作使用
    /// </summary>
    public string? ReminderId { get; init; }

    /// <summary>
    /// 提醒内容 — AddSystemReminder 操作使用
    /// </summary>
    public string? ReminderContent { get; init; }

    /// <summary>
    /// 提醒优先级 — AddSystemReminder 操作使用
    /// </summary>
    public int? ReminderPriority { get; init; }

    /// <summary>
    /// 系统提示词 — SetSystemPrompt 操作使用
    /// </summary>
    public string? SystemPrompt { get; init; }

    /// <summary>
    /// 历史消息列表 — LoadSessionMessages 操作使用
    /// </summary>
    public IReadOnlyList<ApiMessageRecord>? Messages { get; init; }

    /// <summary>
    /// 工具执行上下文 — CompactHistory/Initialize 操作使用
    /// </summary>
    public ToolUseContext? ToolUseContext { get; init; }

    // === 输出 ===

    /// <summary>
    /// 撤回结果 — Rewind 操作设置
    /// </summary>
    public RewindResult? RewindResult { get; set; }

    /// <summary>
    /// 消息列表 — GetMessageList 操作设置
    /// </summary>
    public IReadOnlyList<ApiMessageRecord>? MessageList { get; set; }

    // === 错误处理 ===

    /// <summary>
    /// 操作错误 — 中间件可设置此字段，ChatService 检查并抛出异常
    /// </summary>
    public Exception? Error { get; set; }
}
