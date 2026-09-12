namespace Core.Context;

/// <summary>
/// 聊天管理操作类型
/// </summary>
public enum ChatAdminOperation
{
    /// <summary>初始化会话</summary>
    Initialize,
    /// <summary>清空聊天历史</summary>
    ClearHistory,
    /// <summary>压缩对话历史</summary>
    CompactHistory,
    /// <summary>撤回最后一轮对话</summary>
    RewindLastTurn,
    /// <summary>撤回到指定消息索引</summary>
    RewindToMessageIndex,
    /// <summary>撤回到会话初始状态</summary>
    RewindToStart,
    /// <summary>添加系统提醒</summary>
    AddSystemReminder,
    /// <summary>移除系统提醒</summary>
    RemoveSystemReminder,
    /// <summary>加载历史消息</summary>
    LoadSessionMessages,
    /// <summary>设置系统提示词</summary>
    SetSystemPrompt,
    /// <summary>获取消息列表</summary>
    GetMessageList,
}
