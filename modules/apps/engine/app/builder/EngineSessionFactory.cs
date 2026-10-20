namespace JoinCode.App.Builder;

/// <summary>
/// 异步可释放 Host — 同时实现 <see cref="IHost"/> + <see cref="IAsyncDisposable"/>。
/// <para>Microsoft.Extensions.Hosting 的 IHost 接口只暴露同步 Dispose,但运行时 Host 实例实现 IAsyncDisposable。</para>
/// <para>此接口让编译时类型暴露 DisposeAsync,使调用方可用 <c>await using</c> 正确异步释放,</para>
/// <para>避免 <c>using var</c> 调同步 Dispose 在持有异步资源(Actor/Channel)时阻塞线程池导致 Consumer 死锁。</para>
/// </summary>
public interface IAsyncHost : IHost, IAsyncDisposable;

/// <summary>
/// 异步 Host 包装器 — 将 <see cref="IHost"/> 包装为 <see cref="IAsyncHost"/>。
/// <para>运行时 Host 实例已实现 IAsyncDisposable,此包装器仅让编译时类型暴露 DisposeAsync。</para>
/// <para>释放顺序保证:DisposeAsync 先收尾 <see cref="JoinCode.App.Modules.McpPluginBootstrapHandle"/>
/// (取消 + 等后台插件链完成)再释放内层 Host — 容器本体 dispose 先于后台链收尾时,插件
/// InitializeAsync 内服务解析撞 ObjectDisposedException(退出竞态,全宿主统一在此收口)。</para>
/// </summary>
internal sealed class AsyncHostWrapper : IAsyncHost {
    private readonly IHost _inner;
    /// <summary>构造函数 — 包装指定的 <paramref name="inner"/> Host 实例</summary>
    public AsyncHostWrapper(IHost inner) => _inner = inner;
    /// <summary>获取 DI 服务提供者 — 透传到内部 Host</summary>
    public IServiceProvider Services => _inner.Services;
    /// <summary>启动 Host — 透传到内部 Host</summary>
    public Task StartAsync(CancellationToken cancellationToken = default) => _inner.StartAsync(cancellationToken);
    /// <summary>停止 Host — 透传到内部 Host</summary>
    public Task StopAsync(CancellationToken cancellationToken = default) => _inner.StopAsync(cancellationToken);
    /// <summary>同步释放 — 透传到内部 Host（后台链收尾由容器自身释放 Handle 单例兜底；
    /// 全量保序走 DisposeAsync 主路径，同步 Dispose 是死锁高危遗留调用面不动原语义）</summary>
    public void Dispose() => _inner.Dispose();
    /// <summary>异步释放 — 先收尾后台插件链（取消 + 无超时等待链完成，容器存活期内断链），
    /// 再释放内部 Host(优先 DisposeAsync,回退同步 Dispose)</summary>
    public async ValueTask DisposeAsync() {
        if (_inner.Services.GetService<JoinCode.App.Modules.McpPluginBootstrapHandle>() is { } bootstrap)
            await bootstrap.DisposeAsync().ConfigureAwait(false);
        if (_inner is IAsyncDisposable ad)
            await ad.DisposeAsync().ConfigureAwait(false);
        else
            _inner.Dispose();
    }
}

/// <summary>
/// 引擎会话工厂 — 收拢 LoadConfig + BuildHost + ConfigureModules + ShellCapabilityInit，
/// CLI 和 GUI 统一调用，消除双引擎初始化差异。
/// </summary>
public sealed class EngineSessionFactory {
    /// <summary>引擎会话创建结果 — 包含调用方所需的全部引擎对象</summary>
    public sealed class Result {
        /// <summary>DI 服务提供者 — 用于解析 IExecutionSettingsProvider、IConfigurationService 等辅助服务</summary>
        public required IServiceProvider Services { get; init; }

        /// <summary>聊天服务 — GUI StreamAsync / CLI NonInteractive 的主通道</summary>
        public required IChatService ChatService { get; init; }

        /// <summary>工作流配置 — CLI/GUI 读写 Vendor/ModelId 的共享实例</summary>
        public required WorkflowConfig Config { get; init; }

        /// <summary>
        /// 引擎会话 ID — 工厂统一生成并已 SwitchSession 注入 IChatContextManager，
        /// transcript 落盘/内存桶/meta 全部以此为准；调用方（CliSession 等）复用此值，
        /// 禁止各自再 Generate 造成双源分裂（对齐"sessionId 唯一数据源=引擎"原则）
        /// </summary>
        public required string SessionId { get; init; }

        /// <summary>异步可释放 Host — 调用方用 <c>await using</c> 确保异步 DisposeAsync 释放,避免同步 Dispose 线程池死锁</summary>
        public required IAsyncHost Host { get; init; }
    }

    /// <summary>
    /// 创建 GUI/TUI 引擎会话 — 不含 PipeModule/CliModule（宿主不需要命名管道和终端交互），
    /// 含 HousekeepingModule（后台清理和实体回收）。
    /// extraModules: 宿主可传入额外模块（如 GuiInteractionModule/TuiInteractionModule/CliRegistrationModule）。
    /// loggerProvider: 宿主日志 provider（ADR 0100 表现层注入）；null 时引擎回退默认 console —
    /// TUI 宿主必须传静默 provider（Console 日志直写 stderr 会与 inline 渲染交错，光标追踪错乱致残影）。
    /// </summary>
    public static Task<Result> CreateGuiSessionAsync(
        IEnumerable<IAppModule>? extraModules = null,
        ILoggerProvider? loggerProvider = null,
        CancellationToken cancellationToken = default) {
        var fs = IO.FileSystem.FileSystemFactory.Create();
        var options = new CommandLineOptions {
            NonInteractive = true,
            TrustWorkspace = true,
        };

        List<IAppModule> modules =
        [
            new Modules.CoreModule(),
            new Modules.ClockModule(),
            new Modules.BrowserModule(),
            new Modules.HousekeepingModule(),
            new Modules.McpInitModule(),
        ];
        if (extraModules is not null)
            modules.AddRange(extraModules);

        return CreateSessionAsync(options, fs, modules, loggerProvider, cancellationToken, clearPipeEndpoint: true);
    }

