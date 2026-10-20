namespace JoinCode.App.Builder;

/// <summary>
/// CLI 宿主命令工具 — 参数解析/帮助输出/版本/子命令路由（ADR 0143 宿主部分，引擎构建已拆至 EngineApplicationBuilder）
/// </summary>
public static class ApplicationBuilder {
    #region 基础设施方法 — 供 Main 直接调用

    /// <summary>
    /// 判断参数是否为子命令
    /// </summary>
    public static bool IsSubCommand(string arg) =>
        CliSubCommandExtensions.FromValue(arg) is not null;

    /// <summary>
    /// 执行子命令
    /// </summary>
    public static async Task<int> RunSubCommandAsync(string[] args) {
        var subCommand = CliSubCommandExtensions.FromValue(args[0]);

        if (subCommand is CliSubCommand.RemoteControl or CliSubCommand.Rc or CliSubCommand.Remote) {
            var bridgeFs = IO.FileSystem.FileSystemFactory.Create();
            var bridgeProcessService = new IO.ProcessService.PhysicalProcessService(
                new IO.ProcessService.ProcessStartInfoBuilder(new IO.ProcessService.ProcessEncodingProvider()));

            // 构建 Bridge Guard 服务容器 — 让生产环境真正启用 Guard 检查
            // 决策: 独立 DI 容器+手动注册最小服务集，避免引入完整 Host 初始化开销
            // 替代方案: 调用 AddJoinCodeCompositionAutoRegisteredServices（已否决，会注册大量无关服务）
            await using var bridgeServices = BuildBridgeGuardServices(bridgeFs);

            var command = new ChatCommands.Bridge.BridgeMainCommand(
                services: bridgeServices,
                fs: bridgeFs,
                processService: bridgeProcessService,
                policyService: bridgeServices.GetService<IRemotePolicyService>(),
                tokenStorage: bridgeServices.GetService<ITokenStorage>(),
                configService: bridgeServices.GetService<IConfigurationService>(),
                logger: bridgeServices.GetService<ILogger<ChatCommands.Bridge.BridgeMainCommand>>());
            var bridgeArgs = args.Length > 1 ? args[1..] : [];
            return await command.ExecuteAsync(bridgeArgs).ConfigureAwait(false);
        }

        // schema 子命令 — 输出 CLI 参数定义 JSON（对齐架构指南可发现性：Schema 自省）
        if (subCommand == CliSubCommand.Schema) {
            var props = CliArgSchema.Properties.Select(p => new Cli.Output.CliSchemaPropertyDto(
                p.Name, p.ShortName, p.Description, p.Type, p.AcceptsValue, p.RiskLevel, p.Category, p.Example)).ToArray();
            var result = new Cli.Output.CliSchemaResult(props);
            System.Console.WriteLine(Cli.Output.CliOutputEnvelope<Cli.Output.CliSchemaResult>.Success(result).ToJsonString());
            return 0;
        }

        // 扁平元动词子命令 — ADR 0069: mcp_call/mcp_list/mcp_schema/mcp_search/mcp_serve/slash_call/slash_list/slash_schema/doctor
        if (subCommand is not null) {
            var flatResult = await FlatSubCommandRouter.TryExecuteAsync(subCommand.Value, args, CancellationToken.None).ConfigureAwait(false);
            if (flatResult is not null)
                return flatResult.Value;
        }

        TerminalHelper.WriteError($"未知子命令: {args[0]}（用 jcc --help 查看可用命令）");
        return 1;
    }

    /// <summary>
    /// 构建 Bridge Guard 服务容器 — 注册 BridgeMainCommand 所需的 Guard 服务及其依赖
    /// 决策: 手动注册最小服务集，避免引入完整 AddAiWorkflowServices 的初始化开销
    /// 替代方案: 调用 AddJoinCodeCompositionAutoRegisteredServices（已否决，会注册大量无关服务）
    /// </summary>
    /// <param name="fs">文件系统抽象（与 BridgeMainCommand 复用同一实例）</param>
    /// <returns>已注册 Guard 服务及依赖的 ServiceProvider（调用方负责 Dispose）</returns>
    internal static ServiceProvider BuildBridgeGuardServices(IFileSystem fs) {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole());
        services.AddSingleton(fs);

