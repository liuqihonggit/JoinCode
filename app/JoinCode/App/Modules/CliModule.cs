namespace JoinCode.App.Modules;

/// <summary>
/// CLI 模块 — 注册 CLI 专属服务（预览模式等条件注册）
/// </summary>
[AppModule(Order = 80)]
public sealed class CliModule : IAppModule
{
    public int Order => 80;

    public void ConfigureServices(IServiceCollection services, AppModuleContext context)
    {
        services.AddSingleton<IInteractiveService, TerminalInteractiveService>();

        // 注册 ChatCommandRegistry — 工厂内完成命令注册
        services.AddSingleton(sp =>
        {
            var registry = new ChatCommandRegistry();
            GeneratedCommandRegistration.RegisterAllChatCommands(registry);
            return registry;
        });

        // 注册 ICmdMap 门面 — 解析 ChatCommandRegistry + IToolRegistry
        services.AddSingleton<ICmdMap>(sp =>
        {
            var slash = sp.GetRequiredService<ChatCommandRegistry>();
            var mcp = sp.GetRequiredService<IToolRegistry>();
            return new CmdMap(slash, mcp);
        });
    }

    public Task ConfigureAsync(IServiceProvider services, CancellationToken ct)
        => Task.CompletedTask;
}
