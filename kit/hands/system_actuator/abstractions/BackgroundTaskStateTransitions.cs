namespace Services.SystemActuator;

/// <summary>
/// 后台任务状态转换规则 — 集中定义 SystemActuatorRegistry 后台任务的所有合法转换
/// <para>原 RegisterContextAsync 异步回调 + CancelTaskAsync + KillAllRunningAsync 分散赋值,现统一 guard</para>
/// <para>与 AgentStateMachine 转换表不同:后台任务不支持重试(Completed→Running 禁止),不支持 Paused</para>
/// </summary>
internal static class BackgroundTaskStateTransitions
{
    /// <summary>
    /// 是否为终态 — Completed/Failed/Cancelled 为终态,不可再转换
    /// </summary>
    public static bool IsTerminal(TaskExecutionStatus status) =>
        status is TaskExecutionStatus.Completed or TaskExecutionStatus.Failed or TaskExecutionStatus.Cancelled;

    /// <summary>
    /// 是否可取消 — 仅 Pending/Running 可取消
    /// </summary>
    public static bool CanCancel(TaskExecutionStatus status) =>
        status is TaskExecutionStatus.Pending or TaskExecutionStatus.Running;

    /// <summary>
    /// 是否可完成 — 仅 Running 可转 Completed(异步回调成功路径验证)
    /// </summary>
    public static bool CanComplete(TaskExecutionStatus status) =>
        status == TaskExecutionStatus.Running;
}
