namespace JoinCode.App.Builder;

/// <summary>
/// 应用构建器 — 链式注册模块，提供基础设施方法供 Main 调用
/// </summary>
public sealed class ApplicationBuilder
{
    private readonly List<IAppModule> _modules = [];

    public ApplicationBuilder() { }

    /// <summary>
    /// 注册模块
    /// </summary>
    public ApplicationBuilder UseModule<TModule>() where TModule : IAppModule, new()
    {
        _modules.Add(new TModule());
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

                foreach (var module in ordered)
                {
                    module.ConfigureServices(services, context);
                }
            })
            .ConfigureLogging(logging =>
            {
                logging.AddConsole();
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
        // 仅在 JCC_DEBUG_MODULES=1/true 时打印模块启停日志(链路排查用),默认关闭减少噪音
        var debugModules = Environment.GetEnvironmentVariable("JCC_DEBUG_MODULES") is "1" or "true";
        foreach (var module in ordered)
        {
            if (debugModules)
                Console.Error.WriteLine($"[MODULE] {module.GetType().Name} start");
            await module.ConfigureAsync(services, CancellationToken.None).ConfigureAwait(false);
            if (debugModules)
                Console.Error.WriteLine($"[MODULE] {module.GetType().Name} done");
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
            var bridgeProcessService = new IO.ProcessService.PhysicalProcessService();

            // 构建 Bridge Guard 服务容器 — 让生产环境真正启用 Guard 检查
            // 决策: 独立 DI 容器+手动注册最小服务集，避免引入完整 Host 初始化开销
            // 替代方案: 调用 AddJoinCodeCompositionAutoRegisteredServices（已否决，会注册大量无关服务）
            using var bridgeServices = BuildBridgeGuardServices(bridgeFs);

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

        var rootCommand = new RootCommand("JoinCode CLI");
            var cliFs = IO.FileSystem.FileSystemFactory.Create();
        rootCommand.Add(new ToolCommand());
        rootCommand.Add(new AgentCommand(cliFs));
        rootCommand.Add(new CodeCommand(cliFs));
        return await rootCommand.Parse(args).InvokeAsync();
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
            Environment.Exit(1);
        }
        var options = new CommandLineOptions
        {
            ShowHelp = result.Help,
            ShowVersion = result.Version,
            PipeName = result.Pipe,
            Prompt = result.Prompt,
            Model = result.Model,
            NonInteractive = result.NonInteractive,
            TrustWorkspace = result.Trust,
            Brief = result.Brief,
            ForceInteractive = result.ForceInteractive,
        };

        // --await N: 超时自动关闭秒数
        if (!string.IsNullOrWhiteSpace(result.Await) && int.TryParse(result.Await, out var awaitSeconds) && awaitSeconds > 0)
        {
            options.AwaitTimeoutSeconds = awaitSeconds;
        }

        if (Cli.TerminalHelper.IsHeadless)
        {
            options.NonInteractive = true;
        }

        if (options.ForceInteractive)
        {
            Cli.TerminalHelper.ForceInteractive = true;
            options.NonInteractive = false;
        }

        options.DetectedHeadlessMode = Cli.TerminalHelper.IsHeadless ? HeadlessMode.NoTty : HeadlessMode.Interactive;

        if (options.NonInteractive && options.DetectedHeadlessMode == HeadlessMode.Interactive)
        {
            options.DetectedHeadlessMode = HeadlessMode.UserRequested;
        }

        return options;
    }

    /// <summary>
    /// 加载配置 — 含 DotEnv 回退
    /// </summary>
    public static async Task<WorkflowConfig> LoadConfigAsync(CommandLineOptions options, IFileSystem fs)
    {
        var dotEnv = GetDotEnv();
        WorkflowConfig config;

        try
        {
            config = await ConfigLoader.LoadConfigAsync(fs);
            Console.Error.WriteLine($"[CFG] After LoadConfigAsync: Provider={config.Provider.Provider}, Endpoint={config.Provider.Endpoint}, ModelId={config.Provider.ModelId}");
        }
        catch (ConfigurationException ex) when (ex.Message.Contains("API Key"))
        {
            if (dotEnv is not null)
            {
                await dotEnv.ApplyToConfigAsync(fs);
                config = await ConfigLoader.LoadConfigAsync(fs);
                Console.Error.WriteLine($"[CFG] After reload (DotEnv applied to config): Provider={config.Provider.Provider}, Endpoint={config.Provider.Endpoint}");
            }
            else
            {
                config = new WorkflowConfig();
            }
        }

        if (dotEnv is not null)
        {
            Console.Error.WriteLine($"[CFG] Before ApplyToMemory: Endpoint={config.Provider.Endpoint}");
            dotEnv.ApplyToMemory(config);
            Console.Error.WriteLine($"[CFG] After ApplyToMemory: Provider={config.Provider.Provider}, Endpoint={config.Provider.Endpoint}, ModelId={config.Provider.ModelId}");

            // 环境变量优先级最高 — ApplyToMemory 可能覆盖了 env var 设置的值，重新应用
            Core.Configuration.SettingsMapper.ApplyEnvOverrides(config);
            Console.Error.WriteLine($"[CFG] After re-ApplyEnvOverrides: Provider={config.Provider.Provider}, Endpoint={config.Provider.Endpoint}, ModelId={config.Provider.ModelId}");
        }

        if (!string.IsNullOrWhiteSpace(options.Model))
            config.Provider.ModelId = options.Model;

        if (options.IsPipeMode)
            config.PipeEndpoint = new PipeTransportConfig { PipeName = options.PipeName! };

        return config;
    }

    /// <summary>
    /// 显示帮助信息
    /// </summary>
    public static void ShowHelp()
    {
        Cli.TerminalHelper.WriteLine("JoinCode - AI 智能体命令行工具");
        Cli.TerminalHelper.NewLine();
        Cli.TerminalHelper.WriteLine("用法: jcc [选项] [提示词]");
        Cli.TerminalHelper.NewLine();
        Cli.TerminalHelper.WriteLine("选项:");
        Cli.TerminalHelper.WriteLine("  -h, --help              显示帮助信息");
        Cli.TerminalHelper.WriteLine("  -v, --version           显示版本信息");
        Cli.TerminalHelper.WriteLine("  -p, --prompt <文本>     非交互模式：直接传入提示词");
        Cli.TerminalHelper.WriteLine("  -m, --model <模型ID>    指定模型");
        Cli.TerminalHelper.WriteLine("  --trust                 自动信任当前工作目录");
        Cli.TerminalHelper.WriteLine("  --non-interactive       强制非交互模式");
        Cli.TerminalHelper.WriteLine("  --pipe <管道名>         命名管道通信模式");
        Cli.TerminalHelper.NewLine();
        Cli.TerminalHelper.WriteLine("子命令:");
        Cli.TerminalHelper.WriteLine("  tool                    MCP 工具管理");
        Cli.TerminalHelper.WriteLine("  agent                   智能体管理");
        Cli.TerminalHelper.WriteLine("  code                    代码操作");
        Cli.TerminalHelper.NewLine();
        Cli.TerminalHelper.WriteLine("环境变量:");
        Cli.TerminalHelper.WriteLine("  JCC_PROVIDER           LLM 提供商 (openai/azure/anthropic)");
        Cli.TerminalHelper.WriteLine("  JCC_MODEL_ID           模型 ID");
        Cli.TerminalHelper.WriteLine("  JCC_API_KEY            API Key");
        Cli.TerminalHelper.WriteLine("  JCC_ENDPOINT           API 端点");
        Cli.TerminalHelper.WriteLine("  OPENAI_API_KEY          OpenAI API Key");
        Cli.TerminalHelper.WriteLine("  ANTHROPIC_API_KEY       Anthropic API Key");
        Cli.TerminalHelper.WriteLine("  AZURE_OPENAI_API_KEY    Azure OpenAI API Key");
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
        var envPath = System.IO.Path.Combine(AppContext.BaseDirectory, ".env", "api.json");
        if (System.IO.File.Exists(envPath)) return envPath;

        var projectRoot = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".env", "api.json"));
        if (System.IO.File.Exists(projectRoot)) return projectRoot;

        return null;
    }

    #endregion
}
