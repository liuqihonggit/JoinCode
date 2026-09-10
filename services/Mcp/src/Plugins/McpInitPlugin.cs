namespace JoinCode.Mcp.Plugins;

/// <summary>
/// MCP 服务初始化插件 — 初始化 MCP 服务(工具注册/远程客户端连接)
/// <para>万物皆插件(ADR 0098): 从 McpInitModule 硬编码迁移为插件加载</para>
/// </summary>
[Register(typeof(IWorkflowPlugin), ServiceLifetime.Singleton)]
public sealed partial class McpInitPlugin : WorkflowPluginBase
{
    public McpInitPlugin() : base("McpInit") { }

    /// <summary>插件名称</summary>
    public override string Name => "McpInit";

    /// <summary>插件版本</summary>
    public override string Version => "1.0.0";

    /// <summary>插件描述</summary>
    public override string Description => "MCP服务初始化插件(工具注册/远程客户端连接)";

    /// <summary>加载插件 — 无副作用,仅返回成功</summary>
    public override Task<OperationResult> LoadAsync(PluginContext ctx, CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResult.Ok());

    /// <summary>初始化插件 — 调用 IMcpService.InitializeAsync,5s 超时</summary>
    public override async Task<OperationResult> InitializeAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        var logger = serviceProvider.GetService<ILogger<McpInitPlugin>>();
        try
        {
            using var cts = TimeoutHelper.CreateLinkedTimeout(cancellationToken, TimeSpan.FromSeconds(5));
            var mcpService = serviceProvider.GetRequiredService<IMcpService>();
            await mcpService.InitializeAsync(serviceProvider, cts.Token).ConfigureAwait(false);
            return OperationResult.Ok();
        }
        catch (OperationCanceledException)
        {
            logger?.LogWarning("[MCP] InitializeAsync timed out after 5s");
            return OperationResult.Fail("MCP 初始化超时(5s)");
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "[MCP] InitializeAsync failed");
            return OperationResult.Fail(ex.Message);
        }
    }

    /// <summary>插件特定清理 — 无特殊清理</summary>
    protected override void OnUnload() { }
}
