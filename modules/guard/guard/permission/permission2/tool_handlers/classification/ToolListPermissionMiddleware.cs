
namespace Core.Permission;

/// <summary>
/// 工具列表权限中间件 — 检查自动批准/拒绝工具列表
/// Default/Auto/Plan 模式下均生效
/// </summary>
[Register(typeof(IPermissionMiddleware), ServiceLifetime.Singleton)]
public sealed partial class ToolListPermissionMiddleware : ServiceEntity, IPermissionMiddleware {
    /// <inheritdoc />

    /// <inheritdoc />

    /// <inheritdoc />
    public Task InvokeAsync(PermissionCheckContext context, MiddlewareDelegate<PermissionCheckContext> next, CancellationToken ct) {
        // 文件类工具带路径参数时不在此短路 — 工具级 allow 名单不得绕过路径级边界
        // (工作目录内外、敏感路径、deny 规则由 PathPermissionMiddleware 决策链判定；ADR 0138)
        if (context.Arguments is not null &&
            PermissionCheckContext.ExtractPathFromArguments(context.Arguments) is not null &&
            (PermissionCheckContext.IsFileReadTool(context.ToolName) || PermissionCheckContext.IsFileWriteTool(context.ToolName))) {
            return next(context, ct);
        }

        // 自动批准列表
        if (context.AutoApprovedTools.Contains(context.ToolName)) {
            context.Result = ToolPermissionCheckResult.Approved();
            return Task.CompletedTask;
        }

        // 自动拒绝列表
        if (context.AutoRejectedTools.Contains(context.ToolName)) {
            context.Result = ToolPermissionCheckResult.Rejected("工具在自动拒绝列表中");
            return Task.CompletedTask;
        }

        return next(context, ct);
    }
}