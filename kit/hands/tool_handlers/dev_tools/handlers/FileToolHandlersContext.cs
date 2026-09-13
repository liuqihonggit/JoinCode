namespace Tools.Handlers;

/// <summary>
/// 文件工具处理器依赖上下文 — 聚合文件操作所需的各种可选服务和逻辑组件
/// </summary>
[Register(typeof(FileToolHandlersContext), ServiceLifetime.Singleton)]
public sealed record FileToolHandlersContext(
    ISandboxManager? SandboxManager = null,
    ITelemetryService? TelemetryService = null,
    FileEditLogic? FileEditLogic = null,
    SnipLogic? SnipLogic = null,
    IFileStateCache? FileStateCache = null,
    IFileHistoryService? FileHistoryService = null,
    ILspFileSync? LspFileSync = null,
    FileOperationConfig? FileOperationConfig = null,
    ITeamMemSecretGuard? TeamMemSecretGuard = null,
    IFileReadListenerRegistry? FileReadListenerRegistry = null,
    IFileWriteListenerRegistry? FileWriteListenerRegistry = null,
    ILspDiagnosticProvider? LspDiagnosticProvider = null,
    ApplyPatchLogic? ApplyPatchLogic = null,
    ISubAgentContextAccessor? SubAgentContextAccessor = null,
    WriteDefenseService? WriteDefenseService = null)
{
    /// <summary>
    /// 从服务提供者构造文件工具处理器上下文，按需获取各依赖组件
    /// </summary>
    /// <param name="sp">服务提供者</param>
    /// <returns>填充了各可选依赖的上下文实例</returns>
    public static FileToolHandlersContext FromServiceProvider(IServiceProvider sp) => new(
        SandboxManager: sp.GetService<ISandboxManager>(),
        TelemetryService: sp.GetService<ITelemetryService>(),
        FileEditLogic: sp.GetService<FileEditLogic>(),
        SnipLogic: sp.GetService<SnipLogic>(),
        FileStateCache: sp.GetService<IFileStateCache>(),
        FileHistoryService: sp.GetService<IFileHistoryService>(),
        LspFileSync: sp.GetService<ILspFileSync>(),
        FileOperationConfig: sp.GetService<FileOperationConfig>(),
        TeamMemSecretGuard: sp.GetService<ITeamMemSecretGuard>(),
        FileReadListenerRegistry: sp.GetService<IFileReadListenerRegistry>(),
        FileWriteListenerRegistry: sp.GetService<IFileWriteListenerRegistry>(),
        LspDiagnosticProvider: sp.GetService<ILspDiagnosticProvider>(),
        ApplyPatchLogic: sp.GetService<ApplyPatchLogic>(),
        SubAgentContextAccessor: sp.GetService<ISubAgentContextAccessor>(),
        WriteDefenseService: sp.GetService<WriteDefenseService>());
}
