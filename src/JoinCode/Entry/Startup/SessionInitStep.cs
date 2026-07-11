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
        Console.Error.WriteLine("[STEP] SessionInit start");
        var host = context.Host;

        Console.Error.WriteLine("[STEP] SessionInit CleanupOldPlanFiles...");
        host.Services.GetRequiredService<IPlanModeManager>().CleanupOldPlanFiles();
        Console.Error.WriteLine("[STEP] SessionInit CleanupOldPlanFiles done");

        Console.Error.WriteLine("[STEP] SessionInit RunOnboardingIfNeededAsync...");
        var onboardingService = host.Services.GetRequiredService<IOnboardingService>();
        await StartupWorkflow.RunOnboardingIfNeededAsync(onboardingService, context.Options, host.Services.GetRequiredService<IFileSystem>(), context.HasApiKey, context.Config);
        Console.Error.WriteLine("[STEP] SessionInit RunOnboardingIfNeededAsync done");

        Console.Error.WriteLine("[STEP] SessionInit resolving services...");
        // 诊断: 逐个解析 IChatService 的依赖,定位阻塞点
        Console.Error.WriteLine("[STEP] SessionInit resolving IChatContextManager...");
        var ctxMgr = host.Services.GetRequiredService<IChatContextManager>();
        Console.Error.WriteLine("[STEP] SessionInit resolving StreamMiddlewarePipeline...");
        var smp = host.Services.GetRequiredService<StreamMiddlewarePipeline<ChatMiddlewareContext, ChatStreamEvent>>();
        Console.Error.WriteLine("[STEP] SessionInit resolving MiddlewarePipeline<ChatAdminContext>...");
        // 诊断: 先解析管道的中间件,定位哪个中间件阻塞
        Console.Error.WriteLine("[STEP] SessionInit resolving SessionAdminMiddleware...");
        // 诊断: 先解析 SessionAdminMiddleware 的依赖,定位哪个阻塞
        Console.Error.WriteLine("[STEP] SessionInit resolving ChatPromptManager...");
        var cpm = host.Services.GetRequiredService<ChatPromptManager>();
        Console.Error.WriteLine("[STEP] SessionInit resolving SessionStats...");
        var ss = host.Services.GetRequiredService<SessionStats>();
        Console.Error.WriteLine("[STEP] SessionInit resolving IChatIdleDetector...");
        // 诊断: 逐个解析 ChatIdleDetector 的依赖
        Console.Error.WriteLine("[STEP] SessionInit resolving SystemReminderManager...");
        var srm = host.Services.GetRequiredService<Core.Prompts.SystemReminderManager>();
        Console.Error.WriteLine("[STEP] SessionInit resolving ToolIdleReminderService...");
        // 诊断: ToolIdleReminderService 的可选依赖可能阻塞
        Console.Error.WriteLine("[STEP] SessionInit resolving ITodoService (optional)...");
        // 诊断: TodoService 的可选依赖可能阻塞
        Console.Error.WriteLine("[STEP] SessionInit resolving ITaskRuntime (optional)...");
        // 诊断: TaskRuntimeDeps 有6个可选依赖，逐个解析定位阻塞
        Console.Error.WriteLine("[STEP] SessionInit resolving IFileOperationService...");
        var fos = host.Services.GetService<IFileOperationService>();
        Console.Error.WriteLine("[STEP] SessionInit resolving IRemoteAgentTaskExecutor...");
        var rat = host.Services.GetService<IRemoteAgentTaskExecutor>();
        Console.Error.WriteLine("[STEP] SessionInit resolving IWorkflowTaskExecutor...");
        // 诊断: WorkflowTaskExecutor 依赖 IToolRegistry 和 IAgentLifecycleManager
        Console.Error.WriteLine("[STEP] SessionInit resolving IToolRegistry...");
        var treg = host.Services.GetService<IToolRegistry>();
        Console.Error.WriteLine("[STEP] SessionInit resolving IAgentLifecycleManager...");
        var alm = host.Services.GetService<JoinCode.Abstractions.Interfaces.IAgentLifecycleManager>();
        var wft = host.Services.GetService<IWorkflowTaskExecutor>();
        Console.Error.WriteLine("[STEP] SessionInit resolving IMonitorMcpTaskExecutor...");
        var mmt = host.Services.GetService<IMonitorMcpTaskExecutor>();
        Console.Error.WriteLine("[STEP] SessionInit resolving ILocalShellTaskExecutor...");
        var lst = host.Services.GetService<ILocalShellTaskExecutor>();
        Console.Error.WriteLine("[STEP] SessionInit resolving IInProcessTeammateTaskExecutor...");
        var ipt = host.Services.GetService<IInProcessTeammateTaskExecutor>();
        Console.Error.WriteLine("[STEP] SessionInit resolving TaskRuntimeDeps (deps OK)...");
        var trd = host.Services.GetService<Core.Scheduling.Runtime.TaskRuntimeDeps>();
        var tr = host.Services.GetService<ITaskRuntime>();
        Console.Error.WriteLine("[STEP] SessionInit resolving ITelemetryService (optional)...");
        var ts = host.Services.GetService<ITelemetryService>();
        var tds = host.Services.GetService<ITodoService>();
        Console.Error.WriteLine("[STEP] SessionInit resolving ITaskService (optional)...");
        var tks = host.Services.GetService<ITaskService>();
        var tir = host.Services.GetRequiredService<Core.Prompts.ToolIdleReminderService>();
        Console.Error.WriteLine("[STEP] SessionInit resolving IdleToolDetector...");
        var itd = host.Services.GetRequiredService<IdleToolDetector>();
        Console.Error.WriteLine("[STEP] SessionInit resolving ChatIdleDetector (deps OK)...");
        var cid = host.Services.GetRequiredService<IChatIdleDetector>();
        Console.Error.WriteLine("[STEP] SessionInit resolving IChatInitializer...");
        var ci = host.Services.GetRequiredService<IChatInitializer>();
        Console.Error.WriteLine("[STEP] SessionInit resolving SessionAdminMiddleware (deps OK)...");
        var sam = host.Services.GetRequiredService<SessionAdminMiddleware>();
        Console.Error.WriteLine("[STEP] SessionInit resolving SessionSaveMiddleware...");
        var ssm = host.Services.GetRequiredService<SessionSaveMiddleware>();
        var amp = host.Services.GetRequiredService<MiddlewarePipeline<ChatAdminContext>>();
        Console.Error.WriteLine("[STEP] SessionInit resolving IChatService...");
        Console.Error.WriteLine("[STEP] SessionInit GetRequiredService<IChatService> start...");
        var chatService = host.Services.GetRequiredService<IChatService>();
        Console.Error.WriteLine("[STEP] SessionInit GetRequiredService<IChatService> done");
        Console.Error.WriteLine("[STEP] SessionInit resolving ICodeService...");
        Console.Error.WriteLine("[STEP] SessionInit GetRequiredService<ICodeService> start...");
        var codeService = host.Services.GetRequiredService<ICodeService>();
        Console.Error.WriteLine("[STEP] SessionInit GetRequiredService<ICodeService> done");
        Console.Error.WriteLine("[STEP] SessionInit resolving IPlanService...");
        Console.Error.WriteLine("[STEP] SessionInit GetRequiredService<IPlanService> start...");
        var planService = host.Services.GetRequiredService<IPlanService>();
        Console.Error.WriteLine("[STEP] SessionInit GetRequiredService<IPlanService> done");
        Console.Error.WriteLine("[STEP] SessionInit resolving IToolRegistry...");
        var toolRegistry = host.Services.GetRequiredService<IToolRegistry>();
        Console.Error.WriteLine("[STEP] SessionInit resolving IGoalEngine (optional)...");
        var goalEngine = host.Services.GetService<IGoalEngine>();
        Console.Error.WriteLine("[STEP] SessionInit resolving ICronTaskStore (optional)...");
        Console.Error.WriteLine("[STEP] SessionInit GetService<ICronTaskStore> start...");
        var cronTaskStore = host.Services.GetService<ICronTaskStore>();
        Console.Error.WriteLine("[STEP] SessionInit GetService<ICronTaskStore> done");
        Console.Error.WriteLine("[STEP] SessionInit services resolved");

        Console.Error.WriteLine("[STEP] SessionInit StartCronGoalBridgeAsync...");
        await StartCronGoalBridgeAsync(host.Services, goalEngine, cronTaskStore, ct);
        Console.Error.WriteLine("[STEP] SessionInit StartCronGoalBridgeAsync done");

        // 显式触发 CodeIndexService.StartAsync — 构造 AST + 启动 FileWatcher(方案B)
        // 可剥离: 通过 GetService 获取,失败不阻塞启动;性能差时删除此段即可完全剥离
        await StartCodeIndexServiceAsync(host.Services, ct);

        Console.Error.WriteLine("[STEP] SessionInit creating CliSession...");
        var services = Cli.CliServiceContext.FromServiceProvider(host.Services, goalEngine, cronTaskStore, workflowConfig: context.Config);
        var session = new CliSession(chatService, codeService, planService, toolRegistry, host.Services.GetRequiredService<IFileSystem>(), services);
        Console.Error.WriteLine("[STEP] SessionInit calling session.InitializeAsync...");
        await session.InitializeAsync(ct);
        Console.Error.WriteLine("[STEP] SessionInit InitializeAsync done");

        context.Session = session;
        Console.Error.WriteLine("[STEP] SessionInit done, calling next");
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
        Console.Error.WriteLine("[STEP] SessionInit CodeIndexService resolving...");
        var codeIndexService = services.GetService<global::Services.CodeIndex.CodeIndexService>();
        if (codeIndexService is null)
        {
            Console.Error.WriteLine("[STEP] SessionInit CodeIndexService not registered, skipped");
            return;
        }

        // 检测工作目录是否在 bin/obj/.git/.x 下 — 这些目录无源码,跳过 AST 构造
        // 避免并行 E2E 测试时多个 jcc.exe 同时初始化同一 SQLite 数据库导致锁竞争
        var workingDir = Environment.CurrentDirectory;
        if (IsInExcludedDirectory(workingDir))
        {
            Console.Error.WriteLine($"[STEP] SessionInit CodeIndexService skipped (working dir in bin/obj/.git/.x): {workingDir}");
            return;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        Console.Error.WriteLine("[STEP] SessionInit CodeIndexService.StartAsync start...");
        try
        {
            await codeIndexService.StartAsync(ct).ConfigureAwait(false);
            sw.Stop();
            Console.Error.WriteLine($"[STEP] SessionInit CodeIndexService.StartAsync done, elapsed={sw.ElapsedMilliseconds}ms");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            sw.Stop();
            Console.Error.WriteLine($"[STEP] SessionInit CodeIndexService.StartAsync cancelled, elapsed={sw.ElapsedMilliseconds}ms");
            throw;
        }
        catch (Exception ex)
        {
            // 可剥离: AST 构造失败不阻塞 jcc 启动,仅记录错误
            sw.Stop();
            Console.Error.WriteLine($"[STEP] SessionInit CodeIndexService.StartAsync FAILED, elapsed={sw.ElapsedMilliseconds}ms, error={ex.GetType().Name}: {ex.Message}");
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
