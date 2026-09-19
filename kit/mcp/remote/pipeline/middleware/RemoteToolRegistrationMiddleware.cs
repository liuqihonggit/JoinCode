namespace McpToolRegistry;


/// <summary>
/// 工具注册中间件 — 仅 Tools 操作：注册工具到 ToolRegistry 并更新缓存
/// </summary>
[Register(typeof(IRemoteSyncMiddleware), ServiceLifetime.Singleton)]
public sealed partial class RemoteToolRegistrationMiddleware : ServiceEntity, IRemoteSyncMiddleware {

    /// <summary>
    /// 初始化 <see cref="RemoteToolRegistrationMiddleware"/> 实例
    /// </summary>
    /// <param name="toolRegistry">工具注册表</param>
    /// <param name="logger">日志记录器</param>
    public RemoteToolRegistrationMiddleware(IToolRegistry toolRegistry, ILogger<RemoteToolRegistrationMiddleware> logger) {
        _toolRegistry = toolRegistry;
        _logger = logger;
    }
    private readonly IToolRegistry _toolRegistry;
    private readonly ILogger<RemoteToolRegistrationMiddleware> _logger;

    /// <summary>错误行为：继续执行后续中间件</summary>
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <summary>
    /// 执行中间件逻辑 — 仅 Tools 操作时注册工具到注册表并更新缓存
    /// </summary>
    /// <param name="ctx">远程同步上下文</param>
    /// <param name="next">下一个中间件委托</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>异步任务</returns>
    public async Task InvokeAsync(RemoteSyncContext ctx, MiddlewareDelegate<RemoteSyncContext> next, CancellationToken ct) {
        if (ctx.Operation != RemoteSyncOperation.Tools || ctx.Client is null || ctx.ToolsResult is null) {
            await next(ctx, ct).ConfigureAwait(false);
            return;
        }

        try {
            var toolItems = ctx.ToolsResult.GetData()
                .Select(tool => {
                    var remoteToolHandler = new RemoteMcpToolDispatch(ctx.ClientId, ctx.Client, tool);
                    var fullToolName = McpNameNormalizer.BuildMcpToolName(ctx.ClientId, tool.Name);
                    return (FullToolName: fullToolName, Handler: remoteToolHandler);
                })
                .ToList();

            await Task.WhenAll(toolItems.Select(item => _toolRegistry.RegisterToolAsync(item.Handler, ct))).ConfigureAwait(false);

            var newSpecs = ctx.ToolsResult.GetData()
                .Select(t => new ToolSpec(
                    McpNameNormalizer.BuildMcpToolName(ctx.ClientId, t.Name),
                    t.Description,
                    t.InputSchema?.ToString()))
                .ToList();

            ctx.SyncedNames = toolItems.Select(t => t.FullToolName).ToList();

            _logger.LogInformation(
                "从远程客户端 {ClientId} 同步了 {Count} 个工具",
                ctx.ClientId,
                toolItems.Count);
        } catch (Exception ex) {
            _logger.LogError(ex, "从远程客户端 {ClientId} 注册工具失败", ctx.ClientId);
            ctx.Success = false;
            ctx.ErrorMessage = ex.Message;
        }

        await next(ctx, ct).ConfigureAwait(false);
    }
}