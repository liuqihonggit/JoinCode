namespace JoinCode.App.Modules;

/// <summary>
/// 斜杠命令模块 — GUI/TUI 引擎链的 ChatCommandRegistry 注册点（TASK065 F4）：
/// 注册前 SlashCommandRunner.GetService&lt;ChatCommandRegistry&gt; 永远 null（全仓无注册点）→
/// 每条命令 new + GeneratedCommandRegistration 全量重建（功能对但纯开销）；
/// 此处工厂内一次性全量注册为单例，Runner DI 优先解析复用。
/// 注册形态对齐 CLI 宿主 CliModule 先例（CliModule 属 cli 宿主不在 GUI/TUI 模块链 —
/// CLI 链由 CliModule 自有注册，两处独立互不干扰）。
/// </summary>
[AppModule(Order = 50)]
public sealed class SlashCommandModule : IAppModule {
    /// <summary>模块加载顺序 — 值为 50（核心工作流 CoreModule(30)/时钟 ClockModule(40) 之后、浏览器 BrowserModule(60) 之前）。</summary>
    public int Order => 50;

    /// <summary>
    /// 注册斜杠命令服务 — ChatCommandRegistry 单例（工厂惰性构建，工厂内完成命令全量注册）
    /// + ISlashCommandRegistry 接口转发（CmdMap 门面等接口消费方解析面）。
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="context">模块上下文（斜杠命令注册不依赖配置）</param>
    public void ConfigureServices(IServiceCollection services, AppModuleContext context) {
        services.AddSingleton<ChatCommandRegistry>(sp => {
            var registry = new ChatCommandRegistry();
            GeneratedCommandRegistration.RegisterAllChatCommands(registry);
            return registry;
        });
        services.AddSingleton<ISlashCommandRegistry>(sp => sp.GetRequiredService<ChatCommandRegistry>());
    }

    /// <summary>
    /// 异步配置 — 斜杠命令模块无需异步初始化，直接返回完成。
    /// </summary>
    /// <param name="services">服务提供者</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>已完成的任务</returns>
    public Task ConfigureAsync(IServiceProvider services, CancellationToken ct)
        => Task.CompletedTask;
}
