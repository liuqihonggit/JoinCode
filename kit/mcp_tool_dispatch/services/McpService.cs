
namespace Core.Services;

/// <summary>
/// MCP 服务实现 — 负责工具注册表的初始化与所有工具处理器的注册
/// </summary>
[Register(typeof(IMcpService), ServiceLifetime.Singleton)]
public sealed partial class McpService : ServiceEntity, IMcpService
{

    /// <summary>
    /// 初始化 MCP 服务
    /// </summary>
    /// <param name="toolRegistry">工具注册表</param>
    /// <param name="logger">日志记录器（可选）</param>
    /// <param name="telemetryService">遥测服务（可选）</param>
    /// <param name="registerAllHandlersFunc">注册所有处理器的委托（可选）；优先使用此委托，否则回退到默认注册</param>
    public McpService(IMcpToolRegistry toolRegistry, ILogger<McpService>? logger = null, ITelemetryService? telemetryService = null, Func<IMcpToolRegistry, IServiceProvider, CancellationToken, Task<IMcpToolRegistry>>? registerAllHandlersFunc = null)
    {
        _toolRegistry = toolRegistry;
        _logger = logger;
        _telemetryService = telemetryService;
        _registerAllHandlersFunc = registerAllHandlersFunc;
    }
    private readonly IMcpToolRegistry _toolRegistry;
    private readonly ILogger<McpService>? _logger;
    private readonly ITelemetryService? _telemetryService;
    private readonly Func<IMcpToolRegistry, IServiceProvider, CancellationToken, Task<IMcpToolRegistry>>? _registerAllHandlersFunc;
    private bool _isInitialized;

    /// <summary>
    /// 获取服务是否正在运行
    /// </summary>
    public bool IsRunning => false;

    /// <summary>
    /// 初始化 MCP 服务，注册所有工具处理器。
    /// 优先使用 DI 注入的注册委托（Composition 根提供，包含所有组件的 Handler），
    /// 否则回退到本程序集的默认注册（仅 McpToolDispatch 项目的 Handler）。
    /// </summary>
    /// <param name="serviceProvider">服务提供者，用于解析处理器依赖</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>表示异步初始化操作的任务</returns>
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
            await _toolRegistry.RegisterAllToolDispatchAsync(serviceProvider, cancellationToken).ConfigureAwait(false);

        _isInitialized = true;
        _logger?.LogInformation(L.T(StringKey.McpServiceInitializedLog));

        _telemetryService?.RecordCount("mcp.service.count", new Dictionary<string, string> { ["operation"] = "initialize", ["success"] = true.ToString() }, "count", "Mcp service count");
    }
}
