using JoinCode.Abstractions.Attributes;

namespace Tools.Handlers;

[Register]
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
    ILspDiagnosticProvider? LspDiagnosticProvider = null,
    ApplyPatchLogic? ApplyPatchLogic = null)
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
        LspDiagnosticProvider: sp.GetService<ILspDiagnosticProvider>(),
        ApplyPatchLogic: sp.GetService<ApplyPatchLogic>());
}
