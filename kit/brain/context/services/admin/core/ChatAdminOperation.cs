namespace Core.Context;

/// <summary>
/// 聊天管理操作类型
/// </summary>
public enum ChatAdminOperation
{
    /// <summary>初始化会话</summary>
    [EnumValue("initialize")] Initialize,
    /// <summary>清空聊天历史</summary>
    [EnumValue("clear_history")] ClearHistory,
    /// <summary>压缩对话历史</summary>
    [EnumValue("compact_history")] CompactHistory,
    /// <summary>撤回最后一轮对话</summary>
    [EnumValue("rewind_last_turn")] RewindLastTurn,
    /// <summary>撤回到指定消息索引</summary>
    [EnumValue("rewind_to_message_index")] RewindToMessageIndex,
    /// <summary>撤回到会话初始状态</summary>
    [EnumValue("rewind_to_start")] RewindToStart,
    /// <summary>添加系统提醒</summary>
    [EnumValue("add_system_reminder")] AddSystemReminder,
    /// <summary>移除系统提醒</summary>
    [EnumValue("remove_system_reminder")] RemoveSystemReminder,
    /// <summary>加载历史消息</summary>
    [EnumValue("load_session_messages")] LoadSessionMessages,
    /// <summary>设置系统提示词</summary>
    [EnumValue("set_system_prompt")] SetSystemPrompt,
    /// <summary>获取消息列表</summary>
    [EnumValue("get_message_list")] GetMessageList,
}
