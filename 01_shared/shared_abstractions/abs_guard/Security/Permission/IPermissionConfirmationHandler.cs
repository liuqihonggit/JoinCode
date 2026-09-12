namespace JoinCode.Abstractions.Security.Permission;

/// <summary>
/// 权限确认处理器 — 工具执行需要用户确认时的交互入口
/// CLI 层实现 ^ 提示符交互，非交互环境返回 Deny
/// </summary>
public interface IPermissionConfirmationHandler
{
    /// <summary>
    /// 请求用户确认工具执行权限
    /// </summary>
    /// <param name="toolName">工具名称</param>
    /// <param name="confirmationPrompt">确认提示文本</param>
    /// <returns>用户确认结果</returns>
    PermissionConfirmAction Confirm(string toolName, string confirmationPrompt);
}

/// <summary>
/// 权限确认动作
/// </summary>
public enum PermissionConfirmAction
{
    /// <summary>拒绝执行</summary>
    Deny,
    /// <summary>本次允许执行</summary>
    Allow,
    /// <summary>始终允许（加入临时批准列表）</summary>
    AlwaysAllow
}
