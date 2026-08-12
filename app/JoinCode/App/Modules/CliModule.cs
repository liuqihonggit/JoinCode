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
        services.AddSingleton<ChatCommandRegistry>(sp =>
        {
            var registry = new ChatCommandRegistry();
            GeneratedCommandRegistration.RegisterAllChatCommands(registry);
            return registry;
        });
        services.AddSingleton<ISlashCommandRegistry>(sp => sp.GetRequiredService<ChatCommandRegistry>());

        // 注册 ICmdMap 门面 — 解析 ISlashCommandRegistry + IToolRegistry
        services.AddSingleton<ICmdMap>(sp =>
        {
            var slash = sp.GetRequiredService<ISlashCommandRegistry>();
            var mcp = sp.GetRequiredService<IToolRegistry>();
            return new CmdMap(slash, mcp);
        });
    }

    public async Task ConfigureAsync(IServiceProvider services, CancellationToken ct)
    {
        // 将 ExposeToMcp=true 的斜杠命令注册为 MCP 工具（通过 SlashToMcpAdapter 包装）
        // 这样 LLM 能通过现有 MCP 管线发现和调用斜杠命令
        var commandRegistry = services.GetService<ISlashCommandRegistry>();
        var toolRegistry = services.GetService<IToolRegistry>();
        if (commandRegistry is not null && toolRegistry is not null)
        {
            foreach (var kvp in commandRegistry.GetAllCommands())
            {
                if (kvp.Value is ChatCommandBase { ExposeToMcp: true } cmd)
                {
                    var adapter = new SlashToMcpAdapter(cmd, services, cmd.Kind);
                    await toolRegistry.RegisterToolAsync(adapter, ct).ConfigureAwait(false);
                }
            }
        }
    }
}
