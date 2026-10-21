namespace JoinCode.App.Modules;

/// <summary>
/// MCP 初始化模块 — 启动后初始化 MCP 服务和桥接：挂工具/资源/提示词变更事件（增量刷新）+
/// 接线技能桥；八插件加载经 <see cref="McpPluginBootstrap"/> 后台化（启动开销优化 — TASK037 后续：
/// 不阻塞启动等加载墙钟，工具就绪后自动补齐 kernel.Plugins）。
/// </summary>
[AppModule(Order = 90)]
public sealed class McpInitModule : IAppModule {
    /// <summary>
    /// 获取模块加载顺序 — 固定为 90，确保在其他基础模块之后加载
    /// </summary>
    public int Order => 90;

    /// <summary>
    /// 注册模块所需的服务到 DI 容器 — 后台加载生命周期句柄（Host DisposeAsync 释放时等待链收尾）
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="context">模块配置上下文</param>
    public void ConfigureServices(IServiceCollection services, AppModuleContext context) {
        services.AddSingleton<McpPluginBootstrapHandle>();
    }

    /// <summary>
    /// 异步配置模块 — 初始化 MCP 桥接、加载插件、刷新 kernel.Plugins
    /// </summary>
    /// <param name="services">已构建的服务提供者</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task ConfigureAsync(IServiceProvider services, CancellationToken ct) {
        var logger = services.GetService<ILogger<McpInitModule>>();

        var remoteClientManager = services.GetRequiredService<RemoteClientManager>();
        var syncBridge = services.GetRequiredService<McpToolSyncBridge>();

        // async void 事件处理器必须 try/catch — 否则异常逃逸到同步上下文导致进程崩溃
        remoteClientManager.ToolsListChanged += async (_, _) => {
            try {
                await syncBridge.OnToolsListChangedAsync().ConfigureAwait(false);
                await RefreshKernelPluginsAsync(services, logger).ConfigureAwait(false);
            } catch (Exception ex) { logger?.LogError(ex, "[MCP] OnToolsListChanged handler failed"); }
        };

        remoteClientManager.ResourcesListChanged += async (_, args) => {
            try { await syncBridge.OnResourcesListChangedAsync(args.ClientId, args.SyncResult).ConfigureAwait(false); } catch (Exception ex) { logger?.LogError(ex, "[MCP] OnResourcesListChanged handler failed"); }
        };

        remoteClientManager.PromptsListChanged += async (_, args) => {
            try { await syncBridge.OnPromptsListChangedAsync(args.ClientId, args.SyncResult).ConfigureAwait(false); } catch (Exception ex) { logger?.LogError(ex, "[MCP] OnPromptsListChanged handler failed"); }
        };

        services.WirePluginSkillBridge();

