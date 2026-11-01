namespace JoinCode.App.Builder;

/// <summary>
/// 引擎应用构建器 — 链式注册模块并构建 Host（ADR 0143 从 cli ApplicationBuilder 拆分的引擎部分）。
/// <para>宿主差异（终端日志/参数解析/帮助输出）留在宿主侧：CLI 宿主经 <see cref="EngineSessionFactory"/> 调用。</para>
/// <para>日志 provider 由宿主注入 — 引擎不感知终端（ConsoleActor 等 CLI 表现层由 CLI 宿主传入）。</para>
/// </summary>
public sealed class EngineApplicationBuilder {
    private readonly List<IAppModule> _modules = [];

    /// <summary>默认构造函数 — 初始化空模块列表</summary>
    public EngineApplicationBuilder() { }

    /// <summary>
    /// 注册模块（泛型版本）
    /// </summary>
    public EngineApplicationBuilder UseModule<TModule>() where TModule : IAppModule, new() {
        _modules.Add(new TModule());
        return this;
    }

    /// <summary>
    /// 注册模块（实例版本）— 供外部宿主（如 GUI/TUI）传入跨项目边界的模块
    /// </summary>
    public EngineApplicationBuilder UseModule(IAppModule module) {
        _modules.Add(module);
        return this;
    }

