
namespace Core.Permission;

/// <summary>
/// 早期路径拒绝中间件 — Default 模式下在 autoApprovedTools 之前检查路径级 deny 规则
/// 防止路径级 deny 规则被工具级 auto-approved 绕过
/// </summary>
[Register(typeof(IPermissionMiddleware))]
public sealed partial class EarlyPathDenyMiddleware : IPermissionMiddleware
{
    private readonly IPathPermissionChecker? _pathPermissionChecker;

    /// <inheritdoc />

    /// <inheritdoc />
    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    /// <summary>
    /// 创建 EarlyPathDenyMiddleware
    /// </summary>
    public EarlyPathDenyMiddleware(IPathPermissionChecker? pathPermissionChecker = null)
    {
        _pathPermissionChecker = pathPermissionChecker;
    }

    /// <inheritdoc />
    public Task InvokeAsync(PermissionCheckContext context, MiddlewareDelegate<PermissionCheckContext> next, CancellationToken ct)
    {
        if (context.CurrentMode != PermissionMode.Default || _pathPermissionChecker is null || context.Arguments is null)
            return next(context, ct);

        var path = PermissionCheckContext.ExtractPathFromArguments(context.Arguments);
        if (string.IsNullOrEmpty(path))
            return next(context, ct);

        if (!PermissionCheckContext.IsFileReadTool(context.ToolName) && !PermissionCheckContext.IsFileWriteTool(context.ToolName))
            return next(context, ct);

        var result = PermissionCheckContext.IsFileReadTool(context.ToolName)
            ? _pathPermissionChecker.CheckReadPermission(path)
            : _pathPermissionChecker.CheckWritePermission(path);

        // 仅拦截 deny，allow/ask 由后续完整检查处理
        if (result.Decision == PermissionBehavior.Deny)
        {
            context.Result = PermissionCheckContext.MapPathResult(result);
            return Task.CompletedTask;
        }

        return next(context, ct);
    }
}
