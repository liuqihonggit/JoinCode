namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 意图收集器 — 收集 Worker 上报的文件修改意图，供 HotSpotTracker 统计热点
/// 线程安全，支持多 Worker 并发上报
/// </summary>
public interface IIntentCollector
{
    /// <summary>
    /// Worker 上报修改意图（启动时计划 + 执行中实时）
    /// </summary>
    /// <param name="workerId">上报的 Worker ID</param>
    /// <param name="intents">修改意图列表</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task ReportAsync(string workerId, IReadOnlyList<FileModifyIntent> intents, CancellationToken cancellationToken = default);

    /// <summary>
    /// 查询某文件的所有修改意图
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>该文件的所有意图记录（可能为空）</returns>
    IReadOnlyList<FileModifyIntent> GetIntents(string filePath);

    /// <summary>
    /// 查询所有文件的修改意图
    /// </summary>
    /// <returns>所有意图记录</returns>
    IReadOnlyList<FileModifyIntent> GetAllIntents();

    /// <summary>
    /// 清理某 Worker 的所有上报（Worker 完成或中断时调用）
    /// </summary>
    /// <param name="workerId">Worker ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task RemoveWorkerAsync(string workerId, CancellationToken cancellationToken = default);
}
