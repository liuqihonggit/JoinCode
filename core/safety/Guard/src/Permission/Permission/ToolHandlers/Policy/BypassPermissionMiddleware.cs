
namespace Core.Permission;

/// <summary>
/// 绕过权限检查中间件 — BypassPermissions 模式下直接批准所有操作
/// </summary>
[Register(typeof(IPermissionMiddleware))]
public sealed partial class BypassPermissionMiddleware : IPermissionMiddleware
{
    /// <inheritdoc />

    /// <inheritdoc />

    /// <inheritdoc />
    public Task InvokeAsync(PermissionCheckContext context, MiddlewareDelegate<PermissionCheckContext> next, CancellationToken ct)
    {
        if (context.CurrentMode == PermissionMode.BypassPermissions)
        {
            context.Result = ToolPermissionCheckResult.Approved();
            return Task.CompletedTask;
        }

        return next(context, ct);
    }
}
