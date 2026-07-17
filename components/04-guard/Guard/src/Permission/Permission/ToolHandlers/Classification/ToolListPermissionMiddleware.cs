
namespace Core.Permission;

/// <summary>
/// 工具列表权限中间件 — 检查自动批准/拒绝工具列表
/// Default/Auto/Plan 模式下均生效
/// </summary>
[Register(typeof(IPermissionMiddleware))]
public sealed partial class ToolListPermissionMiddleware : IPermissionMiddleware
{
    /// <inheritdoc />

    /// <inheritdoc />

    /// <inheritdoc />
    public Task InvokeAsync(PermissionCheckContext context, MiddlewareDelegate<PermissionCheckContext> next, CancellationToken ct)
    {
        // 自动批准列表
        if (context.AutoApprovedTools.Contains(context.ToolName))
        {
            context.Result = ToolPermissionCheckResult.Approved();
            return Task.CompletedTask;
        }

        // 自动拒绝列表
        if (context.AutoRejectedTools.Contains(context.ToolName))
        {
            context.Result = ToolPermissionCheckResult.Rejected("工具在自动拒绝列表中");
            return Task.CompletedTask;
        }

        return next(context, ct);
    }
}
