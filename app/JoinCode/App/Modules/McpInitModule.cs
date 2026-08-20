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

        // async void 事件处理器必须 try/catch — 否则异常逃逸到同步上下文导致进程崩溃
        remoteClientManager.ToolsListChanged += async (_, _) =>
        {
            try
            {
                await syncBridge.OnToolsListChangedAsync().ConfigureAwait(false);
                await RefreshKernelPluginsAsync(services, logger).ConfigureAwait(false);
            }
            catch (Exception ex) { logger?.LogError(ex, "[MCP] OnToolsListChanged handler failed"); }
        };

        remoteClientManager.ResourcesListChanged += async (_, args) =>
        {
            try { await syncBridge.OnResourcesListChangedAsync(args.ClientId, args.SyncResult).ConfigureAwait(false); }
            catch (Exception ex) { logger?.LogError(ex, "[MCP] OnResourcesListChanged handler failed"); }
        };

        remoteClientManager.PromptsListChanged += async (_, args) =>
        {
            try { await syncBridge.OnPromptsListChangedAsync(args.ClientId, args.SyncResult).ConfigureAwait(false); }
            catch (Exception ex) { logger?.LogError(ex, "[MCP] OnPromptsListChanged handler failed"); }
        };

        services.WirePluginSkillBridge();

        // 并行：DreamPlugin 加载 + MCP 工具注册（两者逻辑独立，IToolRegistry 线程安全）
        var dreamTask = LoadDreamPluginSafeAsync(services, logger, ct);
        var mcpInitTask = McpInitializeSafeAsync(services, logger, ct);
        await Task.WhenAll(dreamTask, mcpInitTask).ConfigureAwait(false);

        // 所有工具注册完成后，同步工具列表 + 刷新 kernel.Plugins
        try
        {
            var toolsBridge = services.GetRequiredService<Core.DependencyInjection.McpToolSyncBridge>();
            await toolsBridge.OnToolsListChangedAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "[MCP] OnToolsListChangedAsync failed");
        }

        // 把 IToolRegistry 中的工具挂载到 kernel.Plugins — 修复断裂的工具管线
        // McpToolBridge.CreatePluginAsync 从 IToolRegistry 提取所有工具（含 SlashToMcpAdapter 注册的斜杠命令）
        // 挂载到 IChatClient.Plugins 后，AnthropicQueryService/OpenAIQueryService 能通过 BuildXxxToolsFromKernel 构建工具列表发送给 LLM
        await RefreshKernelPluginsAsync(services, logger).ConfigureAwait(false);
    }

    /// <summary>
    /// 安全加载 DreamPlugin — 失败仅记日志，不阻断启动
    /// </summary>
    private static async Task LoadDreamPluginSafeAsync(IServiceProvider services, ILogger? logger, CancellationToken ct)
    {
        try
        {
            var pluginManager = services.GetRequiredService<Core.Plugins.IPluginManager>();
            await pluginManager.LoadWorkflowPluginAsync<JoinCode.Dream.DreamPlugin>(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "[MCP] LoadDreamPlugin failed");
        }
    }

    /// <summary>
    /// 安全初始化 MCP 服务 — 5s 超时，失败仅记日志，不阻断启动
    /// </summary>
    private static async Task McpInitializeSafeAsync(IServiceProvider services, ILogger? logger, CancellationToken ct)
    {
        try
        {
            using var cts = TimeoutHelper.CreateLinkedTimeout(ct, TimeSpan.FromSeconds(5));
            var mcpService = services.GetRequiredService<IMcpService>();
            await mcpService.InitializeAsync(services, cts.Token).ConfigureAwait(false);
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

    /// <summary>
    /// 从 IToolRegistry 刷新 kernel.Plugins — 把所有注册的工具（MCP + 斜杠）挂载到 IChatClient
    /// </summary>
    private static async Task RefreshKernelPluginsAsync(IServiceProvider services, ILogger? logger, CancellationToken cancellationToken = default)
    {
        try
        {
            var chatClient = services.GetService<IChatClient>();
            var toolRegistry = services.GetService<IToolRegistry>();
            if (chatClient is null || toolRegistry is null)
                return;

            var bridge = new McpToolBridge(toolRegistry);
            var plugins = await bridge.CreatePluginAsync(cancellationToken).ConfigureAwait(false);

            chatClient.Plugins.Remove(ToolGroupNameConstants.CoreTools);
            chatClient.Plugins.Remove(ToolGroupNameConstants.McpTools);
            foreach (var p in plugins)
                chatClient.Plugins.Add(p);

            var totalCount = plugins.Sum(p => p.Functions.Count());
            logger?.LogDebug("[MCP] kernel.Plugins 已刷新，{GroupCount} 组 {Count} 个工具", plugins.Count, totalCount);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "[MCP] 刷新 kernel.Plugins 失败");
        }
    }
}