        // ConfigurationService — 依赖 IFileSystem（已注册）
        // 用于读取/写入 remoteDialogSeen 配置
        services.AddSingleton<IConfigurationService, Core.Configuration.ConfigurationService>();

        // TokenStorage — 依赖 IFileSystem（已注册）
        // 用于加载 OAuth Token（未过期）
        services.AddSingleton<ITokenStorage, global::Services.OAuth.TokenStorage>();

        // RemotePolicyOptions — 从环境变量读取配置
        // 决策: 与 TelemetryConfig.FromEnvironment() 模式一致（环境变量优先）
        // 环境变量: JCC_REMOTE_POLICY_ENDPOINT / JCC_REMOTE_POLICY_KEY / JCC_REMOTE_POLICY_REFRESH_SECONDS / JCC_REMOTE_POLICY_CACHE_SECONDS
        var policyOptions = new Core.Policy.RemotePolicyOptions {
            ApiEndpoint = Environment.GetEnvironmentVariable("JCC_REMOTE_POLICY_ENDPOINT") ?? string.Empty,
            ClientKey = Environment.GetEnvironmentVariable("JCC_REMOTE_POLICY_KEY") ?? string.Empty,
            RefreshInterval = ParseTimeSpanSeconds("JCC_REMOTE_POLICY_REFRESH_SECONDS", TimeSpan.FromMinutes(10)),
            CacheExpiration = ParseTimeSpanSeconds("JCC_REMOTE_POLICY_CACHE_SECONDS", TimeSpan.FromMinutes(15)),
        };
        services.AddSingleton(Options.Create(policyOptions));

        // TelemetryConfig — 无参构造函数自动从环境变量初始化（JCC_TELEMETRY_EXPORT/JCC_TELEMETRY_ENABLED 等）
        services.AddSingleton<JoinCode.Abstractions.Models.Telemetry.TelemetryConfig>();
        // TelemetryService — 依赖 TelemetryConfig（必填）、ILogger（可选）
        services.AddSingleton<ITelemetryService, Core.Telemetry.TelemetryService>();

        // IClockService — 支持环境变量 JCC_CLOCK_MODE=Fake 切换到 FakeClockService（调试/E2E测试）
        // 决策: 使用 ClockServiceFactory.Create() 而非直接注册 PhysicalClockService
        // 原因: 与 BuildBridgeGuardServices 中其他环境变量读取模式一致（TelemetryConfig/RemotePolicyOptions）
        // 替代方案已否决: services.AddSingleton<IClockService, PhysicalClockService>()（不支持环境变量切换）
        services.AddSingleton(ClockServiceFactory.Create());

        // HttpClient — 通过 IHttpClientFactory 管理（P1-3 已通过卫星项目 aot-httpclientfactory-test 验证 NativeAOT 兼容）
        // 决策: 使用 AddHttpClient<TClient, TImplementation>() 模式，DI 自动注入 HttpClient 到 RemotePolicyService
        // 优势: HttpMessageHandler 生命周期由 IHttpClientFactory 池化管理，避免 socket 耗尽
        // 替代方案已否决: services.AddSingleton<HttpClient>()（无 Handler 池化，长生命周期风险）
        services.AddHttpClient<IRemotePolicyService, Core.Policy.RemotePolicyService>();

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// 从环境变量解析 TimeSpan（秒数），失败返回默认值
    /// </summary>
    private static TimeSpan ParseTimeSpanSeconds(string envVar, TimeSpan defaultValue) {
        var value = Environment.GetEnvironmentVariable(envVar);
        if (int.TryParse(value, out var seconds) && seconds > 0)
            return TimeSpan.FromSeconds(seconds);
        return defaultValue;
    }

