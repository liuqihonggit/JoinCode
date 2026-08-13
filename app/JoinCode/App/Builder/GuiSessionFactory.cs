namespace JoinCode.App.Builder;

/// <summary>
/// GUI 会话工厂 — 收拢 LoadConfig + BuildHost + ConfigureModules 为一个函数，
/// 供 GUI 引用 JoinCode 项目后一行调用，消除 GUI 和 CLI 的双引擎初始化差异。
/// </summary>
public sealed class GuiSessionFactory
{
    /// <summary>GUI 会话创建结果 — 包含 GUI 所需的全部引擎对象</summary>
    public sealed class Result
    {
        /// <summary>DI 服务提供者 — 用于解析 IExecutionSettingsProvider、IConfigurationService 等辅助服务</summary>
        public required IServiceProvider Services { get; init; }

        /// <summary>聊天服务 — GUI StreamAsync 的主通道</summary>
        public required IChatService ChatService { get; init; }

        /// <summary>工作流配置 — GUI 读写 CurrentVendor/CurrentModelId 的共享实例</summary>
        public required WorkflowConfig Config { get; init; }

        /// <summary>Host 实例 — 调用方负责 Dispose</summary>
        public required IHost Host { get; init; }
    }

    /// <summary>
    /// 创建 GUI 引擎会话 — 一行调用完成：加载配置 → 构建 DI Host → 模块初始化（含 MCP）。
    /// 返回的 Host 需要由调用方 Dispose。
    /// </summary>
    public static async Task<Result> CreateSessionAsync(CancellationToken cancellationToken = default)
    {
        var fs = IO.FileSystem.FileSystemFactory.Create();

        await Entry.StartupWorkflow.EnsureConfigFilesExistAsync(fs).ConfigureAwait(false);

        var options = new CommandLineOptions
        {
            NonInteractive = true,
            TrustWorkspace = true,
        };

        var config = await ApplicationBuilder.LoadConfigAsync(options, fs).ConfigureAwait(false);

        config.PipeEndpoint = null;

        var builder = new ApplicationBuilder()
            .UseModule<Modules.CoreModule>()
            .UseModule<Modules.ClockModule>()
            .UseModule<Modules.BrowserModule>()
            .UseModule<Modules.HousekeepingModule>()
            .UseModule<Modules.McpInitModule>();

        var host = builder.BuildHost(config, options);

        await builder.ConfigureModulesAsync(host.Services).ConfigureAwait(false);

        Core.DependencyInjection.ShellCapabilityInitializer.Initialize(
            fs, host.Services.GetService<ILogger<GuiSessionFactory>>());

        var chatService = host.Services.GetRequiredService<IChatService>();

        return new Result
        {
            Services = host.Services,
            ChatService = chatService,
            Config = config,
            Host = host,
        };
    }
}
