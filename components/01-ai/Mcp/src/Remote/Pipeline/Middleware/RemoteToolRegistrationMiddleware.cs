namespace McpToolRegistry;

using JoinCode.Abstractions.Pipeline;

/// <summary>
/// 工具注册中间件 — 仅 Tools 操作：注册工具到 ToolRegistry 并更新缓存
/// </summary>
[Register(typeof(IRemoteSyncMiddleware))]
public sealed partial class RemoteToolRegistrationMiddleware : IRemoteSyncMiddleware
{
    [Inject] private readonly IToolRegistry _toolRegistry;
    [Inject] private readonly ILogger<RemoteToolRegistrationMiddleware> _logger;

    public ErrorBehavior OnError => ErrorBehavior.Continue;

    public async Task InvokeAsync(RemoteSyncContext ctx, MiddlewareDelegate<RemoteSyncContext> next, CancellationToken ct)
    {
        if (ctx.Operation != RemoteSyncOperation.Tools || ctx.Client is null || ctx.ToolsResult is null)
        {
            await next(ctx, ct).ConfigureAwait(false);
            return;
        }

        try
        {
            var toolItems = ctx.ToolsResult.GetData()
                .Select(tool =>
                {
                    var remoteToolHandler = new RemoteMcpToolHandler(ctx.ClientId, ctx.Client, tool);
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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "从远程客户端 {ClientId} 注册工具失败", ctx.ClientId);
            ctx.Success = false;
            ctx.ErrorMessage = ex.Message;
        }

        await next(ctx, ct).ConfigureAwait(false);
    }
}