    /// <summary>
    /// 解析命令行参数
    /// </summary>
    public static CommandLineOptions ParseArgs(string[] args) {
        var result = CliArgParser.Parse(args);
        if (result.HasError) {
            Cli.TerminalHelper.WriteLine($"错误: {result.Error}");
            Cli.TerminalHelper.WriteLine("使用 --help 查看可用选项。");
            Environment.Exit((int)ExitCode.ArgumentParseError);
        }

        // --debuglog: 尽早启用调试日志输出，确保后续所有 Diag.WriteLine 都能输出
        // 决策: 在构造 CommandLineOptions 之前调用，保证最早可能的诊断时机
        if (result.DebugLog)
            Abstractions.Utils.Diagnostics.Diag.EnableDebugLog();

        // 锁诊断日志跟随 debuglog 开关 — 默认关闭，--debuglog 或 JCC_DEBUGLOG=1 时才开启
        if (Abstractions.Utils.Diagnostics.Diag.IsDebugLog)
            Core.Utils.LockRegistry.SetDiagnosticsEnabled(true);

        var options = new CommandLineOptions {
            ShowHelp = result.Help,
            ShowVersion = result.Version,
            PipeName = result.Pipe,
            Prompt = result.Prompt,
            Model = result.Model,
            Vendor = result.Vendor,
            NonInteractive = result.NonInteractive,
            NoConfirm = result.NoConfirm,
            TrustWorkspace = result.Trust,
            ForceInteractive = result.ForceInteractive,
            DebugLog = result.DebugLog,
            ContinueSession = result.Continue,
            ResumeSessionId = result.Resume,
            PermissionMode = result.PermissionMode,
            DangerouslySkipPermissions = result.DangerouslySkipPermissions,
            AllowedTools = ParseToolList(result.AllowedTools),
            DisallowedTools = ParseToolList(result.DisallowedTools),
            SystemPrompt = result.SystemPrompt,
            AppendSystemPrompt = result.AppendSystemPrompt,
            JsonOutput = result.Json,
            OutputFormat = result.Format,
            DryRun = result.DryRun,
            Yes = result.Yes,
            Force = result.Force,
            Quiet = result.Quiet,
        };

        // --await N: 超时自动关闭秒数
        if (!string.IsNullOrWhiteSpace(result.Await) && int.TryParse(result.Await, out var awaitSeconds) && awaitSeconds > 0) {
            options = options with { AwaitTimeoutSeconds = awaitSeconds };
        }

        // 环境变量映射 — 由 CliOptionGenerator 从 [CliOption(EnvVar=...)] 声明自动生成
        // 别名展开（--yes→--no-confirm, --force/--bypass→--permission-mode bypass, --json→--format json）
        // 已在 CliArgParser.Parse 内部完成，此处只需同步环境变量
        CliArgParser.ApplyEnvVars(result);

        if (Cli.TerminalHelper.IsHeadless) {
            options = options with { NonInteractive = true };
        }

        if (options.ForceInteractive) {
            Cli.TerminalHelper.ForceInteractive = true;
            options = options with { NonInteractive = false };
        }

        // --no-confirm / --yes（别名已展开）→ ForceNonInteractive
        if (options.NoConfirm) {
            Core.Utils.TestEnvironmentDetector.ForceNonInteractive = true;
        }

        options = options with { DetectedHeadlessMode = Cli.TerminalHelper.IsHeadless ? HeadlessMode.NoTty : HeadlessMode.Interactive };

        if (options.NonInteractive && options.DetectedHeadlessMode == HeadlessMode.Interactive) {
            options = options with { DetectedHeadlessMode = HeadlessMode.UserRequested };
        }

        return options;
    }