    /// <summary>
    /// 构建 Host — 按序调用各模块的 ConfigureServices
    /// <para>loggerProvider 非空时替代默认 console 日志（ADR 0100：清除默认 AddConsole，由宿主注入表现层 provider）</para>
    /// </summary>
    public IHost BuildHost(WorkflowConfig config, CommandLineOptions options, ILoggerProvider? loggerProvider = null) {
        var context = new AppModuleContext {
            Options = options,
            Config = config
        };

        var ordered = _modules.OrderBy(m => m.Order).ToList();

        return Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) => {
                services.AddSingleton(config);
                // 视角1 #3: 注册 CommandLineOptions 为单例，供 PermissionConfig 后配置读取
                services.AddSingleton(options);

                foreach (var module in ordered) {
                    module.ConfigureServices(services, context);
                }

                // 视角1 #3: 后配置 PermissionConfig，合并 --allowed-tools / --disallowed-tools CLI 参数
                // 决策: 使用 IOptions 后配置模式，在 Guard 模块的默认配置之后追加，不破坏封装
                // 替代方案已否决: 修改 Guard 模块接口（破坏组件边界）
                services.AddOptions<NetworkRetryOptions>();
                services.AddOptions<PermissionConfig>()
                    .Configure<CommandLineOptions>((permConfig, cliOptions) => {
                        if (cliOptions.AllowedTools is { Count: > 0 }) {
                            foreach (var tool in cliOptions.AllowedTools) {
                                if (!permConfig.AutoApprovedTools.ContainsKey(tool)) {
                                    permConfig.AutoApprovedTools = permConfig.AutoApprovedTools.SetItem(tool, new ToolPermissionRule { ToolName = tool, Description = "From CLI --allowed-tools" });
                                }
                            }
                            Diag.WriteLine($"[MAIN] --allowed-tools 合并 {cliOptions.AllowedTools.Count} 个工具到 PermissionConfig.AutoApprovedTools");
                        }

                        if (cliOptions.DisallowedTools is { Count: > 0 }) {
                            foreach (var tool in cliOptions.DisallowedTools) {
                                if (!permConfig.AutoRejectedTools.ContainsKey(tool)) {
                                    permConfig.AutoRejectedTools = permConfig.AutoRejectedTools.SetItem(tool, new ToolPermissionRule { ToolName = tool, Description = "From CLI --disallowed-tools" });
                                }
                            }
                            Diag.WriteLine($"[MAIN] --disallowed-tools 合并 {cliOptions.DisallowedTools.Count} 个工具到 PermissionConfig.AutoRejectedTools");
                        }
                    });
            })
            .ConfigureLogging(logging => {
                // ADR 0100: 清除 Host.CreateDefaultBuilder() 的默认 AddConsole，避免绕过宿主表现层
                logging.ClearProviders();
                if (loggerProvider is not null)
                    logging.AddProvider(loggerProvider);
                else
                    // Bug 13 修复：AddConsole 默认写 stdout，污染 JSON 输出。设 LogToStandardErrorThreshold=Trace 使全部日志走 stderr
                    logging.AddConsole(consoleOptions => {
                        consoleOptions.FormatterName = "simple";
                        consoleOptions.LogToStandardErrorThreshold = LogLevel.Trace;
                    });

                var minLevelStr = Environment.GetEnvironmentVariable("JCC_LOG_LEVEL");
                var minLevel = minLevelStr switch {
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
    public async Task ConfigureModulesAsync(IServiceProvider services) {
        var ordered = _modules.OrderBy(m => m.Order).ToList();
        foreach (var module in ordered) {
            // P2-5: 迁移到 Diag.WriteLine，统一受 JCC_DEBUGLOG 控制
            Diag.WriteLine($"[MODULE] {module.GetType().Name} start");
            await module.ConfigureAsync(services, CancellationToken.None).ConfigureAwait(false);
            Diag.WriteLine($"[MODULE] {module.GetType().Name} done");
        }
    }

    /// <summary>
    /// 加载配置 — 含 DotEnv 回退
    /// </summary>
    public static async Task<WorkflowConfig> LoadConfigAsync(CommandLineOptions options, IFileSystem fs, IModelConfigLoader? modelConfigLoader = null) {
        var dotEnv = GetDotEnv();
        WorkflowConfig config;

        var configLogLevel = JoinCode.Abstractions.Utils.Diagnostics.Diag.IsDebugLog ? LogLevel.Debug : LogLevel.Warning;
        using var configLoggerFactory = LoggerFactory.Create(b => b.AddProvider(new Core.Configuration.StderrLoggerProvider(configLogLevel)));

        // 启动开销优化 — 先创建 registry 再传给 ConfigLoader，避免 ConfigLoader 构造函数内部重复创建
        // （原流程：ConfigLoader 构造 → ProviderDefinitionRegistry.Create 读 settings.json，
        //   然后 LoadConfigAsync 行 141 又 CreateAsync 读 settings.json — 重复读取）
        modelConfigLoader ??= new ModelConfigLoader();
        var registry = await Core.Configuration.Providers.ProviderDefinitionRegistry.CreateAsync(modelConfigLoader, logger: configLoggerFactory.CreateLogger<Core.Configuration.Providers.ProviderDefinitionRegistry>()).ConfigureAwait(false);

        var loader = new Core.Configuration.ConfigLoader(registry: registry, modelConfigLoader: modelConfigLoader, loggerFactory: configLoggerFactory) {
            SkipProviderValidation = options.SkipProviderValidation
        };

        try {
            config = await loader.LoadConfigAsync(fs).ConfigureAwait(false);
        } catch (ConfigurationException ex) when (ex.Message.Contains("API Key")) {
            if (dotEnv is not null) {
                await dotEnv.ApplyToConfigAsync(fs).ConfigureAwait(false);
                config = await loader.LoadConfigAsync(fs).ConfigureAwait(false);
            } else {
                throw;
            }
        }

        if (dotEnv is not null) {
            config = dotEnv.ApplyToMemory(config, registry);
        }

        // 环境变量优先级最高 — 无论 dotEnv 是否存在，都必须应用环境变量覆盖
        // 修复: 之前 ApplyEnvOverrides 只在 dotEnv != null 时调用，
        // 导致无 .env/api.json 时 JCC_ENDPOINT/JCC_MODEL_ID 等环境变量不生效
        config = await new Core.Configuration.SettingsMapper(registry).ApplyEnvOverridesAsync(config).ConfigureAwait(false);

        // --model 已在 ParseArgs 阶段转为 JCC_MODEL_ID 环境变量，由 EnvOverrideApplier + ApplyEnvOverrides 统一处理
        if (options.IsPipeMode)
            config = config with { PipeEndpoint = new PipeTransportConfig { PipeName = options.PipeName ?? throw new InvalidOperationException("PipeName required in pipe mode") } };

        return config;
    }

    private static Entry.DotEnvConfig? _dotEnvCache;

    private static Entry.DotEnvConfig? GetDotEnv() {
        if (_dotEnvCache is null)
            _dotEnvCache = LoadDotEnvCore();
        return _dotEnvCache;
    }

    private static Entry.DotEnvConfig? LoadDotEnvCore() {
        var envPath = FindDotEnvPath();
        if (envPath is null) return null;
        return Entry.DotEnvConfig.LoadFrom(envPath);
    }

    private static string? FindDotEnvPath() {
        // 1. 当前工作目录 — 用户从任意目录运行 jcc 时查找
        var cwdPath = System.IO.Path.Combine(Environment.CurrentDirectory, ".env", "api.json");
        if (System.IO.File.Exists(cwdPath)) return cwdPath;

        // 2. 项目级 .jcc 目录 — Release 部署场景
        var envPath = System.IO.Path.Combine(AppDataConstants.Paths.DotEnvDirectory, "api.json");
        if (System.IO.File.Exists(envPath)) return envPath;

        // 3. 开发环境回退 — 从 bin/Release/net10.0 向上 5 级到项目根
        var projectRoot = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".env", "api.json"));
        if (System.IO.File.Exists(projectRoot)) return projectRoot;

        return null;
    }
}
