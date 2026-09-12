namespace JoinCode.Abstractions.LLM.Chat;

/// <summary>
/// 消息角色枚举 — [EnumValue] 由 EnumMetadataGenerator 自动生成映射
/// [DisplayText] 用于 UI 显示
/// </summary>
public enum MessageRole
{
    [EnumValue("system")] [DisplayText("系统")] System,
    [EnumValue("user")] [DisplayText("你")] User,
    [EnumValue("assistant")] [DisplayText("AI")] Assistant,
    [EnumValue("tool")] [DisplayText("工具")] Tool
}
