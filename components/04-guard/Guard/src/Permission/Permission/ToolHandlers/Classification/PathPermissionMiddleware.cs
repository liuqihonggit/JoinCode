
namespace Core.Permission;

/// <summary>
/// 路径权限中间件 — Default/Auto 模式下进行完整路径级权限检查
/// 对齐 TS checkReadPermissionForTool / checkWritePermissionForTool
/// </summary>
[Register(typeof(IPermissionMiddleware))]
public sealed partial class PathPermissionMiddleware : IPermissionMiddleware
{
    private readonly IPathPermissionChecker? _pathPermissionChecker;

    /// <inheritdoc />

    /// <inheritdoc />
    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    /// <summary>
    /// 创建 PathPermissionMiddleware
    /// </summary>
    public PathPermissionMiddleware(IPathPermissionChecker? pathPermissionChecker = null)
    {
        _pathPermissionChecker = pathPermissionChecker;
    }

    /// <inheritdoc />
    public Task InvokeAsync(PermissionCheckContext context, MiddlewareDelegate<PermissionCheckContext> next, CancellationToken ct)
    {
        if (_pathPermissionChecker is null || context.Arguments is null)
            return next(context, ct);

        // 仅 Default 和 Auto 模式需要路径权限检查
        if (context.CurrentMode != PermissionMode.Default && context.CurrentMode != PermissionMode.Auto)
            return next(context, ct);

        var path = PermissionCheckContext.ExtractPathFromArguments(context.Arguments);
        if (string.IsNullOrEmpty(path))
            return next(context, ct);

        // Auto 模式仅拦截非批准结果
        if (context.CurrentMode == PermissionMode.Auto)
        {
            var autoPathResult = CheckPathPermission(context.ToolName, path);
            if (autoPathResult is not null && !autoPathResult.IsApproved)
            {
                context.Result = autoPathResult;
                return Task.CompletedTask;
            }
            return next(context, ct);
        }

        // Default 模式：完整路径权限检查
        var pathResult = CheckPathPermission(context.ToolName, path);
        if (pathResult is not null)
        {
            context.Result = pathResult;
            return Task.CompletedTask;
        }

        return next(context, ct);
    }

    /// <summary>
    /// 路径级权限检查 — 对齐 TS checkReadPermissionForTool / checkWritePermissionForTool
    /// </summary>
    private ToolPermissionCheckResult? CheckPathPermission(string toolName, string path)
    {
        var checker = _pathPermissionChecker ?? throw new InvalidOperationException("Path permission checker not available.");
        // 读取工具: 调用 CheckReadPermission
        if (PermissionCheckContext.IsFileReadTool(toolName))
        {
            var result = checker.CheckReadPermission(path);
            return PermissionCheckContext.MapPathResult(result);
        }

        // 写入/编辑工具: 调用 CheckWritePermission
        if (PermissionCheckContext.IsFileWriteTool(toolName))
        {
            var result = checker.CheckWritePermission(path);
            return PermissionCheckContext.MapPathResult(result);
        }

        return null;
    }
}
