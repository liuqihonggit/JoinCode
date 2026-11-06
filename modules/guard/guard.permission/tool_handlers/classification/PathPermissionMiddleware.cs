
namespace Core.Permission;

/// <summary>
/// 路径权限中间件 — Default/Auto 模式下进行完整路径级权限检查
/// 对齐 TS checkReadPermissionForTool / checkWritePermissionForTool
/// </summary>
[Register(typeof(IPermissionMiddleware), ServiceLifetime.Singleton)]
public sealed partial class PathPermissionMiddleware : ServiceEntity, IPermissionMiddleware {
    private readonly IPathPermissionChecker? _pathPermissionChecker;

    /// <inheritdoc />

    /// <inheritdoc />

    /// <summary>
    /// 创建 PathPermissionMiddleware
    /// </summary>
    public PathPermissionMiddleware(IPathPermissionChecker? pathPermissionChecker = null) {
        _pathPermissionChecker = pathPermissionChecker;
    }

    /// <inheritdoc />
    public Task InvokeAsync(PermissionCheckContext context, MiddlewareDelegate<PermissionCheckContext> next, CancellationToken ct) {
        if (context.Arguments is null)
            return next(context, ct);

        // 仅 Default 和 Auto 模式需要路径权限检查
        if (context.CurrentMode != PermissionMode.Auto)
            return next(context, ct);

        if (PermissionCheckContext.ExtractPathFromArguments(context.Arguments) is not { } path || string.IsNullOrEmpty(path))
            return next(context, ct);

        var pathResult = CheckPathPermission(context);
        if (pathResult is not null && !pathResult.IsApproved) {
            context.Result = pathResult;
            return Task.CompletedTask;
        }

        return next(context, ct);
    }

    /// <summary>
    /// 路径级权限检查 — 对齐 TS checkReadPermissionForTool / checkWritePermissionForTool
    /// </summary>
    private ToolPermissionCheckResult? CheckPathPermission(PermissionCheckContext context) {
        var path = context.Arguments is not null ? PermissionCheckContext.ExtractPathFromArguments(context.Arguments) : null;
        if (string.IsNullOrEmpty(path))
            return null;

        var toolName = context.ToolName;

        var isReadTool = PermissionCheckContext.IsFileReadTool(toolName);
        var isWriteTool = PermissionCheckContext.IsFileWriteTool(toolName);

        // checker 缺失属于配置错误 — fail-closed（此前静默放行是防线缺口，ADR 0138）
        // ① 为什么拒绝: 路径级守卫未注册，无法判定工作目录边界；② 触发参数: 文件类工具带 path；
        // ③ 拦截层: PathPermissionMiddleware；④ 正确做法: 检查 PathPermissionChecker 注册DI/权限管道组装
        if (!isReadTool && !isWriteTool)
            return null;
        if (_pathPermissionChecker is null)
            return ToolPermissionCheckResult.PendingConfirmation($"工具 '{toolName}' 请求路径 '{path}': 路径权限检查器未注册，为安全起见需要手动确认（层: PathPermissionMiddleware）");

        // 读取工具: 调用 CheckReadPermission
        if (isReadTool) {
            var result = _pathPermissionChecker.CheckReadPermission(path);
            return PermissionCheckContext.MapPathResult(result);
        }

        // 写入/编辑工具: 调用 CheckWritePermission
        var writeResult = _pathPermissionChecker.CheckWritePermission(path);
        return PermissionCheckContext.MapPathResult(writeResult);
    }
}