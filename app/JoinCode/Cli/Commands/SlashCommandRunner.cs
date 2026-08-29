namespace JoinCode.Cli.Commands;

/// <summary>
/// 斜杠命令执行结果。
/// </summary>
/// <param name="Handled">命令是否被识别并执行（false = 未知命令）。</param>
/// <param name="ShouldContinue">false 表示命令要求退出程序（如 /exit）。</param>
/// <param name="Output">命令输出文本（Console.Out 捕获），可能为空串。</param>
public sealed record SlashCommandResult(bool Handled, bool ShouldContinue, string Output)
{
    /// <summary>未知命令的快捷构造。</summary>
    public static SlashCommandResult Unknown(string commandName) =>
        new(Handled: false, ShouldContinue: true,
            Output: $"未知命令: /{commandName}（输入 /help 查看可用命令）");
}

/// <summary>
/// 斜杠命令执行器 — TUI/GUI 共享的唯一执行链路（消除两套实现）：
/// 解析 → CmdMap 路由（斜杠命令→MCP 工具）→ ChatCommandContext 构造 → Console.Out 捕获回显。
/// UI 差异通过回调注入：TUI 传权限对话框 Confirm，GUI 可传弹窗回调，均可为 null（默认拒绝/空输入）。
/// 线程安全：静态无状态；RunAsync 内部串行重定向 Console.Out，调用方需自行保证不并发执行命令
/// （与 TUI/GUI 的单命令队列语义一致）。
/// </summary>
public static class SlashCommandRunner
{
    /// <summary>
    /// 执行斜杠命令。
    /// </summary>
    /// <param name="input">以 / 开头的完整命令输入。</param>
    /// <param name="services">引擎 DI 容器（提供 ChatCommandRegistry/IToolRegistry 及 CommandServices 所需服务）。</param>
    /// <param name="clearScreen">ClearScreen 回调（可空）。</param>
    /// <param name="confirm">Confirm 回调，返回 true 表示确认（可空，默认拒绝）。</param>
    /// <param name="prompt">Prompt 自由输入回调（可空，默认 null）。</param>
    /// <param name="readPassword">密码输入回调（可空，默认空串）。</param>
    /// <param name="onExitRequested">命令要求退出程序时回调（如 /exit；可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public static async Task<SlashCommandResult> RunAsync(
        string input,
        IServiceProvider services,
        Action? clearScreen = null,
        Func<string, bool>? confirm = null,
        Func<string, string?>? prompt = null,
        Func<string, string?>? readPassword = null,
        Action? onExitRequested = null,
        CancellationToken cancellationToken = default)
    {
        var commandRegistry = services.GetService<ChatCommandRegistry>();
        if (commandRegistry is null)
        {
            // DI 未注册命令注册表时（如测试/精简宿主），用本程序集生成的全量清单兜底
            commandRegistry = new ChatCommandRegistry();
            GeneratedCommandRegistration.RegisterAllChatCommands(commandRegistry);
        }
        var toolRegistry = services.GetService<IToolRegistry>();
        var cmdMap = toolRegistry is not null ? new CmdMap(commandRegistry, toolRegistry) : null;

        var parseResult = commandRegistry.Parse(input);
        if (!parseResult.IsSuccess)
            return new SlashCommandResult(true, true, $"解析失败: {parseResult.ErrorMessage}");

        var commandName = parseResult.CommandName;
        if (commandName is null)
            return new SlashCommandResult(false, true, string.Empty);

        // 路由 — 先查斜杠命令，再查 MCP 工具
        CmdDescriptor? descriptor = null;
        if (cmdMap is not null)
        {
            try
            {
                descriptor = await cmdMap.ResolveAsync(commandName, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return new SlashCommandResult(true, true, $"命令路由失败: {ex.Message}");
            }
        }

        var command = descriptor?.SlashCommand;
        if (command is null)
            return SlashCommandResult.Unknown(commandName);

        var context = new ChatCommandContext
        {
            Arguments = parseResult.Arguments,
            CancellationToken = cancellationToken,
            Services = BuildCommandServiceProvider(services, commandRegistry, toolRegistry),
            ClearScreen = clearScreen,
            Confirm = confirm,
            Prompt = prompt,
            ReadPassword = readPassword,
        };

        // 捕获命令输出 — 重定向 Console.Out 到 StringBuilder（对齐 CLI/TUI 既有行为）
        var commandOutput = new StringBuilder();
        var originalOut = System.Console.Out;
        using var commandWriter = new System.IO.StringWriter(commandOutput);
        var shouldContinue = true;
        try
        {
            System.Console.SetOut(commandWriter);
            var result = await command.ExecuteAsync(context).ConfigureAwait(false);
            shouldContinue = result.ShouldContinue;
        }
        catch (Exception ex)
        {
            return new SlashCommandResult(true, true, $"命令执行失败: {ex.Message}");
        }
        finally
        {
            System.Console.SetOut(originalOut);
            commandWriter.Flush();
        }

        if (!shouldContinue)
            onExitRequested?.Invoke();

        return new SlashCommandResult(true, shouldContinue, commandOutput.ToString().TrimEnd());
    }

    /// <summary>
    /// 构造命令服务包 — 从 DI 解析 CommandServices 必需依赖并包装为轻量 IServiceProvider。
    /// 缺少必需服务时抛出含明确指引的异常（而非 NRE）。
    /// </summary>
    private static IServiceProvider BuildCommandServiceProvider(
        IServiceProvider services,
        ChatCommandRegistry commandRegistry,
        IToolRegistry? toolRegistry)
    {
        var chatService = services.GetService<IChatService>()
            ?? throw new InvalidOperationException("引擎未就绪：DI 中缺少 IChatService，无法执行斜杠命令");
        var codeService = services.GetService<ICodeService>()
            ?? throw new InvalidOperationException("引擎未就绪：DI 中缺少 ICodeService，无法执行斜杠命令");
        var planService = services.GetService<IPlanService>()
            ?? throw new InvalidOperationException("引擎未就绪：DI 中缺少 IPlanService，无法执行斜杠命令");
        var fs = services.GetService<IFileSystem>()
            ?? IO.FileSystem.FileSystemFactory.Create();

        var commandServices = new CommandServices
        {
            ChatService = chatService,
            CodeService = codeService,
            PlanService = planService,
            FileSystem = fs,
            ServiceProvider = services,
            ToolRegistry = toolRegistry,
            CommandRegistry = commandRegistry,
            GoalEngine = services.GetService<IGoalEngine>(),
            GoalRegistry = services.GetService<IGoalRegistry>(),
            CronTaskStore = services.GetService<ICronTaskStore>(),
            SimpleModeService = services.GetService<ISimpleModeService>(),
            BriefModeService = services.GetService<IBriefModeService>(),
            HookConfigurationManager = services.GetService<IHookConfigurationManager>(),
            PluginManager = services.GetService<IPluginManager>(),
            WorkflowConfig = services.GetService<WorkflowConfig>(),
            ExecutionSettingsProvider = services.GetService<IExecutionSettingsProvider>(),
            MemoryManagementService = services.GetService<IMemoryManagementService>(),
            TaskService = services.GetService<ITaskService>(),
            TodoService = services.GetService<ITodoService>(),
            UsageTracker = services.GetService<IUsageTracker>(),
            PermissionManager = services.GetService<IAgentPermissionManager>(),
            ThinkingStore = services.GetService<IThinkingStore>(),
            RateLimitTracker = services.GetService<IRateLimitTracker>(),
            WorkflowTaskExecutor = services.GetService<IWorkflowTaskExecutor>(),
            ClipboardService = services.GetService<IClipboardService>(),
            WorkspaceService = services.GetService<IWorkspaceService>(),
            FileOperationTracker = services.GetService<IFileOperationTracker>(),
            SessionTagService = services.GetService<ISessionTagService>(),
            WebService = services.GetService<IWebService>(),
            CostTracker = services.GetService<Core.CostTracking.CostTracker>(),
            TokenStorage = services.GetService<ITokenStorage>(),
            PkceGenerator = services.GetService<IPkceGenerator>(),
            WorktreeService = services.GetService<IAgentWorktreeService>(),
            BridgeClient = services.GetService<BridgeClient>(),
        };

        return new CommandServiceProvider(commandServices, services);
    }
}
