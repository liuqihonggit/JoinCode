
namespace Core.Permission;

/// <summary>
/// Auto 模式安全中间件 — 已被 DangerousCommandProtectionMiddleware 替代
/// 保留此类仅为向后兼容测试项目，不再注册到权限管道
/// </summary>
[Obsolete("已被 DangerousCommandProtectionMiddleware 替代，不再注册到权限管道")]
public sealed partial class AutoSafetyMiddleware : IPermissionMiddleware
{
    /// <inheritdoc />

    /// <inheritdoc />

    /// <inheritdoc />
    public Task InvokeAsync(PermissionCheckContext context, MiddlewareDelegate<PermissionCheckContext> next, CancellationToken ct)
    {
        if (context.CurrentMode != PermissionMode.Auto)
            return next(context, ct);

        // 敏感路径写入检查
        if (context.IsWriteOperation(context.ToolName) &&
            context.Arguments != null &&
            context.Arguments.TryGetValue("path", out var pathEl) &&
            pathEl.ValueKind == JsonValueKind.String)
        {
            var path = pathEl.GetString()!;
            if (PermissionCheckContext.IsSensitivePath(path, context.Config.SensitivePathPatterns))
            {
                context.Result = ToolPermissionCheckResult.PendingConfirmation($"工具 '{context.ToolName}' 尝试写入敏感路径 '{path}'，是否批准？");
                return Task.CompletedTask;
            }
        }

        // 危险命令检查
        if (context.IsShellOperation(context.ToolName) &&
            context.Arguments != null &&
            context.Arguments.TryGetValue("command", out var cmdEl) &&
            cmdEl.ValueKind == JsonValueKind.String)
        {
            var command = cmdEl.GetString()!;
            if (PermissionCheckContext.IsDangerousCommand(command, context.Config.DangerousCommandPatterns))
            {
                context.Result = ToolPermissionCheckResult.PendingConfirmation($"工具 '{context.ToolName}' 尝试执行潜在危险命令，是否批准？");
                return Task.CompletedTask;
            }
        }

        return next(context, ct);
    }
}
