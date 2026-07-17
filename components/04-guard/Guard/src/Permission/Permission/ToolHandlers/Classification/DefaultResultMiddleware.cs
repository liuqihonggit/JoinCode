
namespace Core.Permission;

/// <summary>
/// 默认结果中间件 — 管道末端的兜底决策
/// Default 模式返回待确认，Auto 模式返回批准
/// </summary>
[Register(typeof(IPermissionMiddleware))]
public sealed partial class DefaultResultMiddleware : IPermissionMiddleware
{
    /// <inheritdoc />

    /// <inheritdoc />

    /// <inheritdoc />
    public Task InvokeAsync(PermissionCheckContext context, MiddlewareDelegate<PermissionCheckContext> next, CancellationToken ct)
    {
        context.Result = context.CurrentMode switch
        {
            PermissionMode.Auto => ToolPermissionCheckResult.Approved(),
            _ => ToolPermissionCheckResult.PendingConfirmation($"工具 '{context.ToolName}' 请求执行操作，是否批准？")
        };

        return Task.CompletedTask;
    }
}
