
namespace Memdir.Sync;

/// <summary>
/// 团队记忆同步服务接口 — 提供团队共享记忆的启动、停止、同步和冲突解决功能
/// </summary>
public interface ITeamMemorySyncService : IAsyncDisposable {
    /// <summary>
    /// 启动同步服务
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 停止同步服务
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 执行同步操作
    /// </summary>
    /// <param name="filePath">指定同步的文件路径,为 null 则同步全部</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    Task SyncAsync(string? filePath = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取同步历史记录
    /// </summary>
    /// <param name="limit">返回记录数上限</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>同步事件历史列表</returns>
    Task<IEnumerable<MemorySyncEvent>> GetSyncHistoryAsync(int limit = 100, CancellationToken cancellationToken = default);

    /// <summary>
    /// 解决指定文件的同步冲突
    /// </summary>
    /// <param name="filePath">冲突文件路径</param>
    /// <param name="resolution">冲突解决方案</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>实际应用的冲突解决方案</returns>
    Task<SyncConflictResolution> ResolveConflictAsync(string filePath, SyncConflictResolution resolution, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取一个值,指示同步服务是否正在运行
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// 同步指定团队的共享记忆
    /// </summary>
    /// <param name="teamId">团队 ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>同步状态</returns>
    Task<TeamSyncStatus> SyncTeamMemoryAsync(string teamId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取团队同步状态
    /// </summary>
    /// <param name="teamId">团队 ID</param>
    /// <returns>同步状态，不存在则返回 null</returns>
    TeamSyncStatus? GetSyncStatus(string teamId);
}