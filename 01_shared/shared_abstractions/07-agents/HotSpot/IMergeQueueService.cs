namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 合并队列服务 — 队长串行处理Worker产出：编译校验→合并→push
/// Worker提交产出到队列不直接push，队长独占push串行合并防冲突
/// </summary>
public interface IMergeQueueService
{
    /// <summary>
    /// Worker 提交产出到合并队列
    /// </summary>
    Task EnqueueAsync(MergeQueueItem item, CancellationToken cancellationToken = default);

    /// <summary>
    /// 队长串行处理下一个：编译校验→合并→返回结果
    /// </summary>
    Task<MergeResult> ProcessNextAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取待处理队列项
    /// </summary>
    IReadOnlyList<MergeQueueItem> GetPending();

    /// <summary>
    /// 待处理数量
    /// </summary>
    int PendingCount { get; }
}