        // 启动开销优化（TASK037 后续）— 八插件加载后台化：启动不阻塞等待加载墙钟（实测 ~1.6s，
        // mcpInit 插件 MCP 客户端握手为关键路径）；工具就绪后就绪钩子自动补齐
        // （toolsBridge 同步 + kernel.Plugins 刷新 — 与 ToolsListChanged 事件增量刷新双保险）；
        // 生命周期经 Handle 单例绑定宿主 Dispose（退出竞态防护 — 先取消加速断链再无超时等待收尾）
        services.GetRequiredService<McpPluginBootstrapHandle>()
            .Run(ct => McpPluginBootstrap.StartAsync(
                ct => StartPluginLoads(services, logger, ct),
                ct => OnPluginsReadyAsync(services, logger, ct),
                ct));
    }

    /// <summary>八插件并行加载清单（逻辑独立，IToolRegistry 线程安全 — WhenAll 墙钟语义）。</summary>
    private static Task[] StartPluginLoads(IServiceProvider services, ILogger? logger, CancellationToken ct) =>
    [
        LoadPluginSafeAsync<JoinCode.Dream.DreamPlugin>("DreamPlugin", services, logger, ct),
        LoadPluginSafeAsync<Core.Plugins.FixHooksPlugin>("FixHooksPlugin", services, logger, ct),
        LoadPluginSafeAsync<Core.Plugins.SandboxProvidersPlugin>("SandboxProvidersPlugin", services, logger, ct),
        LoadPluginSafeAsync<Core.Telemetry.TelemetryPlugin>("TelemetryPlugin", services, logger, ct),
        LoadPluginSafeAsync<Core.Agents.AgentRolesPlugin>("AgentRolesPlugin", services, logger, ct),
        LoadPluginSafeAsync<JoinCode.CodeIndex.CSharpLanguagePlugin>("CSharpLanguagePlugin", services, logger, ct),
        LoadPluginSafeAsync<JoinCode.Llm.Plugins.LlmProvidersPlugin>("LlmProvidersPlugin", services, logger, ct),
        LoadPluginSafeAsync<JoinCode.Mcp.Plugins.McpInitPlugin>("McpInitPlugin", services, logger, ct),
    ];

    /// <summary>
    /// 安全加载工作流插件（八插件公共路径 — 原八个 SafeAsync 方法收敛）：
    /// 失败仅记日志不阻断启动；宿主关闭取消静默（Handle Dispose 触发的取消非失败 —
    /// 否则取消噪音淹没真实加载错误日志）。
    /// </summary>
    private static async Task LoadPluginSafeAsync<TPlugin>(string name, IServiceProvider services, ILogger? logger, CancellationToken ct)
        where TPlugin : class, IWorkflowPlugin, new() {
        try {
            var pluginManager = services.GetRequiredService<Core.Plugins.IPluginManager>();
            await pluginManager.LoadWorkflowPluginAsync<TPlugin>(ct).ConfigureAwait(false);
        } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
            // 宿主关闭取消 — 静默收尾（非失败）
        } catch (Exception ex) {
            logger?.LogError(ex, "[MCP] Load{name} failed", name);
        }
    }

    /// <summary>插件就绪钩子 — 全部加载成功后同步工具列表 + 刷新 kernel.Plugins
    /// （原 ConfigureAsync 尾部逻辑原样迁移；异常由 Bootstrap SafeAsync 兜底吞掉）。</summary>
    private static async Task OnPluginsReadyAsync(IServiceProvider services, ILogger? logger, CancellationToken ct) {
        // 所有工具注册完成后，同步工具列表 + 刷新 kernel.Plugins
        try {
            var toolsBridge = services.GetRequiredService<Core.DependencyInjection.McpToolSyncBridge>();
            await toolsBridge.OnToolsListChangedAsync(ct).ConfigureAwait(false);
        } catch (Exception ex) {
            logger?.LogError(ex, "[MCP] OnToolsListChangedAsync failed");
        }

        // 把 IToolRegistry 中的工具挂载到 kernel.Plugins — 修复断裂的工具管线
        // McpToolBridge.CreatePluginAsync 从 IToolRegistry 提取所有工具（含 SlashToMcpAdapter 注册的斜杠命令）
        // 挂载到 IChatClient.Plugins 后，AnthropicQueryService/OpenAIQueryService 能通过 BuildXxxToolsFromKernel 构建工具列表发送给 LLM
        await RefreshKernelPluginsAsync(services, logger).ConfigureAwait(false);
    }

    /// <summary>
    /// 从 IToolRegistry 刷新 kernel.Plugins — 把所有注册的工具（MCP + 斜杠）挂载到 IChatClient
    /// </summary>
    private static async Task RefreshKernelPluginsAsync(IServiceProvider services, ILogger? logger, CancellationToken cancellationToken = default) {
        try {
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
        } catch (ObjectDisposedException ex) {
            // 引擎关闭期良性竞态：Host 已 dispose（AsyncLock 释放）而后台刷新任务晚到 — 非故障，
            // 降级 Debug 避免 E2E NoErrors 断言误报（CLI 每轮退出与 McpPluginBootstrap 后台加载的时序差）
            logger?.LogDebug(ex, "[MCP] 刷新 kernel.Plugins 跳过：引擎已关闭");
        } catch (Exception ex) {
            logger?.LogError(ex, "[MCP] 刷新 kernel.Plugins 失败");
        }
    }
}