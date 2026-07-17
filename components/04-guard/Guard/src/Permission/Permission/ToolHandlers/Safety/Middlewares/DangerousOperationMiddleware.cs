
namespace Core.Permission;

/// <summary>
/// 危险操作中间件 — Default 模式下检查危险操作
/// </summary>
[Register(typeof(IPermissionMiddleware))]
public sealed partial class DangerousOperationMiddleware : IPermissionMiddleware
{
    /// <inheritdoc />

    /// <inheritdoc />

    /// <inheritdoc />
    public Task InvokeAsync(PermissionCheckContext context, MiddlewareDelegate<PermissionCheckContext> next, CancellationToken ct)
    {
        if (context.CurrentMode != PermissionMode.Default)
            return next(context, ct);

        if (IsDangerousOperation(context))
        {
            context.Result = ToolPermissionCheckResult.PendingConfirmation($"工具 '{context.ToolName}' 可能执行危险操作，请确认是否继续？");
            return Task.CompletedTask;
        }

        return next(context, ct);
    }

    /// <summary>
    /// 检查是否为危险操作
    /// </summary>
    private static bool IsDangerousOperation(PermissionCheckContext context)
    {
        if (context.Config.DangerousOperationPatterns.Any(pattern =>
            PermissionCheckContext.MatchesPattern(context.ToolName, pattern.Pattern, pattern.PatternType)))
            return true;

        if (context.IsShellOperation(context.ToolName))
            return true;

        if (context.IsWriteOperation(context.ToolName) &&
            context.Arguments != null &&
            context.Arguments.TryGetValue("path", out var pathEl) &&
            pathEl.ValueKind == JsonValueKind.String &&
            PermissionCheckContext.IsSensitivePath(pathEl.GetString()!, context.Config.SensitivePathPatterns))
            return true;

        return false;
    }
}
