namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 工具过滤策略接口 — 3 层收敛后的统一工具过滤入口。
/// 对齐 TS 原版 的 filterToolsForAgent 3 层设计：
/// 1. AllAgentDisallowedTools（全局禁用，防递归）
/// 2. AsyncAgentAllowedTools（白名单）
/// 3. AgentDefinition.DisallowedTools（agent 定义级黑名单）
/// </summary>
public interface IToolFilterPolicy
{
    /// <summary>检查工具是否允许执行。</summary>
    /// <param name="context">过滤上下文。</param>
    /// <returns>过滤结果（是否允许 + 拒绝原因）。</returns>
    ToolFilterResult Check(ToolFilterContext context);
}

/// <summary>
/// 工具过滤上下文 — 3 层检查的输入。
/// </summary>
/// <param name="ToolName">工具名称。</param>
/// <param name="Mode">权限模式。</param>
/// <param name="AllAgentDisallowedTools">全局禁用集（层 1，防递归）。</param>
/// <param name="AgentAllowedTools">代理白名单（层 2，null 或空表示无白名单限制）。</param>
/// <param name="AgentDisallowedTools">代理黑名单（层 3，agent 定义级）。</param>
public sealed record ToolFilterContext(
    string ToolName,
    PermissionMode Mode,
    IReadOnlySet<string> AllAgentDisallowedTools,
    IReadOnlySet<string>? AgentAllowedTools,
    IReadOnlySet<string>? AgentDisallowedTools);

/// <summary>
/// 工具过滤结果。
/// </summary>
/// <param name="IsAllowed">是否允许执行。</param>
/// <param name="Reason">拒绝原因（允许时为 null）。</param>
/// <param name="DeniedLayer">拒绝层编号（1=全局禁用, 2=白名单, 3=代理黑名单, 0=允许）。</param>
public sealed record ToolFilterResult(bool IsAllowed, string? Reason, int DeniedLayer)
{
    /// <summary>允许结果。</summary>
    public static readonly ToolFilterResult Allowed = new(true, null, 0);

    /// <summary>创建拒绝结果。</summary>
    /// <param name="reason">拒绝原因。</param>
    /// <param name="layer">拒绝层编号。</param>
    /// <returns>拒绝结果。</returns>
    public static ToolFilterResult Denied(string reason, int layer) => new(false, reason, layer);
}
