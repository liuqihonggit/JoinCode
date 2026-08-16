namespace JoinCode.Abstractions.Composition;

using JoinCode.Abstractions.Tools;
using JoinCode.Abstractions.CodeIndex;
using JoinCode.Abstractions.Interfaces;
using JoinCode.Abstractions.Interfaces.Scheduling;
using JoinCode.Abstractions.Configuration.Providers;

/// <summary>
/// 注册中心聚合门面 — 统一访问通用 Registry，减少调用方依赖散落
/// 聚合 Foundation 层通用 Registry，不聚合层特有 Registry（如 IDreamTaskRegistry、ILspDiagnosticRegistry）
/// </summary>
public interface IRegistryHub
{
    /// <summary>工具注册表</summary>
    IToolRegistry Tools { get; }

    /// <summary>目标调度注册表</summary>
    IGoalRegistry Goals { get; }

    /// <summary>系统执行器注册表</summary>
    ISystemActuatorRegistry SystemActuators { get; }

    /// <summary>Agent 角色注册表</summary>
    IAgentRoleRegistry AgentRoles { get; }

    /// <summary>代码索引器注册表</summary>
    ICodeIndexerRegistry CodeIndexers { get; }

    /// <summary>前台任务注册表</summary>
    IForegroundTaskRegistry ForegroundTasks { get; }

    /// <summary>文件读取监听器注册表</summary>
    IFileReadListenerRegistry FileReadListeners { get; }

    /// <summary>命令注册表</summary>
    ICommandRegistry Commands { get; }

    /// <summary>供应商定义注册表</summary>
    IProviderDefinitionRegistry Providers { get; }
}
