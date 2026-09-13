namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 服务消息类型枚举 — 替代原 ServiceMessageTypes 静态常量类
/// 对齐 Reasonix event.EventKind: 应用级强类型事件
/// </summary>
public enum ServiceMessageType
{
    [EnumValue("task:started")] TaskStarted,
    [EnumValue("task:completed")] TaskCompleted,
    [EnumValue("task:failed")] TaskFailed,
    [EnumValue("agent:spawned")] AgentSpawned,
    [EnumValue("agent:completed")] AgentCompleted,
    [EnumValue("cron:fired")] CronTaskFired,
    [EnumValue("notification")] Notification,
    [EnumValue("system:shutdown")] ShutdownRequested,

    [EnumValue("system:started")] SystemStarted,
    [EnumValue("system:stopped")] SystemStopped,
    [EnumValue("service:status_changed")] ServiceStatusChanged,

    [EnumValue("session:started")] SessionStarted,
    [EnumValue("session:ended")] SessionEnded,
    [EnumValue("turn:started")] TurnStarted,
    [EnumValue("turn:completed")] TurnCompleted,
    [EnumValue("compaction:started")] CompactionStarted,
    [EnumValue("compaction:completed")] CompactionCompleted,
    [EnumValue("goal:achieved")] GoalAchieved,
    [EnumValue("goal:failed")] GoalFailed,
}
