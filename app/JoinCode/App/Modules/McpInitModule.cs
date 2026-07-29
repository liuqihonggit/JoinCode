namespace JoinCode.App.Modules;

/// <summary>
/// MCP 初始化模块 — 启动后初始化 MCP 服务和桥接
/// </summary>
[AppModule(Order = 90)]
public sealed class McpInitModule : IAppModule
{
    public int Order => 90;

    public void ConfigureServices(IServiceCollection services, AppModuleContext context)
    {
    }

    public async Task ConfigureAsync(IServiceProvider services, CancellationToken ct)
    {
        var logger = services.GetService<ILogger<McpInitModule>>();

        var remoteClientManager = services.GetRequiredService<RemoteClientManager>();
        var syncBridge = services.GetRequiredService<McpToolSyncBridge>();

        remoteClientManager.ToolsListChanged += async (_, _) =>
        {
            await syncBridge.OnToolsListChangedAsync().ConfigureAwait(false);
        };

        remoteClientManager.ResourcesListChanged += async (_, args) =>
        {
            await syncBridge.OnResourcesListChangedAsync(args.ClientId, args.SyncResult).ConfigureAwait(false);
        };

        remoteClientManager.PromptsListChanged += async (_, args) =>
        {
            await syncBridge.OnPromptsListChangedAsync(args.ClientId, args.SyncResult).ConfigureAwait(false);
        };

        services.WirePluginSkillBridge();

        try
        {
            var pluginManager = services.GetRequiredService<Core.Plugins.IPluginManager>();
            await pluginManager.LoadWorkflowPluginAsync<JoinCode.Dream.DreamPlugin>(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "[MCP] LoadDreamPlugin failed");
        }

        try
        {
            using var cts = TimeoutHelper.CreateLinkedTimeout(ct, TimeSpan.FromSeconds(5));
            var mcpService = services.GetRequiredService<IMcpService>();
            await mcpService.InitializeAsync(services, cts.Token).ConfigureAwait(false);

            var toolsBridge = services.GetRequiredService<Core.DependencyInjection.McpToolSyncBridge>();
            await toolsBridge.OnToolsListChangedAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            logger?.LogWarning("[MCP] InitializeAsync timed out after 5s");
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "[MCP] InitializeAsync failed");
        }
    }
}
