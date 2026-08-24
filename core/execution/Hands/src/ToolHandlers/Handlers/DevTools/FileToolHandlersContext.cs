using JoinCode.Abstractions.Attributes;

namespace Tools.Handlers;

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
    ISubAgentContextAccessor? SubAgentContextAccessor = null)
{
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
        SubAgentContextAccessor: sp.GetService<ISubAgentContextAccessor>());
}
