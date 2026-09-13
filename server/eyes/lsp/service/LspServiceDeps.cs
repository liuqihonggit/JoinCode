namespace Services.Lsp;

/// <summary>
/// LspService 核心引擎依赖聚合
/// </summary>
[Register(typeof(LspEngineContext), ServiceLifetime.Singleton)]
public sealed partial class LspEngineContext : ServiceEntity
{
    /// <summary>
    /// LSP 管理器
    /// </summary>
    public ILspManager? LspManager { get; init; }

    /// <summary>
    /// LSP 配置加载器
    /// </summary>
    public ILspConfigLoader? ConfigLoader { get; init; }

    /// <summary>默认构造函数</summary>
    public LspEngineContext() { }

    /// <summary>
    /// 构造 LSP 引擎上下文
    /// </summary>
    /// <param name="lspManager">LSP 管理器</param>
    /// <param name="configLoader">LSP 配置加载器</param>
    public LspEngineContext(ILspManager lspManager, ILspConfigLoader configLoader)
    {
        LspManager = lspManager;
        ConfigLoader = configLoader;
    }
}

/// <summary>
/// LspService 可选依赖聚合
/// </summary>
[Register(typeof(LspServiceDeps), ServiceLifetime.Singleton)]
public sealed record LspServiceDeps(
    IFileOperationService? FileOperationService = null,
    IFileSystem? FileSystem = null,
    ITelemetryService? TelemetryService = null)
{
    /// <summary>
    /// 从 DI 服务提供者解析所有可选依赖
    /// </summary>
    public static LspServiceDeps FromServiceProvider(IServiceProvider sp)
    {
        return new LspServiceDeps(
            FileOperationService: sp.GetService<IFileOperationService>(),
            FileSystem: sp.GetService<IFileSystem>(),
            TelemetryService: sp.GetService<ITelemetryService>());
    }
}