    /// <summary>
    /// 通用会话创建 — LoadConfig → BuildHost → ConfigureModules → ShellCapabilityInit。
    /// 模块列表与日志 provider 由宿主决定（CLI 宿主经 CliSessionFactory 传入 CLI 模块与 ConsoleActor provider）。
    /// </summary>
    /// <param name="options">启动选项 — 引擎配置加载与 DI 注册共用</param>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="modules">宿主选定的模块列表（按各自 Order 排序执行）</param>
    /// <param name="loggerProvider">宿主日志 provider（ADR 0100 表现层注入）；null 时引擎回退默认 console</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <param name="clearPipeEndpoint">是否清除 PipeEndpoint 配置（GUI/TUI 会话无命名管道）</param>
    public static async Task<Result> CreateSessionAsync(
        CommandLineOptions options,
        IFileSystem fs,
        IEnumerable<IAppModule> modules,
        ILoggerProvider? loggerProvider = null,
        CancellationToken cancellationToken = default,
        bool clearPipeEndpoint = false) {
        await EngineConfigInitializer.EnsureConfigFilesExistAsync(fs).ConfigureAwait(false);

        var modelConfigLoader = new ModelConfigLoader();
        var config = await EngineApplicationBuilder.LoadConfigAsync(options, fs, modelConfigLoader).ConfigureAwait(false);

        if (clearPipeEndpoint)
            config = config with { PipeEndpoint = null };

        var builder = new EngineApplicationBuilder();
        foreach (var module in modules)
            builder = builder.UseModule(module);

        var host = builder.BuildHost(config, options, loggerProvider);

        await builder.ConfigureModulesAsync(host.Services).ConfigureAwait(false);

        // 统一会话 ID — 工厂生成一次，引擎内存桶 + transcript 落盘 + 调用方（CliSession 等）三方同源。
        // 此前 CLI/GUI/TUI 各自管理 sessionId（CLI=CliSession.Generate、GUI=GUID、TUI=引擎 default），
        // transcript 落盘下沉到 TranscriptPersistMiddleware 后，sessionId 唯一数据源必须是引擎
        var engineSessionId = SessionIdGenerator.Generate();
        host.Services.GetService<IChatContextManager>()?.SwitchSession(engineSessionId);

        SyncModelConfigToDi(host.Services, modelConfigLoader);

        Core.DependencyInjection.ShellCapabilityInitializer.Initialize(
            fs, host.Services.GetService<ILogger<EngineSessionFactory>>());

        if (!options.SkipModelFetch)
            StartModelFetchBackground(fs, host.Services, cancellationToken);

        var chatService = host.Services.GetRequiredService<IChatService>();

        return new Result {
            Services = host.Services,
            ChatService = chatService,
            Config = config,
            SessionId = engineSessionId,
            Host = new AsyncHostWrapper(host),
        };
    }

    /// <summary>
    /// 将预创建的 ModelConfigLoader 数据同步到 DI 单例
    /// <para>LoadConfigAsync 在 DI 容器构建前运行，用独立 ModelConfigLoader 实例加载 providers</para>
    /// <para>DI 容器构建后 IModelConfigLoader 单例为空，需将 providers 数据灌入 DI 单例</para>
    /// <para>否则 SessionController.DetectModalityMismatch 从 DI 获取空实例，导致模态校验误报</para>
    /// </summary>
    private static void SyncModelConfigToDi(IServiceProvider services, IModelConfigLoader source) {
        var diModelConfigLoader = services.GetService<IModelConfigLoader>();
        if (diModelConfigLoader is null || diModelConfigLoader == source) return;

        diModelConfigLoader.ApplyProviders(source.Config.Providers);
    }

    /// <summary>
    /// 启动非阻塞并行模型列表拉取 — 后台执行，失败不影响启动
    /// 拉取完成后写回 settings.json，由现有文件监控链路自动刷新内存和GUI
    /// </summary>
    private static void StartModelFetchBackground(
        IFileSystem fs,
        IServiceProvider services,
        CancellationToken cancellationToken) {
        _ = Task.Run(async () => {
            try {
                var settings = await SettingsLoader.LoadUserSettingsAsync(fs, cancellationToken).ConfigureAwait(false);
                if (settings is null || !settings.AutoFetchModels) return;

                var httpProvider = HttpClientProviderFactory.Create();
                var changeNotifier = services.GetService<IConfigChangeNotifier>();
                var fetcher = new ModelListFetcher(httpProvider, fs, services.GetService<ILogger<ModelListFetcher>>());
                var writer = new SettingsJsonModelWriter(fs, changeNotifier, services.GetService<ILogger<SettingsJsonModelWriter>>());
                var startupService = new ModelFetchStartupService(fetcher, writer, services.GetService<ISettingsChangeApplier>(), services.GetService<ILogger<ModelFetchStartupService>>());

                await startupService.ExecuteAsync(settings, cancellationToken).ConfigureAwait(false);
            } catch (Exception ex) {
                services.GetService<ILogger<EngineSessionFactory>>()?.LogWarning(ex, "[EngineSessionFactory] 模型列表后台拉取失败，不影响启动");
            }
        }, cancellationToken);
    }
}