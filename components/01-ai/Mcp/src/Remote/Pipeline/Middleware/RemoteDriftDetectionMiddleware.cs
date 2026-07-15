namespace McpToolRegistry;

using JoinCode.Abstractions.Pipeline;

/// <summary>
/// 漂移检测中间件 — 仅 Tools 操作：检测工具漂移并决策重连策略
/// </summary>
[Register(typeof(IRemoteSyncMiddleware))]
public sealed partial class RemoteDriftDetectionMiddleware : IRemoteSyncMiddleware
{
    [Inject] private readonly ILogger<RemoteDriftDetectionMiddleware> _logger;

    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    public Task InvokeAsync(RemoteSyncContext ctx, MiddlewareDelegate<RemoteSyncContext> next, CancellationToken ct)
    {
        if (ctx.Operation != RemoteSyncOperation.Tools)
        {
            return next(ctx, ct);
        }

        if (ctx.PreviousToolSpecs is not { Count: > 0 } || ctx.ToolsResult is null)
        {
            return next(ctx, ct);
        }

        var newSpecs = ctx.ToolsResult.GetData()
            .Select(t => new ToolSpec(
                McpNameNormalizer.BuildMcpToolName(ctx.ClientId, t.Name),
                t.Description,
                t.InputSchema?.ToString()))
            .ToList();

        var driftReport = ToolListDriftClassifier.Classify(ctx.PreviousToolSpecs, newSpecs);
        ctx.DriftReport = driftReport;

        _logger.LogInformation(
            "远程客户端 {ClientId} 工具漂移检测: {DriftKind} - {Summary}",
            ctx.ClientId, driftReport.Kind, driftReport.Summary);

        if (!driftReport.IsCacheSafe)
        {
            _logger.LogWarning(
                "远程客户端 {ClientId} 检测到缓存不安全漂移: {DriftKind}，前缀缓存可能失效",
                ctx.ClientId, driftReport.Kind);
        }

        var reconnectResult = McpReconnectPolicy.Decide(driftReport, ctx.AcceptLevel);
        ctx.ReconnectResult = reconnectResult;

        if (!reconnectResult.Accepted)
        {
            _logger.LogWarning(
                "远程客户端 {ClientId} 重连策略拒绝同步: {Reason}",
                ctx.ClientId, reconnectResult.Reason);

            ctx.ReconnectRejected = true;
            ctx.Success = false;
            ctx.ErrorMessage = reconnectResult.Reason;
            return Task.CompletedTask;
        }

        return next(ctx, ct);
    }
}
