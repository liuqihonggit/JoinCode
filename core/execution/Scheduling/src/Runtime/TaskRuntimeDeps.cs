namespace Core.Scheduling.Runtime;

using JoinCode.Abstractions.Attributes;

/// <summary>
/// TaskRuntime 可选依赖聚合
/// </summary>
[Register]
public sealed record TaskRuntimeDeps(
    IFileOperationService? FileOperationService = null,
    string? PersistenceDirectory = null,
    IRemoteAgentTaskExecutor? RemoteAgentTaskExecutor = null,
    IWorkflowTaskExecutor? WorkflowTaskExecutor = null,
    IMonitorMcpTaskExecutor? MonitorMcpTaskExecutor = null,
    ILocalShellTaskExecutor? LocalShellTaskExecutor = null,
    IInProcessTeammateTaskExecutor? InProcessTeammateTaskExecutor = null);
