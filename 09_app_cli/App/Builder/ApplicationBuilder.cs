namespace JoinCode.App.Builder;

/// <summary>
/// 应用构建器 — 链式注册模块，提供基础设施方法供 Main 调用
/// </summary>
public sealed class ApplicationBuilder
{
    private readonly List<IAppModule> _modules = [];

    public ApplicationBuilder() { }

    /// <summary>
    /// 注册模块（泛型版本）
    /// </summary>
    public ApplicationBuilder UseModule<TModule>() where TModule : IAppModule, new()
    {
        _modules.Add(new TModule());
        return this;
    }

    /// <summary>
    /// 注册模块（实例版本）— 供外部项目（如 GUI）传入跨项目边界的模块
    /// </summary>
    public ApplicationBuilder UseModule(IAppModule module)
    {
        _modules.Add(module);
        return this;
    }

    /// <summary>
    /// 构建 Host — 按序调用各模块的 ConfigureServices
    /// </summary>
    public IHost BuildHost(WorkflowConfig config, CommandLineOptions options)
    {
        var context = new AppModuleContext
        {
            Options = options,
            Config = config
        };

        var ordered = _modules.OrderBy(m => m.Order).ToList();

        return Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                services.AddSingleton(config);
                // 视角1 #3: 注册 CommandLineOptions 为单例，供 PermissionConfig 后配置读取
                services.AddSingleton(options);

                foreach (var module in ordered)
                {
                    module.ConfigureServices(services, context);
                }

                // 视角1 #3: 后配置 PermissionConfig，合并 --allowed-tools / --disallowed-tools CLI 参数
                // 决策: 使用 IOptions 后配置模式，在 Guard 模块的默认配置之后追加，不破坏封装
                // 替代方案已否决: 修改 Guard 模块接口（破坏组件边界）
                services.AddOptions<NetworkRetryOptions>();
                services.AddOptions<PermissionConfig>()
                    .Configure<CommandLineOptions>((permConfig, cliOptions) =>
                    {
                        if (cliOptions.AllowedTools is { Count: > 0 })
                        {
                            foreach (var tool in cliOptions.AllowedTools)
                            {
                                if (!permConfig.AutoApprovedTools.ContainsKey(tool))
                                {
                                    permConfig.AutoApprovedTools[tool] = new ToolPermissionRule { ToolName = tool, Description = "From CLI --allowed-tools" };
                                }
                            }
                            Diag.WriteLine($"[MAIN] --allowed-tools 合并 {cliOptions.AllowedTools.Count} 个工具到 PermissionConfig.AutoApprovedTools");
                        }

                        if (cliOptions.DisallowedTools is { Count: > 0 })
                        {
                            foreach (var tool in cliOptions.DisallowedTools)
                            {
                                if (!permConfig.AutoRejectedTools.ContainsKey(tool))
                                {
                                    permConfig.AutoRejectedTools[tool] = new ToolPermissionRule { ToolName = tool, Description = "From CLI --disallowed-tools" };
                                }
                            }
                            Diag.WriteLine($"[MAIN] --disallowed-tools 合并 {cliOptions.DisallowedTools.Count} 个工具到 PermissionConfig.AutoRejectedTools");
                        }
                    });
            })
            .ConfigureLogging(logging =>
            {
                // ADR 0100: 清除 Host.CreateDefaultBuilder() 的默认 AddConsole，用 ConsoleActorLoggerProvider 替代
                // 默认 AddConsole 的 AnsiLogConsole 直接写 Console.Out，绕过 Actor 串行化
                logging.ClearProviders();
                var consoleActor = Cli.TerminalHelper.GetConsoleActor();
                if (consoleActor is not null)
                    logging.AddProvider(new Cli.Display.ConsoleActorLoggerProvider(consoleActor));
                else
                    logging.AddConsole(options => { options.FormatterName = "simple"; });

                var minLevelStr = Environment.GetEnvironmentVariable("JCC_LOG_LEVEL");
                var minLevel = minLevelStr switch
                {
                    "Trace" => LogLevel.Trace,
                    "Debug" => LogLevel.Debug,
                    "Information" => LogLevel.Information,
                    "Warning" => LogLevel.Warning,
                    "Error" => LogLevel.Error,
                    _ => LogLevel.Warning
                };
                logging.SetMinimumLevel(minLevel);
            })
            .Build();
    }

    /// <summary>
    /// 模块初始化 — 按序调用各模块的 ConfigureAsync
    /// </summary>
    public async Task ConfigureModulesAsync(IServiceProvider services)
    {
        var ordered = _modules.OrderBy(m => m.Order).ToList();
        foreach (var module in ordered)
        {
            // P2-5: 迁移到 Diag.WriteLine，统一受 JCC_DEBUGLOG 控制
            Diag.WriteLine($"[MODULE] {module.GetType().Name} start");
            await module.ConfigureAsync(services, CancellationToken.None).ConfigureAwait(false);
            Diag.WriteLine($"[MODULE] {module.GetType().Name} done");
        }
    }

    #region 基础设施方法 — 供 Main 直接调用

    /// <summary>
    /// 判断参数是否为子命令
    /// </summary>
    public static bool IsSubCommand(string arg) =>
        CliSubCommandExtensions.FromValue(arg) is not null;

    /// <summary>
    /// 执行子命令
    /// </summary>
    public static async Task<int> RunSubCommandAsync(string[] args)
    {
        var subCommand = CliSubCommandExtensions.FromValue(args[0]);

        if (subCommand is CliSubCommand.RemoteControl or CliSubCommand.Rc or CliSubCommand.Remote)
        {
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
            return await command.ExecuteAsync(bridgeArgs);
        }

        // schema 子命令 — 输出 CLI 参数定义 JSON（对齐架构指南可发现性：Schema 自省）
        // 使用生成器生成的 ToJson() 方法（Utf8JsonWriter，AOT 兼容，无需 JsonContext）
        if (subCommand == CliSubCommand.Schema)
        {
            System.Console.WriteLine(CliArgSchema.ToJson());
            return 0;
        }

        // 扁平元动词子命令 — ADR 0069: mcp_call/mcp_list/mcp_schema/mcp_search/mcp_serve/slash_call/slash_list/slash_schema/doctor
        if (subCommand is not null)
        {
            var flatResult = await FlatSubCommandRouter.TryExecuteAsync(subCommand.Value, args, CancellationToken.None).ConfigureAwait(false);
            if (flatResult is not null)
                return flatResult.Value;
        }

        // 旧子命令提示 — ADR 0069: 已由扁平元动词取代
        if (subCommand is CliSubCommand.Mcp)
        {
            TerminalHelper.WriteError("jcc mcp 已由扁平元动词取代，请用 jcc mcp_call/mcp_list/mcp_schema/mcp_search/mcp_serve");
            return 1;
        }
        if (subCommand is CliSubCommand.Tool or CliSubCommand.Agent or CliSubCommand.Code)
        {
            TerminalHelper.WriteError($"jcc {args[0]} 已废弃，请用 jcc mcp_call 或 jcc slash_call");
            return 1;
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
    internal static ServiceProvider BuildBridgeGuardServices(IFileSystem fs)
    {
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
        var policyOptions = new Core.Policy.RemotePolicyOptions
        {
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
    private static TimeSpan ParseTimeSpanSeconds(string envVar, TimeSpan defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(envVar);
        if (int.TryParse(value, out var seconds) && seconds > 0)
            return TimeSpan.FromSeconds(seconds);
        return defaultValue;
    }

    /// <summary>
    /// 解析命令行参数
    /// </summary>
    public static CommandLineOptions ParseArgs(string[] args)
    {
        var result = CliArgParser.Parse(args);
        if (result.HasError)
        {
            Cli.TerminalHelper.WriteLine($"错误: {result.Error}");
            Cli.TerminalHelper.WriteLine("使用 --help 查看可用选项。");
            Environment.Exit((int)ExitCode.ArgumentParseError);
        }

        // --debuglog: 尽早启用调试日志输出，确保后续所有 Diag.WriteLine 都能输出
        // 决策: 在构造 CommandLineOptions 之前调用，保证最早可能的诊断时机
        if (result.DebugLog)
            Abstractions.Utils.Diagnostics.Diag.EnableDebugLog();

        var options = new CommandLineOptions
        {
            ShowHelp = result.Help,
            ShowVersion = result.Version,
            PipeName = result.Pipe,
            Prompt = result.Prompt,
            Model = result.Model,
            Vendor = result.Vendor,
            NonInteractive = result.NonInteractive,
            NoConfirm = result.NoConfirm,
            TrustWorkspace = result.Trust,
            Brief = result.Brief,
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
            DoctorMode = result.Doctor,
            DoctorServerMode = result.DoctorServer,
            DoctorEndpoint = result.DoctorEndpoint,
            JsonOutput = result.Json,
            OutputFormat = result.Format,
            DryRun = result.DryRun,
            Yes = result.Yes,
            Force = result.Force,
            Quiet = result.Quiet,
        };

        // --await N: 超时自动关闭秒数
        if (!string.IsNullOrWhiteSpace(result.Await) && int.TryParse(result.Await, out var awaitSeconds) && awaitSeconds > 0)
        {
            options.AwaitTimeoutSeconds = awaitSeconds;
        }

        // --doctor-port N: 医生 SSE 服务器端口
        if (!string.IsNullOrWhiteSpace(result.DoctorPort) && int.TryParse(result.DoctorPort, out var doctorPort) && doctorPort > 0)
        {
            options.DoctorPort = doctorPort;
        }

        // 环境变量映射 — 由 CliOptionGenerator 从 [CliOption(EnvVar=...)] 声明自动生成
        // 别名展开（--yes→--no-confirm, --force/--bypass→--permission-mode bypass, --json→--format json）
        // 已在 CliArgParser.Parse 内部完成，此处只需同步环境变量
        CliArgParser.ApplyEnvVars(result);

        if (Cli.TerminalHelper.IsHeadless)
        {
            options.NonInteractive = true;
        }

        if (options.ForceInteractive)
        {
            Cli.TerminalHelper.ForceInteractive = true;
            options.NonInteractive = false;
        }

        // --no-confirm / --yes（别名已展开）→ ForceNonInteractive
        if (options.NoConfirm)
        {
            Core.Utils.TestEnvironmentDetector.ForceNonInteractive = true;
        }

        options.DetectedHeadlessMode = Cli.TerminalHelper.IsHeadless ? HeadlessMode.NoTty : HeadlessMode.Interactive;

        if (options.NonInteractive && options.DetectedHeadlessMode == HeadlessMode.Interactive)
        {
            options.DetectedHeadlessMode = HeadlessMode.UserRequested;
        }

        return options;
    }

    /// <summary>
    /// 解析工具列表（逗号或空格分隔）— 用于 --allowed-tools / --disallowed-tools
    /// 支持 "Read,Edit,Bash(git:*)" 和 "Read Edit Bash(git:*)" 两种格式
    /// </summary>
    private static List<string> ParseToolList(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return new List<string>();

        // 逗号分隔优先，再尝试空格分隔
        var parts = raw.Contains(',')
            ? raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : raw.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return parts.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// 加载配置 — 含 DotEnv 回退
    /// </summary>
    public static async Task<WorkflowConfig> LoadConfigAsync(CommandLineOptions options, IFileSystem fs, IModelConfigLoader? modelConfigLoader = null)
    {
        var dotEnv = GetDotEnv();
        WorkflowConfig config;

        try
        {
            config = await new Core.Configuration.ConfigLoader(modelConfigLoader: modelConfigLoader).LoadConfigAsync(fs);
        }
        catch (ConfigurationException ex) when (ex.Message.Contains("API Key"))
        {
            if (dotEnv is not null)
            {
                await dotEnv.ApplyToConfigAsync(fs);
                config = await new Core.Configuration.ConfigLoader(modelConfigLoader: modelConfigLoader).LoadConfigAsync(fs);
            }
            else
            {
                throw;
            }
        }

        var registry = new Core.Configuration.Providers.ProviderDefinitionRegistry(modelConfigLoader ?? new ModelConfigLoader());

        if (dotEnv is not null)
        {
            dotEnv.ApplyToMemory(config, registry);
        }

        // 环境变量优先级最高 — 无论 dotEnv 是否存在，都必须应用环境变量覆盖
        // 修复: 之前 ApplyEnvOverrides 只在 dotEnv != null 时调用，
        // 导致无 .env/api.json 时 JCC_ENDPOINT/JCC_MODEL_ID 等环境变量不生效
        new Core.Configuration.SettingsMapper(registry).ApplyEnvOverrides(config);

        // --model 已在 ParseArgs 阶段转为 JCC_MODEL_ID 环境变量，由 EnvOverrideApplier + ApplyEnvOverrides 统一处理
        if (options.IsPipeMode)
            config.PipeEndpoint = new PipeTransportConfig { PipeName = options.PipeName ?? throw new InvalidOperationException("PipeName required in pipe mode") };

        return config;
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
    public static void ShowHelp(string? topic = null)
    {
        Cli.TerminalHelper.WriteLine("JoinCode - AI 智能体命令行工具");
        Cli.TerminalHelper.NewLine();

        if (string.IsNullOrWhiteSpace(topic))
        {
            ShowHelpOverview();
            return;
        }

        var t = topic.Trim();
        switch (t.ToLowerInvariant())
        {
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
                if (TryShowSubCommandHelp(t)) break;
                Cli.TerminalHelper.WriteLine($"未知主题: {t}");
                Cli.TerminalHelper.NewLine();
                ShowHelpOverview();
                break;
        }
    }

    /// <summary>
    /// 帮助概览 — 第一级展开, 只显示分类入口
    /// </summary>
    private static void ShowHelpOverview()
    {
        Cli.TerminalHelper.WriteLine("用法: jcc [选项] [子命令] [参数]");
        Cli.TerminalHelper.NewLine();
        Cli.TerminalHelper.WriteLine("帮助主题:");
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
    private static bool TryShowSubCommandHelp(string topic)
    {
        var help = CliSubCommandHelpText.GetHelp(topic);
        if (help.StartsWith("未知")) return false;
        Cli.TerminalHelper.WriteLine(help);
        return true;
    }

    /// <summary>
    /// 显示环境变量 — 从 JccEnvVar 枚举 + [SubCommandInfo] 特性源码生成
    /// </summary>
    private static void ShowEnvironmentVariables()
    {
        Cli.TerminalHelper.WriteLine("环境变量:");
        Cli.TerminalHelper.NewLine();
        foreach (var cat in JccEnvVarHelpText.GetHelp().Split('\n', StringSplitOptions.RemoveEmptyEntries))
            Cli.TerminalHelper.WriteLine(cat);
    }

    /// <summary>
    /// 显示退出码 — 从 JccExitCode 枚举 + [SubCommandInfo] 特性源码生成
    /// </summary>
    private static void ShowExitCodes()
    {
        Cli.TerminalHelper.WriteLine("退出码:");
        Cli.TerminalHelper.NewLine();
        foreach (var line in JccExitCodeHelpText.GetHelp().Split('\n', StringSplitOptions.RemoveEmptyEntries))
            Cli.TerminalHelper.WriteLine(line);
    }

    /// <summary>
    /// 显示版本信息
    /// </summary>
    public static void ShowVersion()
    {
        var assemblyVersion = typeof(ApplicationBuilder).Assembly.GetName().Version;
        var appVersion = assemblyVersion?.ToString() ?? "1.0.0";
        var runtimeVersion = Environment.Version.ToString();
        Cli.TerminalHelper.WriteLine($"JoinCode v{appVersion}");
        Cli.TerminalHelper.WriteLine($"运行时: .NET {runtimeVersion}");
    }

    private static Entry.DotEnvConfig? _dotEnvCache;

    private static Entry.DotEnvConfig? GetDotEnv()
    {
        if (_dotEnvCache is null)
            _dotEnvCache = LoadDotEnvCore();
        return _dotEnvCache;
    }

    private static Entry.DotEnvConfig? LoadDotEnvCore()
    {
        var envPath = FindDotEnvPath();
        if (envPath is null) return null;
        return Entry.DotEnvConfig.LoadFrom(envPath);
    }

    private static string? FindDotEnvPath()
    {
        // 1. JCC_CONFIG_PATH 环境变量 — 用户自定义配置路径
        var customPath = Environment.GetEnvironmentVariable("JCC_CONFIG_PATH");
        if (!string.IsNullOrEmpty(customPath) && System.IO.File.Exists(customPath))
            return customPath;

        // 2. 当前工作目录 — 用户从任意目录运行 jcc 时查找
        var cwdPath = System.IO.Path.Combine(Environment.CurrentDirectory, ".env", "api.json");
        if (System.IO.File.Exists(cwdPath)) return cwdPath;

        // 3. 项目级 .jcc 目录 — Release 部署场景
        var envPath = System.IO.Path.Combine(AppDataConstants.Paths.DotEnvDirectory, "api.json");
        if (System.IO.File.Exists(envPath)) return envPath;

        // 4. 开发环境回退 — 从 bin/Release/net10.0 向上 5 级到项目根
        var projectRoot = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".env", "api.json"));
        if (System.IO.File.Exists(projectRoot)) return projectRoot;

        return null;
    }

    #endregion
}
