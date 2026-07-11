namespace JoinCode.Cli;

/// <summary>
/// CLI 服务上下文 — 聚合所有可选服务引用，替代 TuiServiceContext
/// </summary>
public sealed record CliServiceContext
{
    public IGoalEngine? GoalEngine { get; init; }
    public ICronTaskStore? CronTaskStore { get; init; }
    public BridgeClient? BridgeClient { get; init; }
    public WorkflowConfig? WorkflowConfig { get; init; }
    public ISimpleModeService? SimpleModeService { get; init; }
    public IBriefModeService? BriefModeService { get; init; }
    public ITranscriptService? TranscriptService { get; init; }
    public IFastModeService? FastModeService { get; init; }
    public IHookConfigurationManager? HookConfigurationManager { get; init; }
    public IPluginManager? PluginManager { get; init; }
    public IExecutionSettingsProvider? ExecutionSettingsProvider { get; init; }
    public IMemoryManagementService? MemoryManagementService { get; init; }
    public ITaskService? TaskService { get; init; }
    public ITodoService? TodoService { get; init; }
    public IUsageTracker? UsageTracker { get; init; }
    public IAgentPermissionManager? PermissionManager { get; init; }
    public IServiceProvider? ServiceProvider { get; init; }
    public IThinkingStore? ThinkingStore { get; init; }
    public IRateLimitTracker? RateLimitTracker { get; init; }
    public IWorkflowTaskExecutor? WorkflowTaskExecutor { get; init; }
    public IClipboardService? ClipboardService { get; init; }
    public IWorkspaceService? WorkspaceService { get; init; }
    public IFileOperationTracker? FileOperationTracker { get; init; }
    public ISessionTagService? SessionTagService { get; init; }

    /// <summary>
    /// 从 DI 容器解析所有可选服务
    /// </summary>
    public static CliServiceContext FromServiceProvider(
        IServiceProvider? sp,
        IGoalEngine? goalEngine = null,
        ICronTaskStore? cronTaskStore = null,
        BridgeClient? bridgeClient = null,
        WorkflowConfig? workflowConfig = null) => new()
        {
            GoalEngine = goalEngine,
            CronTaskStore = cronTaskStore,
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
