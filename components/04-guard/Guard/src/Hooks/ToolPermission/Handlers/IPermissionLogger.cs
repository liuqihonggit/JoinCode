namespace Core.Hooks.ToolPermission;

/// <summary>
/// 权限日志记录器接口
/// </summary>
public interface IPermissionLogger
{
    /// <summary>
    /// 记录权限决策
    /// </summary>
    /// <param name="context">日志上下文</param>
    /// <param name="args">决策参数</param>
    void LogPermissionDecision(PermissionLogContext context, PermissionDecisionArgs args);

    /// <summary>
    /// 记录权限取消
    /// </summary>
    /// <param name="context">日志上下文</param>
    void LogPermissionCancelled(PermissionLogContext context);

    /// <summary>
    /// 记录代码编辑工具决策
    /// </summary>
    /// <param name="toolName">工具名称</param>
    /// <param name="decision">决策结果</param>
    /// <param name="source">决策来源</param>
    /// <param name="language">编程语言（可选）</param>
    void LogCodeEditToolDecision(string toolName, string decision, string source, string? language = null);
}