    /// <summary>
    /// 解析工具列表（逗号或空格分隔）— 用于 --allowed-tools / --disallowed-tools
    /// 支持 "Read,Edit,Bash(git:*)" 和 "Read Edit Bash(git:*)" 两种格式
    /// </summary>
    private static List<string> ParseToolList(string? raw) {
        if (string.IsNullOrWhiteSpace(raw))
            return new List<string>();

        // 逗号分隔优先，再尝试空格分隔
        var parts = raw.Contains(',')
            ? raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : raw.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return parts.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// 显示帮助信息 — 多级渐进式展开, 避免一次性注入过多帮助
    /// <para>jcc -h             → 分类概览(参数/子命令/环境变量/退出码)</para>
    /// <para>jcc -h options     → 参数选项详情(按分类分组)</para>
    /// <para>jcc -h sub         → 子命令分类概览</para>
    /// <para>jcc -h &lt;分类&gt;      → 该分类下的子命令列表</para>
    /// <para>jcc -h &lt;命令名&gt;    → 命令详情+示例</para>
    /// <para>jcc -h env         → 环境变量</para>
    /// <para>jcc -h exit        → 退出码</para>
    /// <para>jcc -h examples    → 使用示例</para>
    /// </summary>
    public static void ShowHelp(string? topic = null) {
        Cli.TerminalHelper.WriteLine("JoinCode - AI 智能体命令行工具");
        Cli.TerminalHelper.NewLine();

        if (string.IsNullOrWhiteSpace(topic)) {
            ShowHelpOverview();
            return;
        }

        var t = topic.Trim();
        var lower = t.ToLowerInvariant();

        // scenario 支持两级主题: scenario / scenario <name>
        if (lower is "scenario" or "scenarios" or "scene") {
            ShowScenarios();
            return;
        }
        if (lower.StartsWith("scenario ", StringComparison.Ordinal)
            || lower.StartsWith("scenarios ", StringComparison.Ordinal)
            || lower.StartsWith("scene ", StringComparison.Ordinal)) {
            ShowScenarioDetail(t.Substring(t.IndexOf(' ') + 1).Trim());
            return;
        }

        // 其他主题取第一个 word（兼容多参数输入如 "gh pr" → "gh"）
        var firstWord = t.Contains(' ') ? t.AsSpan(0, t.IndexOf(' ')).ToString() : t;
        switch (firstWord.ToLowerInvariant()) {
            case "options" or "opt":
            Cli.TerminalHelper.WriteLine(CliArgParser.GetHelpText("categorized").Replace("cliarg", "jcc"));
            break;
            case "sub" or "subcommand" or "subs":
            Cli.TerminalHelper.WriteLine("子命令分类:");
            Cli.TerminalHelper.NewLine();
            Cli.TerminalHelper.WriteLine(CliSubCommandHelpText.GetCategories());
            break;
            case "env" or "environment":
            ShowEnvironmentVariables();
            break;
            case "exit" or "exitcode" or "exitcodes":
            ShowExitCodes();
            break;
            case "examples" or "ex" or "example":
            Cli.TerminalHelper.WriteLine(CliArgParser.GetHelpText("examples"));
            break;
            default:
            if (TryShowSubCommandHelp(firstWord)) break;
            Cli.TerminalHelper.WriteLine($"未知主题: {t}");
            Cli.TerminalHelper.NewLine();
            ShowHelpOverview();
            break;
        }
    }

    /// <summary>
    /// 帮助概览 — 第一级展开, 只显示分类入口
    /// </summary>
    private static void ShowHelpOverview() {
        Cli.TerminalHelper.WriteLine("用法: jcc [选项] [子命令] [参数]");
        Cli.TerminalHelper.NewLine();
        Cli.TerminalHelper.WriteLine("帮助主题:");
        Cli.TerminalHelper.WriteLine("  jcc -h scenario    情景模式(工具集+编排流程)");
        Cli.TerminalHelper.WriteLine("  jcc -h options     参数选项(按分类分组)");
        Cli.TerminalHelper.WriteLine("  jcc -h sub         子命令(按分类分组)");
        Cli.TerminalHelper.WriteLine("  jcc -h env         环境变量");
        Cli.TerminalHelper.WriteLine("  jcc -h exit        退出码");
        Cli.TerminalHelper.WriteLine("  jcc -h examples    使用示例");
        Cli.TerminalHelper.NewLine();
        Cli.TerminalHelper.WriteLine("快捷方式:");
        Cli.TerminalHelper.WriteLine("  jcc -h <分类名>    直接查看子命令分类(如 jcc -h GitHub)");
        Cli.TerminalHelper.WriteLine("  jcc -h <命令名>    直接查看命令详情(如 jcc -h gh)");
    }

    /// <summary>
    /// 尝试显示子命令帮助 — 按分类名或命令名查找
    /// </summary>
    private static bool TryShowSubCommandHelp(string topic) {
        var help = CliSubCommandHelpText.GetHelp(topic);
        if (help.StartsWith("未知")) return false;
        Cli.TerminalHelper.WriteLine(help);
        return true;
    }

    /// <summary>
    /// 显示环境变量 — 从 JccEnvVar 枚举 + [SubCommandInfo] 特性源码生成
    /// </summary>
    private static void ShowEnvironmentVariables() {
        Cli.TerminalHelper.WriteLine("环境变量:");
        Cli.TerminalHelper.NewLine();
        foreach (var cat in JccEnvVarHelpText.GetHelp().Split('\n', StringSplitOptions.RemoveEmptyEntries))
            Cli.TerminalHelper.WriteLine(cat);
    }

    /// <summary>
    /// 显示退出码 — 从 JccExitCode 枚举 + [SubCommandInfo] 特性源码生成
    /// </summary>
    private static void ShowExitCodes() {
        Cli.TerminalHelper.WriteLine("退出码:");
        Cli.TerminalHelper.NewLine();
        foreach (var line in JccExitCodeHelpText.GetHelp().Split('\n', StringSplitOptions.RemoveEmptyEntries))
            Cli.TerminalHelper.WriteLine(line);
    }

    /// <summary>
    /// 显示所有情景模式 — jcc -h scenario
    /// </summary>
    private static void ShowScenarios() {
        Cli.TerminalHelper.WriteLine("情景模式:");
        Cli.TerminalHelper.NewLine();
        if (ScenarioRegistry.Scenarios.Length == 0) {
            Cli.TerminalHelper.WriteLine("  (暂无已注册的情景模式)");
            return;
        }
        foreach (var s in ScenarioRegistry.Scenarios) {
            Cli.TerminalHelper.WriteLine($"  {s.Name}");
            Cli.TerminalHelper.WriteLine($"    {s.Description}");
            Cli.TerminalHelper.NewLine();
        }
        Cli.TerminalHelper.WriteLine("查看详情: jcc -h scenario <名称>");
    }

    /// <summary>
    /// 显示特定情景模式详情 — jcc -h scenario &lt;name&gt;，经 ToolMenuRenderer 统一渲染
    /// </summary>
    private static void ShowScenarioDetail(string name) {
        var scenario = ScenarioRegistry.Find(name);
        if (scenario is null) {
            Cli.TerminalHelper.WriteLine($"未知情景模式: {name}");
            Cli.TerminalHelper.NewLine();
            ShowScenarios();
            return;
        }
        Cli.TerminalHelper.WriteLine(ToolMenuRenderer.ToText(scenario).TrimEnd());
    }

    /// <summary>
    /// 显示版本信息
    /// </summary>
    public static void ShowVersion() {
        var assemblyVersion = typeof(ApplicationBuilder).Assembly.GetName().Version;
        var appVersion = assemblyVersion?.ToString() ?? "1.0.0";
        var runtimeVersion = Environment.Version.ToString();
        Cli.TerminalHelper.WriteLine($"JoinCode v{appVersion}");
        Cli.TerminalHelper.WriteLine($"运行时: .NET {runtimeVersion}");
    }

    #endregion
}