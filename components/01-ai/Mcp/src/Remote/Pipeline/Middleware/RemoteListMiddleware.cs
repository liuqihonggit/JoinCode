namespace McpToolRegistry;

using JoinCode.Abstractions.Pipeline;

/// <summary>
/// 远程列表中间件 — 调用 ListTools/ListResources/ListPrompts
/// </summary>
[Register(typeof(IRemoteSyncMiddleware))]
public sealed partial class RemoteListMiddleware : IRemoteSyncMiddleware
{
    [Inject] private readonly ILogger<RemoteListMiddleware> _logger;

    public ErrorBehavior OnError => ErrorBehavior.Continue;

    public async Task InvokeAsync(RemoteSyncContext ctx, MiddlewareDelegate<RemoteSyncContext> next, CancellationToken ct)
    {
        if (ctx.Client is null)
        {
            await next(ctx, ct).ConfigureAwait(false);
            return;
        }

        try
        {
            switch (ctx.Operation)
            {
                case RemoteSyncOperation.Tools:
                    var toolsResult = await ctx.Client.ListToolsAsync(ct).ConfigureAwait(false);
                    if (!toolsResult.Success)
                    {
                        ctx.Success = false;
                        ctx.ErrorMessage = toolsResult.ErrorMessage;
                        await next(ctx, ct).ConfigureAwait(false);
                        return;
                    }

                    ctx.ToolsResult = toolsResult;
                    ctx.SyncedNames = toolsResult.GetData()
                        .Select(t => McpNameNormalizer.BuildMcpToolName(ctx.ClientId, t.Name))
                        .ToList();

                    _logger.LogInformation(
                        "从远程客户端 {ClientId} 列出了 {Count} 个工具",
                        ctx.ClientId, toolsResult.GetData().Count);
                    break;

                case RemoteSyncOperation.Resources:
                    var resourcesResult = await ctx.Client.ListResourcesAsync(ct).ConfigureAwait(false);
                    if (!resourcesResult.Success)
                    {
                        ctx.Success = false;
                        ctx.ErrorMessage = resourcesResult.ErrorMessage;
                        await next(ctx, ct).ConfigureAwait(false);
                        return;
                    }

                    ctx.SyncedNames = resourcesResult.Data!
                        .Select(r => r.Uri)
                        .ToList();

                    _logger.LogInformation(
                        "从远程客户端 {ClientId} 列出了 {Count} 个资源",
                        ctx.ClientId, resourcesResult.GetData().Count);
                    break;

                case RemoteSyncOperation.Prompts:
                    var promptsResult = await ctx.Client.ListPromptsAsync(ct).ConfigureAwait(false);
                    if (!promptsResult.Success)
                    {
                        ctx.Success = false;
                        ctx.ErrorMessage = promptsResult.ErrorMessage;
                        await next(ctx, ct).ConfigureAwait(false);
                        return;
                    }

                    ctx.SyncedNames = promptsResult.Data!
                        .Select(p => p.Name)
                        .ToList();

                    _logger.LogInformation(
                        "从远程客户端 {ClientId} 列出了 {Count} 个提示模板",
                        ctx.ClientId, promptsResult.GetData().Count);
                    break;
            }

            ctx.Success = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "从远程客户端 {ClientId} 同步{Operation}失败", ctx.ClientId, ctx.Operation);
            ctx.Success = false;
            ctx.ErrorMessage = ex.Message;
        }

        await next(ctx, ct).ConfigureAwait(false);
    }
}
