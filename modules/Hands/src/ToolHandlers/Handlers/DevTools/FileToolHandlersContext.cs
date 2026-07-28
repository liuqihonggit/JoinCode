using JoinCode.Abstractions.Attributes;

namespace Tools.Handlers;

/// <summary>
/// FileToolHandlers 可选服务上下文 — 聚合所有 nullable 可选依赖，减少构造函数参数数量
/// </summary>
[Register]
public sealed record FileToolHandlersContext(
    IScratchpadSandbox? ScratchpadSandbox = null,
    ITelemetryService? TelemetryService = null,
    FileEditLogic? FileEditLogic = null,
    SnipLogic? SnipLogic = null,
    IFileStateCache? FileStateCache = null,
    IFileHistoryService? FileHistoryService = null,
    ILspFileSync? LspFileSync = null,
    FileOperationConfig? FileOperationConfig = null,
    ITeamMemSecretGuard? TeamMemSecretGuard = null,
    IFileReadListenerRegistry? FileReadListenerRegistry = null,
    ILspDiagnosticProvider? LspDiagnosticProvider = null)
{
    /// <summary>
    /// 从 DI 容器解析所有可选服务
    /// </summary>
    public static FileToolHandlersContext FromServiceProvider(IServiceProvider sp) => new(
        ScratchpadSandbox: sp.GetService<IScratchpadSandbox>(),
        TelemetryService: sp.GetService<ITelemetryService>(),
        FileEditLogic: sp.GetService<FileEditLogic>(),
        SnipLogic: sp.GetService<SnipLogic>(),
        FileStateCache: sp.GetService<IFileStateCache>(),
        FileHistoryService: sp.GetService<IFileHistoryService>(),
        LspFileSync: sp.GetService<ILspFileSync>(),
        FileOperationConfig: sp.GetService<FileOperationConfig>(),
        TeamMemSecretGuard: sp.GetService<ITeamMemSecretGuard>(),
        FileReadListenerRegistry: sp.GetService<IFileReadListenerRegistry>(),
        LspDiagnosticProvider: sp.GetService<ILspDiagnosticProvider>());
}
