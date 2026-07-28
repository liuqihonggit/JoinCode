
namespace Core.Services;

[Register]
public sealed partial class McpService : IMcpService
{
    [Inject] private readonly IMcpToolRegistry _toolRegistry;
    [Inject] private readonly ILogger<McpService>? _logger;
    [Inject] private readonly ITelemetryService? _telemetryService;
    [Inject] private readonly Func<IMcpToolRegistry, IServiceProvider, CancellationToken, Task<IMcpToolRegistry>>? _registerAllHandlersFunc;
    private bool _isInitialized;

    public bool IsRunning => false;

    /// <summary>
    /// 初始化 MCP 服务，注册所有工具处理器。
    /// 优先使用 DI 注入的注册委托（Composition 根提供，包含所有组件的 Handler），
    /// 否则回退到本程序集的默认注册（仅 McpToolHandlers 项目的 Handler）。
    /// </summary>
    public async Task InitializeAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        if (_isInitialized)
        {
            _logger?.LogDebug(L.T(StringKey.McpServiceAlreadyInitializedLog));
            return;
        }

        _logger?.LogInformation(L.T(StringKey.McpServiceRegisteringLog));

        if (_registerAllHandlersFunc is not null)
            await _registerAllHandlersFunc(_toolRegistry, serviceProvider, cancellationToken).ConfigureAwait(false);
        else
            await _toolRegistry.RegisterAllToolHandlersAsync(serviceProvider, cancellationToken).ConfigureAwait(false);

        _isInitialized = true;
        _logger?.LogInformation(L.T(StringKey.McpServiceInitializedLog));

        _telemetryService?.RecordCount("mcp.service.count", new Dictionary<string, string> { ["operation"] = "initialize", ["success"] = true.ToString() }, "count", "Mcp service count");
    }
}
