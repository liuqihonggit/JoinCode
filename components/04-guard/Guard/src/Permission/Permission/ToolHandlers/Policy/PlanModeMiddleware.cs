
namespace Core.Permission;

/// <summary>
/// Plan 模式中间件 — Plan 模式下读取操作自动批准，写入操作需确认
/// </summary>
[Register(typeof(IPermissionMiddleware))]
public sealed partial class PlanModeMiddleware : IPermissionMiddleware
{
    /// <inheritdoc />

    /// <inheritdoc />

    /// <inheritdoc />
    public Task InvokeAsync(PermissionCheckContext context, MiddlewareDelegate<PermissionCheckContext> next, CancellationToken ct)
    {
        if (context.CurrentMode != PermissionMode.Plan)
            return next(context, ct);

        if (context.IsReadOperation(context.ToolName))
        {
            context.Result = ToolPermissionCheckResult.Approved();
            return Task.CompletedTask;
        }

        if (context.IsWriteOperation(context.ToolName))
        {
            context.Result = ToolPermissionCheckResult.PendingConfirmation($"计划模式：工具 '{context.ToolName}' 将执行写入操作，是否批量批准此类操作？");
            return Task.CompletedTask;
        }

        // Plan 模式下非读写操作默认批准
        context.Result = ToolPermissionCheckResult.Approved();
        return Task.CompletedTask;
    }
}
