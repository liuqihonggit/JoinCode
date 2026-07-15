using JoinCode.Abstractions.Attributes;

namespace Services.Lsp;

/// <summary>
/// LspService 核心引擎依赖聚合
/// </summary>
[Register]
public sealed partial class LspEngineContext
{
    /// <summary>
    /// LSP 管理器
    /// </summary>
    public ILspManager? LspManager { get; init; }

    /// <summary>
    /// LSP 配置加载器
    /// </summary>
    public ILspConfigLoader? ConfigLoader { get; init; }

    public LspEngineContext() { }

    public LspEngineContext(ILspManager lspManager, ILspConfigLoader configLoader)
    {
        LspManager = lspManager;
        ConfigLoader = configLoader;
    }
}

/// <summary>
/// LspService 可选依赖聚合
/// </summary>
[Register]
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
