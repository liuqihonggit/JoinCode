namespace Core.Hooks.ToolPermission;

/// <summary>
/// 权限钩子执行器接口 — 注册、注销并执行工具权限钩子链
/// </summary>
public interface IPermissionHookExecutor
{
    /// <summary>
    /// 异步注册权限钩子
    /// </summary>
    /// <param name="hook">要注册的权限钩子</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task RegisterHookAsync(IPermissionHook hook, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按名称异步注销权限钩子
    /// </summary>
    /// <param name="hookName">钩子名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task UnregisterHookAsync(string hookName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步执行所有已注册权限钩子，按注册顺序返回结果流
    /// </summary>
    /// <param name="toolName">工具名称</param>
    /// <param name="toolUseId">工具调用 ID</param>
    /// <param name="input">工具输入参数</param>
    /// <param name="permissionMode">权限模式</param>
    /// <param name="suggestions">权限更新建议</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>权限钩子结果异步流</returns>
    IAsyncEnumerable<PermissionHookResult> ExecuteHooksAsync(
        string toolName,
        string toolUseId,
        Dictionary<string, JsonElement> input,
        string? permissionMode,
        List<PermissionUpdate>? suggestions,
        CancellationToken cancellationToken = default);
}
