namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 意图上报器 — Worker 执行中检测要改的文件，热文件契约改实时通过 IMailbox 上报队长
/// 与 IntentCollector 互补：Collector 收集所有意图供 HotSpotTracker 统计，Reporter 对热文件契约改额外发邮箱通知队长
/// </summary>
public interface IIntentReporter
{
    /// <summary>
    /// Worker 上报修改意图：收集到 IntentCollector + 热文件契约改发 IMailbox 通知队长
    /// </summary>
    /// <param name="workerId">上报的 Worker ID</param>
    /// <param name="captainId">队长 Agent ID（接收通知）</param>
    /// <param name="intents">修改意图列表</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task ReportModifyIntentsAsync(string workerId, string captainId, IReadOnlyList<FileModifyIntent> intents, CancellationToken cancellationToken = default);
}
