namespace JoinCode.Abstractions.LLM.Chat;

/// <summary>
/// ApiMessage.Metadata 字典键名枚举 — 统一管理，消除硬编码字符串
/// 对齐 ChatService 写入格式，确保读写一致
/// </summary>
public enum MessageMetadataKey
{
    /// <summary>Assistant 工具调用列表 — 格式: [{Id, Name, Arguments}]</summary>
    [EnumValue("ToolCalls")] ToolCalls,

    /// <summary>Tool 消息的工具调用 ID</summary>
    [EnumValue("ToolCallId")] ToolCallId,

    /// <summary>Tool 消息的工具名称</summary>
    [EnumValue("ToolName")] ToolName,

    /// <summary>消息时间戳 — ISO 8601 格式</summary>
    [EnumValue("timestamp")] Timestamp,
}
