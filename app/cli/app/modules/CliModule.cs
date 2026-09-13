namespace JoinCode.App.Modules;

/// <summary>
/// CLI 模块 — 注册 CLI 专属服务（预览模式等条件注册）
/// </summary>
[AppModule(Order = 80)]
public sealed class CliModule : IAppModule
{
    /// <summary>模块排序权重（80，CLI 专属服务在基础设施之后注册）</summary>
    public int Order => 80;

    /// <summary>注册 CLI 专属服务（交互服务、斜杠命令注册表、命令门面、命令服务聚合）</summary>
    /// <param name="services">服务集合</param>
    /// <param name="context">模块上下文（含命令行选项和配置）</param>
    public void ConfigureServices(IServiceCollection services, AppModuleContext context)
    {
        services.AddSingleton<IInteractiveService, TerminalInteractiveService>();

        // 注册 ChatCommandRegistry — 工厂内完成命令注册
        services.AddSingleton<ChatCommandRegistry>(sp =>
        {
            var registry = new ChatCommandRegistry();
            GeneratedCommandRegistration.RegisterAllChatCommands(registry);
            return registry;
        });
        services.AddSingleton<ISlashCommandRegistry>(sp => sp.GetRequiredService<ChatCommandRegistry>());

        // 注册 ICmdMap 门面 — 解析 ISlashCommandRegistry + IToolRegistry
        services.AddSingleton<ICmdMap>(sp =>
        {
            var slash = sp.GetRequiredService<ISlashCommandRegistry>();
            var mcp = sp.GetRequiredService<IToolRegistry>();
            return new CmdMap(slash, mcp);
        });

        services.AddSingleton<CommandServices>(sp =>
        {
            return new CommandServices
            {
                ChatService = sp.GetRequiredService<IChatService>(),
                CodeService = sp.GetRequiredService<ICodeService>(),
                PlanService = sp.GetRequiredService<IPlanService>(),
                FileSystem = sp.GetRequiredService<IFileSystem>(),
                ServiceProvider = sp,
                ToolRegistry = sp.GetService<IToolRegistry>(),
                CommandRegistry = sp.GetService<ChatCommandRegistry>(),
                GoalEngine = sp.GetService<IGoalEngine>(),
                GoalRegistry = sp.GetService<IGoalRegistry>(),
                CronTaskStore = sp.GetService<ICronTaskStore>(),
                SimpleModeService = sp.GetService<ISimpleModeService>(),
                BriefModeService = sp.GetService<IBriefModeService>(),
                HookConfigurationManager = sp.GetService<IHookConfigurationManager>(),
                PluginManager = sp.GetService<IPluginManager>(),
                BridgeClient = sp.GetService<BridgeClient>(),
                WorkflowConfig = sp.GetService<WorkflowConfig>(),
                ExecutionSettingsProvider = sp.GetService<IExecutionSettingsProvider>(),
                MemoryManagementService = sp.GetService<IMemoryManagementService>(),
                TaskService = sp.GetService<ITaskService>(),
                TodoService = sp.GetService<ITodoService>(),
                UsageTracker = sp.GetService<IUsageTracker>(),
                PermissionManager = sp.GetService<IAgentPermissionManager>(),
                ThinkingStore = sp.GetService<IThinkingStore>(),
                RateLimitTracker = sp.GetService<IRateLimitTracker>(),
                WorkflowTaskExecutor = sp.GetService<IWorkflowTaskExecutor>(),
                CostTracker = sp.GetService<Core.CostTracking.CostTracker>(),
                TokenStorage = sp.GetService<ITokenStorage>(),
                PkceGenerator = sp.GetService<IPkceGenerator>(),
                WorktreeService = sp.GetService<IAgentWorktreeService>(),
                ClipboardService = sp.GetService<IClipboardService>(),
                WorkspaceService = sp.GetService<IWorkspaceService>(),
                FileOperationTracker = sp.GetService<IFileOperationTracker>(),
                SessionTagService = sp.GetService<ISessionTagService>(),
                WebService = sp.GetService<IWebService>(),
            };
        });
    }

    /// <summary>模块异步初始化 — 将 ExposeToMcp=true 的斜杠命令注册为 MCP 工具（通过 SlashToMcpAdapter 包装）</summary>
    /// <param name="services">已构建的服务提供者</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步初始化操作的任务</returns>
    public async Task ConfigureAsync(IServiceProvider services, CancellationToken ct)
    {
        // 将 ExposeToMcp=true 的斜杠命令注册为 MCP 工具（通过 SlashToMcpAdapter 包装）
        // 这样 LLM 能通过现有 MCP 管线发现和调用斜杠命令
        var commandRegistry = services.GetService<ISlashCommandRegistry>();
        var toolRegistry = services.GetService<IToolRegistry>();
        if (commandRegistry is not null && toolRegistry is not null)
        {
            foreach (var kvp in commandRegistry.GetAllCommands())
            {
                if (kvp.Value is ChatCommandBase { ExposeToMcp: true } cmd)
                {
                    var adapter = new SlashToMcpAdapter(cmd, services, cmd.Kind);
                    await toolRegistry.RegisterToolAsync(adapter, ct).ConfigureAwait(false);
                }
            }
        }
    }
}
