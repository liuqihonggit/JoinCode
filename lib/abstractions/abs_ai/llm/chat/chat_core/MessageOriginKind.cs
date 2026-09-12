namespace JoinCode.Abstractions.LLM.Chat;

/// <summary>
/// 消息来源类型 — 对齐 TS MessageOrigin.kind
/// 不设置 (null) = human (keyboard) 真实用户输入
/// </summary>
public enum MessageOriginKind
{
    [EnumValue("task-notification")] TaskNotification,
    [EnumValue("coordinator")] Coordinator,
    [EnumValue("channel")] Channel,
}
