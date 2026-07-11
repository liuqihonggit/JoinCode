
namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 工具权限管理器接口，负责管理工具执行权限的检查和控制
/// </summary>
public interface IToolPermissionManager
{
    /// <summary>
    /// 检查权限请求
    /// </summary>
    /// <param name="request">权限请求</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>权限检查结果</returns>
    Task<PermissionResult> CheckPermissionAsync(PermissionRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 设置权限模式
    /// </summary>
    /// <param name="mode">权限模式</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task SetPermissionModeAsync(PermissionMode mode, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取当前权限模式
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>当前权限模式</returns>
    Task<PermissionMode> GetCurrentModeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 对齐 TS allowedPrompts: 添加语义级Bash权限
    /// 允许LLM在退出plan模式时请求特定Bash命令的自动批准
    /// </summary>
    /// <param name="prompt">语义描述（如"run tests"、"install dependencies"）</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task AddAllowedPromptAsync(string prompt, CancellationToken cancellationToken = default);

    /// <summary>
    /// 对齐 TS stripDangerousPermissionsForAutoMode: 从Auto模式进入Plan时剥离危险权限规则
    /// 返回被剥离的规则数量，供退出Plan时恢复
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>被剥离的危险权限规则数量</returns>
    Task<int> StripDangerousRulesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 对齐 TS restoreDangerousPermissions: 退出Plan时恢复之前剥离的危险权限规则
    /// </summary>
    /// <param name="ruleCount">之前剥离的规则数量</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task RestoreDangerousRulesAsync(int ruleCount, CancellationToken cancellationToken = default);

    /// <summary>
    /// 清除权限缓存 — 对齐 TS: IToolPermissionManager.ClearCache
    /// </summary>
    void ClearCache();
}
