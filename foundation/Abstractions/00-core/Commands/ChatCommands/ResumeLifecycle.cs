namespace JoinCode.Abstractions.ChatCommands;

/// <summary>
/// 主动循环生命周期的"运行/暂停/清理"动作集合。
/// 适用范围: /proactive (pause/resume) + /goal (pause/resume/clear) 两个命令
/// 3 个值全为运行态控制,与 ToggleAction(激活/停用 主开关)语义不同,创建独立枚举。
///
/// 使用示例:
/// - FromValue("pause")  → ResumeLifecycle.Pause
/// - FromValue("RESUME") → ResumeLifecycle.Resume (OrdinalIgnoreCase)
/// - ResumeLifecycle.Clear.ToValue() → "clear"
/// - clear 在 GoalCommand 中有 stop/off/reset/cancel 4 个别名
/// </summary>
public enum ResumeLifecycle
{
    /// <summary>暂停主动循环(保留状态,可 Resume 恢复)</summary>
    [EnumValue("pause")] Pause,

    /// <summary>恢复被暂停的主动循环</summary>
    [EnumValue("resume")] Resume,

    /// <summary>清除/停止目标(不可恢复,丢弃当前状态)</summary>
    [EnumValue("clear")] Clear,

    /// <summary>清除别名 — 等价于 clear (GoalCommand 兼容)</summary>
    [EnumValue("stop")] Stop,

    /// <summary>清除别名 — 等价于 clear (GoalCommand 兼容)</summary>
    [EnumValue("off")] Off,

    /// <summary>清除别名 — 等价于 clear (GoalCommand 兼容)</summary>
    [EnumValue("reset")] Reset,

    /// <summary>清除别名 — 等价于 clear (GoalCommand 兼容)</summary>
    [EnumValue("cancel")] Cancel,
}
