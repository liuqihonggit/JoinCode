namespace Composition.Tests.Commands;

/// <summary>
/// 斜杠命令测试骨架 — 统一 CommandServices/ChatCommandContext 构造与 Console.SetOut 输出捕获
/// <para>必需服务(ChatService/CodeService/PlanService)用 Moq 空实现,可选服务按需注入</para>
/// <para>输出捕获经 Console.SetOut,与命令侧 TerminalHelper.WriteLine 底层同为 Console.Out</para>
/// <para>交互回调(ReadPassword/Prompt)由调用方注入 lambda,模拟真实用户输入形态</para>
/// </summary>
internal static class CommandTestHarness {
    /// <summary>
    /// 构造命令服务包 — 可选服务按需注入,未注入项保持 null 触发命令侧降级分支
    /// </summary>
    /// <param name="fileSystem">文件系统抽象,默认内存实现(0磁盘IO)</param>
    /// <param name="configureDi">DI 注册回调 — 命令通过 GetService 从容器解析的可选服务</param>
    /// <param name="commandRegistry">命令注册表</param>
    /// <param name="permissionManager">权限管理器</param>
    /// <param name="workspaceService">工作区服务</param>
    /// <param name="tokenStorage">OAuth Token 存储</param>
    /// <param name="pkceGenerator">PKCE 生成器</param>
    /// <param name="statusBarData">状态栏数据</param>
    /// <param name="executionSettingsProvider">执行设置提供者</param>
    /// <param name="workflowConfig">工作流配置</param>
    /// <param name="usageTracker">使用量追踪器</param>
    /// <param name="rateLimitTracker">速率限制追踪器</param>
    /// <returns>可直接注入 CommandServiceProvider 的命令服务包</returns>
    internal static CommandServices CreateServices(
        IFileSystem? fileSystem = null,
        Action<IServiceCollection>? configureDi = null,
        ChatCommandRegistry? commandRegistry = null,
        IAgentPermissionManager? permissionManager = null,
        IWorkspaceService? workspaceService = null,
        ITokenStorage? tokenStorage = null,
        IPkceGenerator? pkceGenerator = null,
        StatusBarData? statusBarData = null,
        IExecutionSettingsProvider? executionSettingsProvider = null,
        WorkflowConfig? workflowConfig = null,
        IUsageTracker? usageTracker = null,
        IRateLimitTracker? rateLimitTracker = null) {
        var services = new ServiceCollection();
        configureDi?.Invoke(services);
        return new CommandServices {
            ChatService = Mock.Of<IChatService>(),
            CodeService = Mock.Of<ICodeService>(),
            PlanService = Mock.Of<IPlanService>(),
            FileSystem = fileSystem ?? TestFileSystem.Current,
            ServiceProvider = services.BuildServiceProvider(),
            CommandRegistry = commandRegistry,
            PermissionManager = permissionManager,
            WorkspaceService = workspaceService,
            TokenStorage = tokenStorage,
            PkceGenerator = pkceGenerator,
            StatusBarData = statusBarData,
            ExecutionSettingsProvider = executionSettingsProvider,
            WorkflowConfig = workflowConfig,
            UsageTracker = usageTracker,
            RateLimitTracker = rateLimitTracker,
        };
    }

    /// <summary>
    /// 构造命令执行上下文 — SessionId 默认非空,交互回调按需注入
    /// </summary>
    /// <param name="services">命令服务包</param>
    /// <param name="arguments">命令参数(不含命令名)</param>
    /// <param name="sessionId">会话标识</param>
    /// <param name="prompt">用户输入回调</param>
    /// <param name="readPassword">密码输入回调</param>
    /// <returns>命令执行上下文</returns>
    internal static ChatCommandContext CreateContext(
        CommandServices services,
        string arguments = "",
        string sessionId = "test-session",
        Func<string, string?>? prompt = null,
        Func<string, string?>? readPassword = null) {
        return new ChatCommandContext {
            Arguments = arguments,
            SessionId = sessionId,
            CancellationToken = CancellationToken.None,
            Services = new CommandServiceProvider(services),
            Prompt = prompt,
            ReadPassword = readPassword,
        };
    }

    /// <summary>
    /// 执行命令并捕获控制台输出 — SetOut 重定向 StringWriter,finally 恢复原始输出流
    /// </summary>
    /// <typeparam name="T">命令执行结果类型</typeparam>
    /// <param name="action">待执行的命令调用</param>
    /// <returns>(命令结果, 捕获的输出文本)</returns>
    internal static async Task<(T Result, string Output)> CaptureOutputAsync<T>(Func<Task<T>> action) {
        var output = new StringBuilder();
        var originalOut = Console.Out;
        await using var writer = new StringWriter(output);
        Console.SetOut(writer);
        try {
            var result = await action();
            return (result, output.ToString());
        } finally {
            Console.SetOut(originalOut);
        }
    }
}
