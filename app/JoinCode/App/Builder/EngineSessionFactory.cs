namespace JoinCode.App.Builder;

/// <summary>
/// 引擎会话工厂 — 收拢 LoadConfig + BuildHost + ConfigureModules + ShellCapabilityInit，
/// CLI 和 GUI 统一调用，消除双引擎初始化差异。
/// </summary>
public sealed class EngineSessionFactory
{
    /// <summary>引擎会话创建结果 — 包含调用方所需的全部引擎对象</summary>
    public sealed class Result
    {
        /// <summary>DI 服务提供者 — 用于解析 IExecutionSettingsProvider、IConfigurationService 等辅助服务</summary>
        public required IServiceProvider Services { get; init; }

        /// <summary>聊天服务 — GUI StreamAsync / CLI NonInteractive 的主通道</summary>
        public required IChatService ChatService { get; init; }

        /// <summary>工作流配置 — CLI/GUI 读写 Vendor/ModelId 的共享实例</summary>
        public required WorkflowConfig Config { get; init; }

        /// <summary>Host 实例 — 调用方负责 Dispose</summary>
        public required IHost Host { get; init; }
    }

    /// <summary>
    /// 创建 CLI 引擎会话 — 包含全部 CLI 模块（PipeModule、CliModule、HousekeepingModule）。
    /// CLI 的 Program.Main 调用此方法，替代手动 LoadConfig+BuildHost+ConfigureModules。
    /// </summary>
    public static Task<Result> CreateCliSessionAsync(
        CommandLineOptions options, IFileSystem fs, CancellationToken cancellationToken = default)
    {
        return CreateCoreAsync(
            options, fs,
            builder => builder
                .UseModule<Modules.CoreModule>()
                .UseModule<Modules.ClockModule>()
                .UseModule<Modules.BrowserModule>()
                .UseModule<Modules.PipeModule>()
                .UseModule<Modules.CliModule>()
                .UseModule<Modules.HousekeepingModule>()
                .UseModule<Modules.McpInitModule>(),
            cancellationToken);
    }

    /// <summary>
    /// 创建 GUI 引擎会话 — 不含 PipeModule/CliModule（GUI 不需要命名管道和终端交互），
    /// 含 HousekeepingModule（后台清理和实体回收）。
    /// extraModules: GUI 可传入额外模块（如 GuiInteractionModule）覆盖 Core 层默认注册。
    /// </summary>
    public static Task<Result> CreateGuiSessionAsync(
        IEnumerable<IAppModule>? extraModules = null,
        CancellationToken cancellationToken = default)
    {
        var fs = IO.FileSystem.FileSystemFactory.Create();
        var options = new CommandLineOptions
        {
            NonInteractive = true,
            TrustWorkspace = true,
        };

        return CreateCoreAsync(
            options, fs,
            builder =>
            {
                builder = builder
                    .UseModule<Modules.CoreModule>()
                    .UseModule<Modules.ClockModule>()
                    .UseModule<Modules.BrowserModule>()
                    .UseModule<Modules.HousekeepingModule>()
                    .UseModule<Modules.McpInitModule>();
                if (extraModules is not null)
                {
                    foreach (var module in extraModules)
                        builder = builder.UseModule(module);
                }
                return builder;
            },
            cancellationToken,
            clearPipeEndpoint: true);
    }

    /// <summary>
    /// 核心创建逻辑 — LoadConfig → BuildHost → ConfigureModules → ShellCapabilityInit。
    /// CLI 和 GUI 的差异仅在于模块列表和 PipeEndpoint 处理，其余全部统一。
    /// </summary>
    private static async Task<Result> CreateCoreAsync(
        CommandLineOptions options,
        IFileSystem fs,
        Func<ApplicationBuilder, ApplicationBuilder> configureModules,
        CancellationToken cancellationToken,
        bool clearPipeEndpoint = false)
    {
        await new Entry.StartupWorkflow().EnsureConfigFilesExistAsync(fs).ConfigureAwait(false);

        var config = await ApplicationBuilder.LoadConfigAsync(options, fs).ConfigureAwait(false);

        if (clearPipeEndpoint)
            config.PipeEndpoint = null;

        var builder = configureModules(new ApplicationBuilder());

        var host = builder.BuildHost(config, options);

        await builder.ConfigureModulesAsync(host.Services).ConfigureAwait(false);

        Core.DependencyInjection.ShellCapabilityInitializer.Initialize(
            fs, host.Services.GetService<ILogger<EngineSessionFactory>>());

        StartModelFetchBackground(fs, host.Services, cancellationToken);

        var chatService = host.Services.GetRequiredService<IChatService>();

        return new Result
        {
            Services = host.Services,
            ChatService = chatService,
            Config = config,
            Host = host,
        };
    }

    /// <summary>
    /// 启动非阻塞并行模型列表拉取 — 后台执行，失败不影响启动
    /// 拉取完成后写回 settings.json，由现有文件监控链路自动刷新内存和GUI
    /// </summary>
    private static void StartModelFetchBackground(
        IFileSystem fs,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var settings = await SettingsLoader.LoadUserSettingsAsync(fs, cancellationToken).ConfigureAwait(false);
                if (settings is null || !settings.AutoFetchModels) return;

                var httpProvider = HttpClientProviderFactory.Create();
                var changeNotifier = services.GetService<IConfigChangeNotifier>();
                var fetcher = new ModelListFetcher(httpProvider, services.GetService<ILogger<ModelListFetcher>>());
                var writer = new SettingsJsonModelWriter(fs, changeNotifier, services.GetService<ILogger<SettingsJsonModelWriter>>());
                var startupService = new ModelFetchStartupService(fetcher, writer, services.GetService<ILogger<ModelFetchStartupService>>());

                await startupService.ExecuteAsync(settings, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                services.GetService<ILogger<EngineSessionFactory>>()?.LogWarning(ex, "[EngineSessionFactory] 模型列表后台拉取失败，不影响启动");
            }
        }, cancellationToken);
    }
}
