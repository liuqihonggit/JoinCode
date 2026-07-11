namespace JoinCode.Abstractions.Interfaces.Scheduling;

/// <summary>
/// Cron 调度器引用接口 — 对齐 TS setScheduledTasksEnabled
/// 工具处理器通过此接口通知调度器任务已变更，触发立即刷新
/// </summary>
public interface ICronSchedulerRef
{
    /// <summary>
    /// 通知调度器任务已变更 — 对齐 TS setScheduledTasksEnabled(true)
    /// 调度器应在下一个 tick 重新加载任务列表
    /// </summary>
    void NotifyTaskChanged();
}
