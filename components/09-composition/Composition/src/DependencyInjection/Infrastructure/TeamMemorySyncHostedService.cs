
using JoinCode.Abstractions.Attributes;

namespace Core.DependencyInjection;

[Register(typeof(IHostedService))]
public sealed partial class TeamMemorySyncHostedService : IHostedService
{
    private readonly global::Memdir.Sync.ITeamMemorySyncService _syncService;
    [Inject] private readonly ILogger<TeamMemorySyncHostedService>? _logger;

    public TeamMemorySyncHostedService(
        global::Memdir.Sync.ITeamMemorySyncService syncService,
        ILogger<TeamMemorySyncHostedService>? logger = null)
    {
        _syncService = syncService;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _syncService.StartAsync(cancellationToken).ConfigureAwait(false);
            _logger?.LogInformation(L.T(StringKey.TeamMemorySyncStartedLog));
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, L.T(StringKey.TeamMemorySyncStartFailedLog));
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _syncService.StopAsync(cancellationToken).ConfigureAwait(false);
            _logger?.LogInformation(L.T(StringKey.TeamMemorySyncStoppedLog));
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, L.T(StringKey.TeamMemorySyncStopFailedLog));
        }
    }
}
