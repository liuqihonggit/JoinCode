namespace JoinCode.Abstractions.LLM.Chat;

/// <summary>
/// 消息来源类型 — 对齐 TS MessageOrigin.kind
/// 不设置 (null) = human (keyboard) 真实用户输入
/// </summary>
public enum MessageOriginKind {
    [EnumValue("task-notification")] TaskNotification,
    [EnumValue("coordinator")] Coordinator,
    [EnumValue("channel")] Channel,
    /// <summary>轮内注入（steer）— 轮次运行中用户插入的输入，在下一 step 开头进入上下文</summary>
    [EnumValue("steer")] SteerInput,
}