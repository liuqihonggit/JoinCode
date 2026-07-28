using JoinCode.Abstractions.Attributes;

namespace Core.Agents.Coordinator;

/// <summary>
/// AgentCoordinator 核心依赖聚合 — 5 个必需服务
/// </summary>
[Register]
public sealed record AgentCoreDependencies(
    IAgentLifecycleManager LifecycleManager,
    IAgentWorktreeManager WorktreeManager,
    IAgentMessageBroker MessageBroker,
    IAgentExecutionEngine ExecutionEngine,
    AgentStateMachine StateMachine);

/// <summary>
/// AgentCoordinator 权限路由依赖聚合 — 1 个可选服务
/// PermissionRouter 和 PlanApprovalRouter 已移至 SpawnCoordPermissionRoutingMiddleware
/// </summary>
[Register]
public sealed record AgentPermissionDependencies(
    ISwarmPermissionBridge? PermissionBridge = null);

/// <summary>
/// AgentCoordinator 团队管理依赖聚合 — 1 个可选服务
/// TeammateInitService 已移至 SpawnCoordRegisterMessageMiddleware
/// ShellBackgroundTaskService 已移至 DisposeShellTasksMiddleware
/// LayoutManager 已移至 SpawnCoordTeammatePaneMiddleware + DisposePaneMiddleware
/// </summary>
[Register]
public sealed record AgentTeamDependencies(
    JoinCode.Abstractions.Interfaces.ITeammateReconnectService? ReconnectService = null);
