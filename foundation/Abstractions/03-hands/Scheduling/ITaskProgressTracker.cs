
namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 任务进度追踪器 — 追踪 TODO 表完成数，供循环检测判断任务是否真正推进
/// </summary>
public interface ITaskProgressTracker
{
    /// <summary>
    /// 获取当前已完成的 TODO 项数量
    /// </summary>
    Task<int> GetCompletedTodoCountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 记录当前快照作为"上次检测时的完成数"
    /// </summary>
    Task SnapshotCurrentProgressAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 判断自上次快照以来任务是否有推进（完成数是否变化）
    /// </summary>
    Task<bool> HasProgressedSinceLastSnapshotAsync(CancellationToken cancellationToken = default);
}
