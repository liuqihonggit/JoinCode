
namespace Core.DependencyInjection;

/// <summary>
/// 团队记忆同步托管服务 — 作为 <see cref="IHostedService"/> 在应用启动时启动团队记忆同步，
/// 在应用停止时停止同步。
/// </summary>
[Register(typeof(IHostedService), ServiceLifetime.Singleton)]
public sealed partial class TeamMemorySyncHostedService : ServiceEntity, IHostedService {
    private readonly global::Memdir.Sync.ITeamMemorySyncService _syncService;
    private readonly ILogger<TeamMemorySyncHostedService>? _logger;

    /// <summary>
    /// 初始化 <see cref="TeamMemorySyncHostedService"/> 实例。
    /// </summary>
    /// <param name="syncService">团队记忆同步服务。</param>
    /// <param name="logger">可选的日志记录器。</param>
    public TeamMemorySyncHostedService(
        global::Memdir.Sync.ITeamMemorySyncService syncService,
        ILogger<TeamMemorySyncHostedService>? logger = null) {
        _syncService = syncService;
        _logger = logger;
    }

    /// <summary>
    /// 启动团队记忆同步服务。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task StartAsync(CancellationToken cancellationToken) {
        try {
            await _syncService.StartAsync(cancellationToken).ConfigureAwait(false);
            _logger?.LogInformation(L.T(StringKey.TeamMemorySyncStartedLog));
        } catch (Exception ex) {
            _logger?.LogWarning(ex, L.T(StringKey.TeamMemorySyncStartFailedLog));
        }
    }

    /// <summary>
    /// 停止团队记忆同步服务。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task StopAsync(CancellationToken cancellationToken) {
        try {
            await _syncService.StopAsync(cancellationToken).ConfigureAwait(false);
            _logger?.LogInformation(L.T(StringKey.TeamMemorySyncStoppedLog));
        } catch (Exception ex) {
            _logger?.LogWarning(ex, L.T(StringKey.TeamMemorySyncStopFailedLog));
        }
    }
}