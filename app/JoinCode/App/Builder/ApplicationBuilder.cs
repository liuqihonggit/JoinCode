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
                logging.AddConsole(options =>
                {
                    options.FormatterName = "simple";
                });
                logging.AddSimpleConsole(options =>
                {
                    options.IncludeScopes = true;
                });
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
        if (subCommand == CliSubCommand.Schema)
        {
            var schemaProps = CliArgSchema.Properties;
            var envelope = Cli.Output.CliOutputEnvelope.Success(schemaProps, new Cli.Output.CliOutputMeta
            {
                Version = typeof(ApplicationBuilder).Assembly.GetName().Version?.ToString(),
            });
            var json = System.Text.Json.JsonSerializer.Serialize(envelope, Cli.Output.CliOutputJsonContext.Default.CliOutputEnvelope);
            System.Console.WriteLine(json);
            return 0;
        }

        var rootCommand = new RootCommand("JoinCode CLI");
            var cliFs = IO.FileSystem.FileSystemFactory.Create();
        rootCommand.Add(new ToolCommand());
        rootCommand.Add(new AgentCommand(cliFs));
        rootCommand.Add(new CodeCommand(cliFs));
        rootCommand.Add(new McpCliCommand());
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

        // 视角1 #6 + #9: CLI 参数 → JCC_PERMISSION_MODE 环境变量
        // 决策: 复用 PermissionChecker.TryGetPermissionModeFromEnv 现有逻辑，不修改 PermissionChecker 构造函数
        // --bypass 等价于 --permission-mode bypass
        // 两者同时存在时 --permission-mode 优先（更具体）
        var permissionModeFromCli = !string.IsNullOrEmpty(options.PermissionMode)
            ? options.PermissionMode
            : options.DangerouslySkipPermissions ? "bypass" : null;
        if (!string.IsNullOrEmpty(permissionModeFromCli))
        {
            Environment.SetEnvironmentVariable(JccEnvVar.PermissionMode.ToValue(), permissionModeFromCli);
            Diag.WriteLine($"[MAIN] CLI permission-mode={permissionModeFromCli} → JCC_PERMISSION_MODE 环境变量已设置");
        }

        // --vendor: CLI 参数 → JCC_VENDOR 环境变量（自动匹配 profiles 中的同名预设）
        if (!string.IsNullOrEmpty(options.Vendor))
        {
            Environment.SetEnvironmentVariable(JccEnvVar.Vendor.ToValue(), options.Vendor);
            Diag.WriteLine($"[MAIN] CLI vendor={options.Vendor} → JCC_VENDOR 环境变量已设置");
        }

        // --model: CLI 参数 → JCC_MODEL_ID 环境变量（统一走环境变量覆盖链，与 --vendor 一致）
        if (!string.IsNullOrEmpty(options.Model))
        {
            Environment.SetEnvironmentVariable(JccEnvVar.ModelId.ToValue(), options.Model);
            Diag.WriteLine($"[MAIN] CLI model={options.Model} → JCC_MODEL_ID 环境变量已设置");
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

        if (options.NoConfirm)
        {
            Core.Utils.TestEnvironmentDetector.ForceNonInteractive = true;
        }

        // --yes 等价于 --no-confirm（对齐架构指南 AX 模式）
        if (options.Yes)
        {
            options.NoConfirm = true;
            Core.Utils.TestEnvironmentDetector.ForceNonInteractive = true;
        }

        // --force 等价于 --bypass（对齐架构指南安全设计）
        if (options.Force)
        {
            options.DangerouslySkipPermissions = true;
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
    /// 显示帮助信息 — 使用生成器自动生成的分类帮助
    /// </summary>
    public static void ShowHelp()
    {
        Cli.TerminalHelper.WriteLine("JoinCode - AI 智能体命令行工具");
        Cli.TerminalHelper.NewLine();
        // 使用 CliOptionGenerator 自动生成的分类帮助文本
        Cli.TerminalHelper.WriteLine(CliArgParser.GetHelpText("categorized"));
        Cli.TerminalHelper.NewLine();
        Cli.TerminalHelper.WriteLine("子命令:");
        Cli.TerminalHelper.WriteLine("  tool                    MCP 工具管理");
        Cli.TerminalHelper.WriteLine("  agent                   智能体管理");
        Cli.TerminalHelper.WriteLine("  code                    代码操作");
        Cli.TerminalHelper.WriteLine("  schema                  输出 CLI 参数定义 JSON（供 Agent 动态查询）");
        Cli.TerminalHelper.NewLine();
        Cli.TerminalHelper.WriteLine("环境变量:");
        Cli.TerminalHelper.WriteLine("  JCC_VENDOR            LLM 供应商 (openai/azure/anthropic/deepseek/sensenova)");
        Cli.TerminalHelper.WriteLine("  JCC_MODEL_ID           模型 ID");
        Cli.TerminalHelper.WriteLine("  JCC_ENDPOINT           API 端点");
        Cli.TerminalHelper.NewLine();
        Cli.TerminalHelper.WriteLine("  OPENAI_API_KEY          OpenAI API Key");
        Cli.TerminalHelper.WriteLine("  ANTHROPIC_API_KEY       Anthropic API Key");
        Cli.TerminalHelper.WriteLine("  DEEPSEEK_API_KEY        DeepSeek API Key");
        Cli.TerminalHelper.WriteLine("  AZURE_OPENAI_API_KEY    Azure OpenAI API Key");
        Cli.TerminalHelper.NewLine();
        Cli.TerminalHelper.WriteLine("  JCC_DEBUGLOG           启用调试日志输出 (1/true/yes)");
        Cli.TerminalHelper.WriteLine("  JCC_LOG_LEVEL          日志级别 (Trace/Debug/Information/Warning/Error)");
        Cli.TerminalHelper.WriteLine("  JCC_LANGUAGE           界面语言 (zh/en)");
        Cli.TerminalHelper.WriteLine("  JCC_CONFIG_PATH        自定义配置文件路径");
        Cli.TerminalHelper.WriteLine("  JCC_PERMISSION_MODE    权限模式 (plan/auto/ask/bypass)");
        Cli.TerminalHelper.WriteLine("  JCC_CLOCK_MODE         时钟模式 (Physical/Fake，调试用)");
        Cli.TerminalHelper.WriteLine("  NO_COLOR               禁用颜色输出（https://no-color.org/ 标准）");
        Cli.TerminalHelper.WriteLine("  APP_NO_TUI             禁用 TUI 交互");
        Cli.TerminalHelper.NewLine();
        Cli.TerminalHelper.WriteLine("退出码:");
        Cli.TerminalHelper.WriteLine("  0     成功");
        Cli.TerminalHelper.WriteLine("  1     通用错误");
        Cli.TerminalHelper.WriteLine("  2     配置错误");
        Cli.TerminalHelper.WriteLine("  3     参数错误");
        Cli.TerminalHelper.WriteLine("  4     API Key 缺失");
        Cli.TerminalHelper.WriteLine("  10    LLM 调用失败");
        Cli.TerminalHelper.WriteLine("  11    工具执行失败");
        Cli.TerminalHelper.WriteLine("  12    MCP 连接失败");
        Cli.TerminalHelper.WriteLine("  130   用户中断 (Ctrl+C)");
        Cli.TerminalHelper.WriteLine("  1234  --await 超时");
        Cli.TerminalHelper.NewLine();
        Cli.TerminalHelper.WriteLine("示例:");
        Cli.TerminalHelper.WriteLine(CliArgParser.GetHelpText("examples"));
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

        // 3. 可执行文件目录 — Release 部署场景
        var envPath = System.IO.Path.Combine(AppContext.BaseDirectory, ".env", "api.json");
        if (System.IO.File.Exists(envPath)) return envPath;

        // 4. 开发环境回退 — 从 bin/Release/net10.0 向上 5 级到项目根
        var projectRoot = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".env", "api.json"));
        if (System.IO.File.Exists(projectRoot)) return projectRoot;

        return null;
    }

    #endregion
}
