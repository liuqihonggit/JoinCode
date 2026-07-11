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

        Console.Error.WriteLine("[MCP] WireMcpToolSyncBridge start");
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
        Console.Error.WriteLine("[MCP] WireMcpToolSyncBridge done");

        Console.Error.WriteLine("[MCP] WirePluginSkillBridge start");
        services.WirePluginSkillBridge();
        Console.Error.WriteLine("[MCP] WirePluginSkillBridge done");

        Console.Error.WriteLine("[MCP] LoadDreamPlugin start");
        try
        {
            var pluginManager = services.GetRequiredService<Core.Plugins.IPluginManager>();
            await pluginManager.LoadWorkflowPluginAsync<JoinCode.Dream.DreamPlugin>(ct).ConfigureAwait(false);
            Console.Error.WriteLine("[MCP] LoadDreamPlugin done");
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "[MCP] LoadDreamPlugin failed");
            Console.Error.WriteLine($"[MCP] LoadDreamPlugin error: {ex.Message}");
        }

        Console.Error.WriteLine("[MCP] InitializeAsync start");
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30));
            var mcpService = services.GetRequiredService<IMcpService>();
            await mcpService.InitializeAsync(services, cts.Token).ConfigureAwait(false);
            Console.Error.WriteLine("[MCP] InitializeAsync done");

            var toolsBridge = services.GetRequiredService<Core.DependencyInjection.McpToolSyncBridge>();
            await toolsBridge.OnToolsListChangedAsync(ct).ConfigureAwait(false);
            Console.Error.WriteLine("[MCP] OnToolsListChanged done");
        }
        catch (OperationCanceledException)
        {
            logger?.LogWarning("[MCP] InitializeAsync timed out after 30s");
            Console.Error.WriteLine("[MCP] timeout");
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "[MCP] InitializeAsync failed");
            Console.Error.WriteLine($"[MCP] error: {ex.Message}");
        }
    }
}
