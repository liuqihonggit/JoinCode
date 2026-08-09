namespace JoinCode.ChatCommands;

/// <summary>
/// 命令服务容器 — 聚合所有可选服务引用，从 ChatCommandContext 中拆分
/// </summary>
public sealed class CommandServices
{
    /// <summary>DI 服务提供者</summary>
    public IServiceProvider? ServiceProvider { get; init; }

    /// <summary>聊天服务</summary>
    public required IChatService ChatService { get; init; }

    /// <summary>代码服务</summary>
    public required ICodeService CodeService { get; init; }

    /// <summary>计划服务</summary>
    public required IPlanService PlanService { get; init; }

    /// <summary>工具注册表</summary>
    public IToolRegistry? ToolRegistry { get; init; }

    /// <summary>命令注册表</summary>
    public ChatCommandRegistry? CommandRegistry { get; init; }

    /// <summary>成本追踪器</summary>
    public Core.CostTracking.CostTracker? CostTracker { get; init; }

    /// <summary>Worktree 服务</summary>
    public IAgentWorktreeService? WorktreeService { get; init; }

    /// <summary>Token 存储</summary>
    public ITokenStorage? TokenStorage { get; init; }

    /// <summary>PKCE 生成器</summary>
    public IPkceGenerator? PkceGenerator { get; init; }

    /// <summary>目标引擎</summary>
    public IGoalEngine? GoalEngine { get; init; }

    /// <summary>Cron 任务存储</summary>
    public ICronTaskStore? CronTaskStore { get; init; }

    /// <summary>精简模式服务</summary>
    public ISimpleModeService? SimpleModeService { get; init; }

    /// <summary>简报模式服务</summary>
    public IBriefModeService? BriefModeService { get; init; }

    /// <summary>Hook 配置管理器</summary>
    public IHookConfigurationManager? HookConfigurationManager { get; init; }

    /// <summary>状态栏数据</summary>
    public StatusBarData? StatusBarData { get; init; }

    /// <summary>插件管理器</summary>
    public IPluginManager? PluginManager { get; init; }

    /// <summary>Bridge 客户端</summary>
    public BridgeClient? BridgeClient { get; init; }

    /// <summary>工作流配置</summary>
    public WorkflowConfig? WorkflowConfig { get; init; }

    /// <summary>执行设置提供者</summary>
    public IExecutionSettingsProvider? ExecutionSettingsProvider { get; init; }

    /// <summary>记忆管理服务</summary>
    public IMemoryManagementService? MemoryManagementService { get; init; }

    /// <summary>任务服务</summary>
    public ITaskService? TaskService { get; init; }

    /// <summary>待办服务</summary>
    public ITodoService? TodoService { get; init; }

    /// <summary>使用量追踪器</summary>
    public IUsageTracker? UsageTracker { get; init; }

    /// <summary>权限管理器</summary>
    public IAgentPermissionManager? PermissionManager { get; init; }

    /// <summary>思考存储</summary>
    public IThinkingStore? ThinkingStore { get; init; }

    /// <summary>速率限制追踪器</summary>
    public IRateLimitTracker? RateLimitTracker { get; init; }

    /// <summary>工作流执行器</summary>
    public IWorkflowTaskExecutor? WorkflowTaskExecutor { get; init; }

    /// <summary>剪贴板服务</summary>
    public IClipboardService? ClipboardService { get; init; }

    /// <summary>工作区服务</summary>
    public IWorkspaceService? WorkspaceService { get; init; }

    /// <summary>文件操作追踪器</summary>
    public IFileOperationTracker? FileOperationTracker { get; init; }

    /// <summary>Turn Diff 提供者</summary>
    public ITurnDiffProvider? TurnDiffProvider { get; init; }

    /// <summary>会话标签服务</summary>
    public ISessionTagService? SessionTagService { get; init; }

    /// <summary>Web 服务</summary>
    public IWebService? WebService { get; init; }

    /// <summary>文件系统抽象</summary>
    public required IFileSystem FileSystem { get; init; }
}
