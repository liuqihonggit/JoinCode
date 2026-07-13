namespace JoinCode.Entry;

/// <summary>
/// 会话初始化中间件 — 创建 CliSession 并初始化
/// </summary>
[Register]
internal sealed class SessionInitStep : IMiddleware<StartupContext>
{
    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    public async Task InvokeAsync(StartupContext context, MiddlewareDelegate<StartupContext> next, CancellationToken ct)
    {
        var host = context.Host;

        host.Services.GetRequiredService<IPlanModeManager>().CleanupOldPlanFiles();

        var onboardingService = host.Services.GetRequiredService<IOnboardingService>();
        await StartupWorkflow.RunOnboardingIfNeededAsync(onboardingService, context.Options, host.Services.GetRequiredService<IFileSystem>(), context.HasApiKey, host.Services.GetRequiredService<IProviderDefinitionRegistry>(), context.Config);

        var ctxMgr = host.Services.GetRequiredService<IChatContextManager>();
        var smp = host.Services.GetRequiredService<StreamMiddlewarePipeline<ChatMiddlewareContext, ChatStreamEvent>>();
        var cpm = host.Services.GetRequiredService<ChatPromptManager>();
        var ss = host.Services.GetRequiredService<SessionStats>();
        var srm = host.Services.GetRequiredService<Core.Prompts.SystemReminderManager>();
        var fos = host.Services.GetService<IFileOperationService>();
        var rat = host.Services.GetService<IRemoteAgentTaskExecutor>();
        var treg = host.Services.GetService<IToolRegistry>();
        var alm = host.Services.GetService<JoinCode.Abstractions.Interfaces.IAgentLifecycleManager>();
        var wft = host.Services.GetService<IWorkflowTaskExecutor>();
        var mmt = host.Services.GetService<IMonitorMcpTaskExecutor>();
        var lst = host.Services.GetService<ILocalShellTaskExecutor>();
        var ipt = host.Services.GetService<IInProcessTeammateTaskExecutor>();
        var trd = host.Services.GetService<Core.Scheduling.Runtime.TaskRuntimeDeps>();
        var tr = host.Services.GetService<ITaskRuntime>();
        var ts = host.Services.GetService<ITelemetryService>();
        var tds = host.Services.GetService<ITodoService>();
        var tks = host.Services.GetService<ITaskService>();
        var tir = host.Services.GetRequiredService<Core.Prompts.ToolIdleReminderService>();
        var itd = host.Services.GetRequiredService<IdleToolDetector>();
        var cid = host.Services.GetRequiredService<IChatIdleDetector>();
        var ci = host.Services.GetRequiredService<IChatInitializer>();
        var sam = host.Services.GetRequiredService<SessionAdminMiddleware>();
        var ssm = host.Services.GetRequiredService<SessionSaveMiddleware>();
        var amp = host.Services.GetRequiredService<MiddlewarePipeline<ChatAdminContext>>();
        var chatService = host.Services.GetRequiredService<IChatService>();
        var codeService = host.Services.GetRequiredService<ICodeService>();
        var planService = host.Services.GetRequiredService<IPlanService>();
        var toolRegistry = host.Services.GetRequiredService<IToolRegistry>();
        var goalEngine = host.Services.GetService<IGoalEngine>();
        var cronTaskStore = host.Services.GetService<ICronTaskStore>();

        await StartCronGoalBridgeAsync(host.Services, goalEngine, cronTaskStore, ct);

        // 显式触发 CodeIndexService.StartAsync — 构造 AST + 启动 FileWatcher(方案B)
        // 可剥离: 通过 GetService 获取,失败不阻塞启动;性能差时删除此段即可完全剥离
        await StartCodeIndexServiceAsync(host.Services, ct);

        var services = Cli.CliServiceContext.FromServiceProvider(host.Services, goalEngine, cronTaskStore, workflowConfig: context.Config);
        var session = new CliSession(chatService, codeService, planService, toolRegistry, host.Services.GetRequiredService<IFileSystem>(), services);
        await session.InitializeAsync(ct);

        context.Session = session;
        await next(context, ct);
    }

    private static async Task StartCronGoalBridgeAsync(IServiceProvider services, IGoalEngine? goalEngine, ICronTaskStore? cronTaskStore, CancellationToken ct)
    {
        if (goalEngine is null || cronTaskStore is null) return;
        var logger = services.GetService<ILogger<CronGoalBridge>>();
        var bridge = new CronGoalBridge(cronTaskStore, goalEngine, logger);
        await bridge.StartAsync(ct);
    }

    /// <summary>
    /// 显式触发 CodeIndexService.StartAsync — 构造 AST + 启动 FileWatcher(方案B)
    /// 可剥离设计: 通过 GetService 获取,未注册则跳过;异常不抛出,仅记录到 stderr
    /// 性能计时: 输出 [STEP] CodeIndex build done, elapsed=Xms 到 stderr,供 E2E 解析
    /// 跳过条件: 工作目录在 bin/obj/.git/.x 下时不构造 AST(避免并行测试 SQLite 锁竞争 + 这些目录本就无源码)
    /// </summary>
    private static async Task StartCodeIndexServiceAsync(IServiceProvider services, CancellationToken ct)
    {
        var codeIndexService = services.GetService<global::Services.CodeIndex.CodeIndexService>();
        if (codeIndexService is null)
        {
            return;
        }

        // 检测工作目录是否在 bin/obj/.git/.x 下 — 这些目录无源码,跳过 AST 构造
        // 避免并行 E2E 测试时多个 jcc.exe 同时初始化同一 SQLite 数据库导致锁竞争
        var workingDir = Environment.CurrentDirectory;
        if (IsInExcludedDirectory(workingDir))
        {
            return;
        }

        try
        {
            await codeIndexService.StartAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 可剥离: AST 构造失败不阻塞 jcc 启动,仅记录错误
            var logger = services.GetService<ILogger<SessionInitStep>>();
            logger?.LogError(ex, "CodeIndexService.StartAsync failed");
        }
    }

    /// <summary>
    /// 检测路径是否在 bin/obj/.git/.x 目录下(按路径段匹配)
    /// 用于跳过 AST 构造: 这些目录无源码,且并行测试时多个 jcc.exe 同时初始化 SQLite 会导致锁竞争
    /// </summary>
    private static bool IsInExcludedDirectory(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        var excluded = new[] { "bin", "obj", ".git", ".x" };
        var parts = path.Split(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        foreach (var part in parts)
        {
            foreach (var ex in excluded)
            {
                if (string.Equals(part, ex, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        return false;
    }
}
