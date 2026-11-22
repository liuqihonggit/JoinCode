
namespace Core.Permission;

/// <summary>
/// 危险操作中间件 — Auto/Unattended 模式下检查危险操作
/// </summary>
[Register(typeof(IPermissionMiddleware), ServiceLifetime.Singleton)]
public sealed partial class DangerousOperationMiddleware : ServiceEntity, IPermissionMiddleware {
    /// <inheritdoc />
    public Task InvokeAsync(PermissionCheckContext context, MiddlewareDelegate<PermissionCheckContext> next, CancellationToken ct) {
        if (context.CurrentMode is not (PermissionMode.Auto or PermissionMode.Unattended))
            return next(context, ct);

        if (IsDangerousOperation(context)) {
            // Unattended 没有宿主可确认：危险操作按无人值守契约自动执行（命令级硬红灯仍由 DangerousCommand 层拦截 + 审计）
            context.Result = context.CurrentMode == PermissionMode.Unattended
                ? ToolPermissionCheckResult.Approved()
                : ToolPermissionCheckResult.PendingConfirmation($"工具 '{context.ToolName}' 可能执行危险操作，请确认是否继续？");
            return Task.CompletedTask;
        }

        return next(context, ct);
    }

    /// <summary>
    /// 检查是否为危险操作
    /// </summary>

    private static bool IsDangerousOperation(PermissionCheckContext context) {
        if (context.Config.DangerousOperationPatterns.Any(pattern =>
            PermissionCheckContext.MatchesPattern(context.ToolName, pattern.Pattern, pattern.PatternType)))
            return true;

        if (context.IsShellOperation(context.ToolName))
            return true;

        if (context.IsWriteOperation(context.ToolName) &&
            context.Arguments != null &&
            context.Arguments.TryGetValue("path", out var pathEl) &&
            pathEl.ValueKind == JsonValueKind.String &&
            pathEl.GetString() is { } path && PermissionCheckContext.IsSensitivePath(path, context.Config.SensitivePathPatterns))
            return true;

        return false;
    }
}