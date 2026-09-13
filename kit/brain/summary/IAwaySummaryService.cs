
namespace Core.Summary;

/// <summary>
/// 离开摘要服务接口 — 管理用户离开期间的事件跟踪与摘要生成
/// </summary>
public interface IAwaySummaryService
{
    /// <summary>
    /// 异步标记用户离开，启动离开期间的事件跟踪与自动保存。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    Task MarkAwayAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步生成离开摘要，汇总离开期间的事件、错误与待处理事项。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>包含汇总结果的 <see cref="AwaySummaryResult"/> 任务。</returns>
    Task<AwaySummaryResult> GenerateSummaryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步跟踪一个离开事件；若用户未处于离开状态则忽略。
    /// </summary>
    /// <param name="awayEvent">要跟踪的离开事件。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    Task TrackEventAsync(AwayEvent awayEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取一个值，指示用户当前是否处于离开状态。
    /// </summary>
    bool IsAway { get; }

    /// <summary>
    /// 获取用户离开时刻；若用户未离开，返回 <c>null</c>。
    /// </summary>
    DateTime? AwaySince { get; }
}
