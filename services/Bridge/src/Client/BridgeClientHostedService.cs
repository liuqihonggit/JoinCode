
using JoinCode.Abstractions.Attributes;

namespace Core.Bridge;

[Register(typeof(IHostedService))]
public sealed partial class BridgeClientHostedService : IHostedService, IAsyncDisposable
{
    private readonly BridgeClient _bridgeClient;
    private readonly BridgeConfig _config;
    [Inject] private readonly ILogger<BridgeClientHostedService>? _logger;
    private readonly CancellationTokenSource _cts = new();

    public BridgeClientHostedService(
        BridgeClient bridgeClient,
        BridgeConfig config,
        ILogger<BridgeClientHostedService>? logger = null)
    {
        _bridgeClient = bridgeClient ?? throw new ArgumentNullException(nameof(bridgeClient));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_config.Enabled)
        {
            _logger?.LogInformation("[BridgeClientHostedService] Bridge 客户端已禁用");
            return;
        }

        try
        {
            _logger?.LogInformation("[BridgeClientHostedService] 启动 Bridge 客户端...");

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);

            await _bridgeClient.StartAsync(linkedCts.Token).ConfigureAwait(false);

            _logger?.LogInformation("[BridgeClientHostedService] Bridge 客户端已启动");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[BridgeClientHostedService] 启动 Bridge 客户端失败");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (!_config.Enabled)
        {
            return;
        }

        try
        {
            _logger?.LogInformation("[BridgeClientHostedService] 停止 Bridge 客户端...");

            await _cts.CancelAsync().ConfigureAwait(false);
            await _bridgeClient.StopAsync(cancellationToken).ConfigureAwait(false);

            _logger?.LogInformation("[BridgeClientHostedService] Bridge 客户端已停止");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[BridgeClientHostedService] 停止 Bridge 客户端失败");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        await _bridgeClient.DisposeAsync().ConfigureAwait(false);
        _cts.Dispose();
    }
}
