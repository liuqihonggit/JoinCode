
namespace Core.Permission;

/// <summary>
/// Config GET 操作中间件 — Default 模式下自动批准 Config GET 操作
/// 对齐 TS 版 ConfigTool.checkPermissions: GET 操作自动允许
/// </summary>
[Register(typeof(IPermissionMiddleware))]
public sealed partial class ConfigGetOperationMiddleware : IPermissionMiddleware
{
    /// <inheritdoc />

    /// <inheritdoc />

    /// <inheritdoc />
    public Task InvokeAsync(PermissionCheckContext context, MiddlewareDelegate<PermissionCheckContext> next, CancellationToken ct)
    {
        if (context.CurrentMode == PermissionMode.Default &&
            PermissionCheckContext.IsConfigGetOperation(context.ToolName, context.Arguments))
        {
            context.Result = ToolPermissionCheckResult.Approved();
            return Task.CompletedTask;
        }

        return next(context, ct);
    }
}
