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

                // SkipChatServices=true 且未 SkipToolFilter 时只注册 gh_* 工具 — jcc gh/mcp_call 等元命令不需要 file/shell/search 等工具
                // mcp serve 需要暴露所有工具给外部客户端，设 SkipToolFilter=true 跳过过滤
                if (options.SkipChatServices && !options.SkipToolFilter)
                    services.AddSingleton<Func<string, bool>>(static name => name.StartsWith("gh_", StringComparison.OrdinalIgnoreCase));

                foreach (var module in ordered) {
                    var sw = PerfTimer.Start($"  ConfigureServices {module.GetType().Name}");
                    module.ConfigureServices(services, context);
                    sw?.Stop();
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
            var sw = PerfTimer.Start($"  模块 {module.GetType().Name}");
            await module.ConfigureAsync(services, CancellationToken.None).ConfigureAwait(false);
            sw?.Stop();
            Diag.WriteLine($"[MODULE] {module.GetType().Name} done");
        }
    }

    /// <summary>
    /// 加载配置 — 含 DotEnv 回退
    /// ADR 0155: 当 Hub 服务可用时，跳过 Provider 配置加载，直接使用 Hub 协议
    /// </summary>
    public static async Task<WorkflowConfig> LoadConfigAsync(CommandLineOptions options, IFileSystem fs, IModelConfigLoader? modelConfigLoader = null) {
        var sw = PerfTimer.Start("  GetDotEnv");
        var dotEnv = GetDotEnv();
        sw?.Stop();
        WorkflowConfig config;

        var configLogLevel = JoinCode.Abstractions.Utils.Diagnostics.Diag.IsDebugLog ? LogLevel.Debug : LogLevel.Warning;
        using var configLoggerFactory = LoggerFactory.Create(b => b.AddProvider(new Core.Configuration.StderrLoggerProvider(configLogLevel)));

        // ADR 0155: Hub 模式 — 当 Hub 服务可用时，跳过所有 Provider 配置加载
        // 客户端不需要 ProviderDefinitionRegistry、API Key 解析、Provider 验证
        // 所有 Provider 相关逻辑由 jcchub.exe 服务端处理
        if (await IsHubServerAvailableAsync().ConfigureAwait(false)) {
            Diag.WriteLine("[HUB] Hub 服务可用，使用 Hub 协议 — 跳过 Provider 配置加载");
            config = await LoadHubConfigAsync(fs, options).ConfigureAwait(false);
            return config;
        }

        // 启动开销优化 — 先创建 registry 再传给 ConfigLoader，避免 ConfigLoader 构造函数内部重复创建
        modelConfigLoader ??= new ModelConfigLoader();
        var sw1 = PerfTimer.Start("  ProviderRegistry.Create");
        var registry = await Core.Configuration.Providers.ProviderDefinitionRegistry.CreateAsync(modelConfigLoader, logger: configLoggerFactory.CreateLogger<Core.Configuration.Providers.ProviderDefinitionRegistry>()).ConfigureAwait(false);
        sw1?.Stop();

        var loader = new Core.Configuration.ConfigLoader(registry: registry, modelConfigLoader: modelConfigLoader, loggerFactory: configLoggerFactory) {
            SkipProviderValidation = options.SkipProviderValidation
        };

        var sw2 = PerfTimer.Start("  ConfigLoader.Load");
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
        sw2?.Stop();

        var sw3 = PerfTimer.Start("  DotEnv+EnvOverrides");
        if (dotEnv is not null) {
            config = dotEnv.ApplyToMemory(config, registry);
        }

        // 环境变量优先级最高 — 无论 dotEnv 是否存在，都必须应用环境变量覆盖
        config = await new Core.Configuration.SettingsMapper(registry).ApplyEnvOverridesAsync(config).ConfigureAwait(false);
        sw3?.Stop();

        // --model 已在 ParseArgs 阶段转为 JCC_MODEL_ID 环境变量，由 EnvOverrideApplier + ApplyEnvOverrides 统一处理
        if (options.IsPipeMode)
            config = config with { PipeEndpoint = new PipeTransportConfig { PipeName = options.PipeName ?? throw new InvalidOperationException("PipeName required in pipe mode") } };

        return config;
    }

    /// <summary>
    /// ADR 0155: Hub 模式配置加载 — 跳过所有 Provider 配置，直接使用 Hub 协议
    /// 客户端只需要 Hub endpoint（固定 http://localhost:3457），不需要 API Key、ProviderDefinitionRegistry
    /// </summary>
    private static async Task<WorkflowConfig> LoadHubConfigAsync(IFileSystem fs, CommandLineOptions options) {
        const string hubEndpoint = "http://localhost:3457";

        // 只加载 settings.json 的 current 部分（UI 偏好等），不加载 vendor 部分
        var settings = await Core.Configuration.ConfigLoader.LoadSettingsJsonAsync(fs).ConfigureAwait(false);

        // 生成 Agent ID 并向 Hub 服务端注册
        var agentId = $"agent-{Guid.NewGuid():N}"[..16];
        await RegisterAgentAsync(hubEndpoint, agentId).ConfigureAwait(false);

        // 构造使用 Hub 协议的简化 WorkflowConfig
        var config = new WorkflowConfig {
            Provider = new ProviderConfig {
                Vendor = "hub",
                Protocol = ProtocolKind.Hub.ToValue(),
                Endpoint = hubEndpoint,
                ApiKey = string.Empty,
                ModelId = string.Empty,
                Definition = null,
                AgentId = agentId,
            }
        };

        // 应用非 Provider 相关的 settings.current 配置（UI 偏好、行为开关等）
        if (settings is not null) {
            config = config with { FastMode = settings.Current?.FastMode ?? false };
            config = config with { Language = settings.Current?.Language };
            config = config with { OutputStyle = settings.Current?.OutputStyle };
            config = config with { AutoMemoryEnabled = settings.Current?.AutoMemoryEnabled ?? true };
            config = config with { AutoDreamEnabled = settings.Current?.AutoDreamEnabled ?? true };
            config = config with { CleanupPeriodDays = settings.Current?.CleanupPeriodDays ?? 30 };
            config = config with { ShowTurnDuration = settings.Current?.ShowTurnDuration ?? true };
            config = config with { AutoCompactEnabled = settings.Current?.AutoCompactEnabled ?? true };
            config = config with { FileCheckpointingEnabled = settings.Current?.FileCheckpointingEnabled ?? true };

            // 注入 settings.env 到环境变量
            Core.Configuration.SettingsMapper.InjectEnvFromSettings(settings);

            if (settings.Current?.DebugLog == true) {
                Diag.EnableDebugLog();
            }
        }

        // 从服务端 /v1/models 获取可用模型列表，优先使用用户配置的模型
        var configuredModel = settings?.GetActiveProfile()?.Model;
        try {
            using var modelClient = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var modelsResponse = await modelClient.GetAsync("http://localhost:3457/v1/models").ConfigureAwait(false);
            if (modelsResponse.IsSuccessStatusCode) {
                var modelsJson = await modelsResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                var modelsData = RelaxedJsonSerializer.Deserialize(modelsJson, HubModelsJsonContext.Default.HubModelsResponse);
                if (modelsData?.Data is not null && modelsData.Data.Count > 0) {
                    var selectedModel = SelectHubModel(modelsData.Data, configuredModel);
                    config = config with { Provider = config.Provider with { ModelId = selectedModel.Id } };
                    Diag.WriteLine($"[HUB] 从服务端获取 {modelsData.Data.Count} 个模型，使用: {selectedModel.Id} (provider={selectedModel.Provider})");
                }
            }
        } catch (Exception ex) {
            Diag.WriteLine($"[HUB] 从服务端获取模型列表失败: {ex.Message}");
            // 兜底：使用配置的模型名（即使无法验证是否可用）
            if (!string.IsNullOrEmpty(configuredModel)) {
                config = config with { Provider = config.Provider with { ModelId = configuredModel } };
                Diag.WriteLine($"[HUB] 服务端不可达，使用配置的模型: {configuredModel}");
            }
        }

        // 加载规则文件
        var projectDir = fs.GetCurrentDirectory();
        await using var rulesLoader = new Core.Configuration.ProjectRulesLoader(fs);
        var projectRules = await rulesLoader.LoadRulesAsync(projectDir).ConfigureAwait(false);
        var externalRulesLoader = new Core.Configuration.ExternalRulesLoader(fs);
        var externalRules = await externalRulesLoader.LoadProjectRulesAsync(projectDir).ConfigureAwait(false);

        config = config with {
            ProjectRules = projectRules,
            ExternalRules = externalRules
        };

        // Pipe 模式
        if (options.IsPipeMode)
            config = config with { PipeEndpoint = new PipeTransportConfig { PipeName = options.PipeName ?? throw new InvalidOperationException("PipeName required in pipe mode") } };

        return config;
    }

    private static async Task<bool> IsHubServerAvailableAsync() {
        try {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
            var response = await client.GetAsync("http://localhost:3457/health").ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        } catch {
            return false;
        }
    }

    /// <summary>
    /// 从服务端返回的模型列表中选择模型 — 优先使用用户配置的模型
    /// </summary>
    private static HubModelInfo SelectHubModel(List<HubModelInfo> availableModels, string? configuredModel) {
        if (!string.IsNullOrEmpty(configuredModel)) {
            var match = availableModels.FirstOrDefault(m =>
                string.Equals(m.Id, configuredModel, StringComparison.OrdinalIgnoreCase));
            if (match is not null) {
                Diag.WriteLine($"[HUB] 配置的模型 {configuredModel} 在可用列表中，使用它");
                return match;
            }
            Diag.WriteLine($"[HUB] 配置的模型 {configuredModel} 不在可用列表中，使用第一个可用模型: {availableModels[0].Id}");
        }
        return availableModels[0];
    }

    /// <summary>
    /// 向 Hub 服务端注册当前 Agent
    /// </summary>
    private static async Task RegisterAgentAsync(string hubEndpoint, string agentId) {
        try {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var requestBody = JsonSerializer.Serialize(
                new HubAgentRegisterRequest {
                    AgentId = agentId,
                    SessionId = $"session-{Guid.NewGuid():N}"[..18],
                    Capabilities = [],
                },
                HubModelsJsonContext.Default.HubAgentRegisterRequest);
            using var content = new StringContent(requestBody, Encoding.UTF8, "application/json");
            var response = await client.PostAsync($"{hubEndpoint}/agent/register", content).ConfigureAwait(false);
            if (response.IsSuccessStatusCode) {
                Diag.WriteLine($"[HUB] Agent 注册成功: {agentId}");
            } else {
                Diag.WriteLine($"[HUB] Agent 注册失败: {response.StatusCode}");
            }
        } catch (Exception ex) {
            Diag.WriteLine($"[HUB] Agent 注册异常: {ex.Message}");
        }
    }

    /// <summary>
    /// 向 Hub 服务端注销当前 Agent
    /// </summary>
    public static async Task UnregisterAgentAsync(string hubEndpoint, string agentId) {
        if (string.IsNullOrEmpty(agentId)) return;
        try {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var requestBody = JsonSerializer.Serialize(
                new HubAgentUnregisterRequest { AgentId = agentId },
                HubModelsJsonContext.Default.HubAgentUnregisterRequest);
            using var content = new StringContent(requestBody, Encoding.UTF8, "application/json");
            var response = await client.PostAsync($"{hubEndpoint}/agent/unregister", content).ConfigureAwait(false);
            if (response.IsSuccessStatusCode) {
                Diag.WriteLine($"[HUB] Agent 注销成功: {agentId}");
            }
        } catch (Exception ex) {
            Diag.WriteLine($"[HUB] Agent 注销异常: {ex.Message}");
        }
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
