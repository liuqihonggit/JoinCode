
namespace Memdir.Sync;

public interface ITeamMemorySyncService : IDisposable
{
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task SyncAsync(string? filePath = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MemorySyncEvent>> GetSyncHistoryAsync(int limit = 100, CancellationToken cancellationToken = default);
    Task<SyncConflictResolution> ResolveConflictAsync(string filePath, SyncConflictResolution resolution, CancellationToken cancellationToken = default);
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
