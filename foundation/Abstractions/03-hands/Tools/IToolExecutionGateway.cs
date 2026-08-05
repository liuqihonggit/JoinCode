namespace JoinCode.Abstractions.Tools;

/// <summary>
/// 带权限检查与横切中间件的工具执行网关 — 所有工具调用的统一入口
/// 实现者: PermissionAwareToolExecutor（11 件中间件管道: 参数修复→校验→权限→执行）
/// 用途: 消除 IToolRegistry.ExecuteToolAsync 的散落调用点，统一收敛到权限管道
/// </summary>
public interface IToolExecutionGateway
{
    /// <summary>
    /// 通过权限管道执行工具调用 — 对齐 PermissionAwareToolExecutor.ExecuteAsync
    /// </summary>
    /// <param name="toolName">工具名称</param>
    /// <param name="arguments">工具参数</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <param name="onProgress">进度回调（可选）</param>
    /// <returns>工具执行结果</returns>
    Task<ToolResult> ExecuteAsync(
        string toolName,
        Dictionary<string, JsonElement> arguments,
        CancellationToken cancellationToken = default,
        ToolProgressCallback? onProgress = null);
}
