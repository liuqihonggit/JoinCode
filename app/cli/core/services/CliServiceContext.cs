namespace JoinCode.Cli;

/// <summary>
/// CLI 服务上下文 — 聚合所有可选服务引用，替代 TuiServiceContext
/// </summary>
public sealed record CliServiceContext
{
    /// <summary>目标引擎 — 负责目标分解与执行调度</summary>
    public IGoalEngine? GoalEngine { get; init; }
    /// <summary>目标注册表 — 管理已注册的目标定义</summary>
    public IGoalRegistry? GoalRegistry { get; init; }
    /// <summary>定时任务存储 — 持久化 cron 定时任务</summary>
    public ICronTaskStore? CronTaskStore { get; init; }
    /// <summary>Dream 任务注册表 — 管理后台 Dream 任务</summary>
    public JoinCode.Dream.Persistence.IDreamTaskRegistry? DreamTaskRegistry { get; init; }
    /// <summary>桥接客户端 — 与外部进程通信的桥接层</summary>
    public BridgeClient? BridgeClient { get; init; }
    /// <summary>工作流配置 — 工作流执行参数与策略</summary>
    public WorkflowConfig? WorkflowConfig { get; init; }
    /// <summary>简单模式服务 — 简化交互模式下的会话处理</summary>
    public ISimpleModeService? SimpleModeService { get; init; }
    /// <summary>简洁模式服务 — 精简输出模式下的会话处理</summary>
    public IBriefModeService? BriefModeService { get; init; }
    /// <summary>会话记录服务 — 转录对话历史到外部存储</summary>
    public ITranscriptService? TranscriptService { get; init; }
    /// <summary>快速模式服务 — 低延迟响应模式下的会话处理</summary>
    public IFastModeService? FastModeService { get; init; }
    /// <summary>钩子配置管理器 — 管理生命周期钩子的配置与调度</summary>
    public IHookConfigurationManager? HookConfigurationManager { get; init; }
    /// <summary>插件管理器 — 加载、卸载与调度插件</summary>
    public IPluginManager? PluginManager { get; init; }
    /// <summary>执行设置提供者 — 提供当前执行环境的运行时设置</summary>
    public IExecutionSettingsProvider? ExecutionSettingsProvider { get; init; }
    /// <summary>内存管理服务 — 上下文压缩与记忆回收</summary>
    public IMemoryManagementService? MemoryManagementService { get; init; }
    /// <summary>任务服务 — 任务创建、查询与状态流转</summary>
    public ITaskService? TaskService { get; init; }
    /// <summary>待办服务 — 待办事项的增删改查</summary>
    public ITodoService? TodoService { get; init; }
    /// <summary>用量跟踪器 — 统计 token 与请求用量</summary>
    public IUsageTracker? UsageTracker { get; init; }
    /// <summary>权限管理器 — 代理工具调用权限的授予与校验</summary>
    public IAgentPermissionManager? PermissionManager { get; init; }
    /// <summary>服务提供者 — DI 容器回退解析的入口</summary>
    public IServiceProvider? ServiceProvider { get; init; }
    /// <summary>思考存储 — 模型推理思考过程的暂存区</summary>
    public IThinkingStore? ThinkingStore { get; init; }
    /// <summary>速率限制跟踪器 — 跟踪并强制执行 API 速率限制</summary>
    public IRateLimitTracker? RateLimitTracker { get; init; }
    /// <summary>工作流任务执行器 — 执行工作流中的单个任务节点</summary>
    public IWorkflowTaskExecutor? WorkflowTaskExecutor { get; init; }
    /// <summary>剪贴板服务 — 系统剪贴板的读写访问</summary>
    public IClipboardService? ClipboardService { get; init; }
    /// <summary>工作区服务 — 当前工作区路径与配置管理</summary>
    public IWorkspaceService? WorkspaceService { get; init; }
    /// <summary>文件操作跟踪器 — 记录文件读写与变更操作</summary>
    public IFileOperationTracker? FileOperationTracker { get; init; }
    /// <summary>会话标签服务 — 会话标签的增删改与查询</summary>
    public ISessionTagService? SessionTagService { get; init; }

    /// <summary>
    /// 从 DI 容器解析所有可选服务
    /// </summary>
    public static CliServiceContext FromServiceProvider(
        IServiceProvider? sp,
        IGoalEngine? goalEngine = null,
        IGoalRegistry? goalRegistry = null,
        ICronTaskStore? cronTaskStore = null,
        BridgeClient? bridgeClient = null,
        WorkflowConfig? workflowConfig = null) => new()
        {
            GoalEngine = goalEngine,
            GoalRegistry = goalRegistry ?? sp?.GetService<IGoalRegistry>(),
            CronTaskStore = cronTaskStore,
            DreamTaskRegistry = sp?.GetService<JoinCode.Dream.Persistence.IDreamTaskRegistry>(),
            BridgeClient = bridgeClient,
            WorkflowConfig = workflowConfig,
            SimpleModeService = sp?.GetService<ISimpleModeService>(),
            BriefModeService = sp?.GetService<IBriefModeService>(),
            TranscriptService = sp?.GetService<ITranscriptService>(),
            FastModeService = sp?.GetService<IFastModeService>(),
            HookConfigurationManager = sp?.GetService<IHookConfigurationManager>(),
            PluginManager = sp?.GetService<IPluginManager>(),
            ExecutionSettingsProvider = sp?.GetService<IExecutionSettingsProvider>(),
            MemoryManagementService = sp?.GetService<IMemoryManagementService>(),
            TaskService = sp?.GetService<ITaskService>(),
            TodoService = sp?.GetService<ITodoService>(),
            UsageTracker = sp?.GetService<IUsageTracker>(),
            PermissionManager = sp?.GetService<IAgentPermissionManager>(),
            ServiceProvider = sp,
            ThinkingStore = sp?.GetService<IThinkingStore>(),
            RateLimitTracker = sp?.GetService<IRateLimitTracker>(),
            WorkflowTaskExecutor = sp?.GetService<IWorkflowTaskExecutor>(),
            ClipboardService = sp?.GetService<IClipboardService>(),
            WorkspaceService = sp?.GetService<IWorkspaceService>(),
            FileOperationTracker = sp?.GetService<IFileOperationTracker>(),
            SessionTagService = sp?.GetService<ISessionTagService>(),
        };
}
