
namespace Core.Scheduling;

/// <summary>
/// Workflow 状态存储接口 — 负责断点续跑的快照持久化与恢复
/// </summary>
public interface IWorkflowStateStore
{
    /// <summary>
    /// 保存 workflow 执行快照（原子写入 workflow_{id}.state.json）
    /// </summary>
    /// <param name="workflowId">Workflow 唯一标识</param>
    /// <param name="snapshot">执行快照</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task SaveSnapshotAsync(string workflowId, WorkflowSnapshot snapshot, CancellationToken cancellationToken = default);

    /// <summary>
    /// 加载 workflow 执行快照（损坏文件自动隔离并返回 null）
    /// </summary>
    /// <param name="workflowId">Workflow 唯一标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>快照；文件不存在或损坏时返回 null</returns>
    Task<WorkflowSnapshot?> LoadSnapshotAsync(string workflowId, CancellationToken cancellationToken = default);
}
