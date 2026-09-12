namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 契约变更广播器 — 队长 push 热文件后，给依赖 Worker 发 ContractChanged 消息
/// 热文件变就广播，非热文件不广播；定向投递不全局广播
/// </summary>
public interface IContractChangeBroadcaster
{
    /// <summary>
    /// 队长 push 热文件后广播契约变更通知
    /// </summary>
    /// <param name="captainId">队长 Agent ID（发送者）</param>
    /// <param name="filePath">变更的文件路径</param>
    /// <param name="dependentWorkers">依赖该文件的 Worker 列表（接收者）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>成功通知的 Worker 数（非热文件返回0）</returns>
    Task<int> BroadcastContractChangeAsync(string captainId, string filePath, IReadOnlyList<string> dependentWorkers, CancellationToken cancellationToken = default);
}
