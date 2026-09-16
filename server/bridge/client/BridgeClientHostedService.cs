namespace Core.Bridge;

/// <summary>
/// Bridge 客户端托管服务 — 作为 IHostedService 管理 BridgeClient 生命周期，随宿主启动/停止
/// </summary>
[Register(typeof(IHostedService), ServiceLifetime.Singleton)]
public sealed partial class BridgeClientHostedService : IHostedService, IAsyncDisposable
{
    private readonly BridgeClient _bridgeClient;
    private readonly BridgeConfig _config;
    private readonly ILogger<BridgeClientHostedService>? _logger;
    private readonly CancellationTokenSource _cts = new();
    private int _disposed;

    /// <summary>
    /// 构造 BridgeClientHostedService
    /// </summary>
    /// <param name="bridgeClient">Bridge 客户端实例</param>
    /// <param name="config">Bridge 配置</param>
    /// <param name="logger">可选日志记录器</param>
    public BridgeClientHostedService(
        BridgeClient bridgeClient,
        BridgeConfig config,
        ILogger<BridgeClientHostedService>? logger = null)
    {
        _bridgeClient = bridgeClient ?? throw new ArgumentNullException(nameof(bridgeClient));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger;
    }

    /// <summary>
    /// 启动托管服务 — 配置禁用时直接返回，否则启动 BridgeClient
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
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

    /// <summary>
    /// 停止托管服务 — 取消内部令牌并停止 BridgeClient
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
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

    /// <summary>
    /// 异步释放资源 — 取消内部令牌并释放 BridgeClient
    /// </summary>
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return ValueTask.CompletedTask;
        _ = _cts.CancelAsync();
        _ = _bridgeClient.DisposeAsync();
        _cts.Dispose();
        return ValueTask.CompletedTask;
    }
}
