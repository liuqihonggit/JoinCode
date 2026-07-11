
using JoinCode.Abstractions.Attributes;

namespace Core.Bridge;

/// <summary>
/// Bridge 服务器托管服务 - 跟随主机生命周期自动启停
/// </summary>
[Register(typeof(IHostedService))]
public sealed partial class BridgeServerHostedService : IHostedService, IAsyncDisposable
{
    private readonly BridgeServer _bridgeServer;
    private readonly BridgeConfig _config;
    private readonly CapacityWakeService? _capacityWakeService;
    [Inject] private readonly ILogger<BridgeServerHostedService>? _logger;

    public BridgeServerHostedService(
        BridgeServer bridgeServer,
        BridgeConfig config,
        CapacityWakeService? capacityWakeService = null,
        ILogger<BridgeServerHostedService>? logger = null)
    {
        _bridgeServer = bridgeServer ?? throw new ArgumentNullException(nameof(bridgeServer));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _capacityWakeService = capacityWakeService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_config.Enabled)
        {
            _logger?.LogInformation("[BridgeServerHostedService] Bridge 服务器已禁用");
            return;
        }

        try
        {
            _logger?.LogInformation("[BridgeServerHostedService] 启动 Bridge 服务器...");
            _bridgeServer.Start();
            _logger?.LogInformation("[BridgeServerHostedService] Bridge 服务器已启动");

            if (_capacityWakeService != null)
            {
                await _capacityWakeService.StartMonitoringAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                _logger?.LogInformation("[BridgeServerHostedService] 容量监控已启动");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[BridgeServerHostedService] 启动 Bridge 服务器失败");
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (!_config.Enabled)
        {
            return;
        }

        try
        {
            if (_capacityWakeService != null)
            {
                await _capacityWakeService.StopMonitoringAsync().ConfigureAwait(false);
                _logger?.LogInformation("[BridgeServerHostedService] 容量监控已停止");
            }

            _logger?.LogInformation("[BridgeServerHostedService] 停止 Bridge 服务器...");
            await _bridgeServer.StopAsync(cancellationToken).ConfigureAwait(false);
            _logger?.LogInformation("[BridgeServerHostedService] Bridge 服务器已停止");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[BridgeServerHostedService] 停止 Bridge 服务器失败");
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _bridgeServer.Dispose();
        return ValueTask.CompletedTask;
    }
}
