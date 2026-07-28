namespace JoinCode.Dream;

/// <summary>
/// 做梦功能接口 - 提供记忆整合功能
/// </summary>
public interface IDreamFeature
{
    /// <summary>
    /// 执行梦境整合
    /// </summary>
    /// <param name="request">执行请求</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>执行结果</returns>
    Task<DreamResult> ExecuteAsync(DreamRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取任务状态
    /// </summary>
    /// <param name="taskId">任务ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>任务状态，如果不存在返回null</returns>
    Task<DreamTaskState?> GetTaskStatusAsync(string taskId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取所有任务
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>任务字典</returns>
    Task<IReadOnlyDictionary<string, DreamTaskState>> ListTasksAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 终止任务
    /// </summary>
    /// <param name="taskId">任务ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task KillTaskAsync(string taskId, CancellationToken cancellationToken = default);
}
