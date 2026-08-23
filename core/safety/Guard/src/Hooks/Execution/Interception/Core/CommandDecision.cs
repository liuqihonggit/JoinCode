namespace Core.Hooks.Execution.Interception;

/// <summary>
/// 命令干预决策 — 守卫对命令的瞬时决策结果(无状态、纯函数)
/// <para>
/// 决策类型:
/// <list type="bullet">
/// <item><see cref="Allow"/> 放行,继续评估下一个守卫</item>
/// <item><see cref="Rewrite"/> 改写命令,更新命令后继续链(允许链式改写)</item>
/// <item><see cref="Deny"/> 硬拒绝,短路返回诊断</item>
/// <item><see cref="Redirect"/> 转交专用工具,短路返回引导提示(软引导,由 LLM 自行调用)</item>
/// <item><see cref="Handoff"/> 降级到拦截器层处理(需交互/状态的决策)</item>
/// </list>
/// </para>
/// </summary>
public abstract record CommandDecision
{
    /// <summary>
    /// 放行 — 继续评估下一个守卫,命令不变
    /// </summary>
    public sealed record Allow : CommandDecision;

    /// <summary>
    /// 改写命令 — 更新命令后继续链,允许链式改写(如 VPN 加代理后 gh pr 再加 --body)
    /// </summary>
    /// <param name="NewCommand">改写后的命令</param>
    /// <param name="Reason">改写原因(可选,用于日志)</param>
    public sealed record Rewrite(string NewCommand, string? Reason = null) : CommandDecision;

    /// <summary>
    /// 硬拒绝 — 短路返回诊断,命令不执行
    /// </summary>
    /// <param name="Diagnostic">拒绝诊断信息</param>
    public sealed record Deny(ToolDiagnostic Diagnostic) : CommandDecision;

    /// <summary>
    /// 转交专用工具 — 短路返回引导提示(软引导,由 LLM 自行调用目标工具)
    /// <para>
    /// 典型场景:拦截 bash 的 <c>git commit</c>,引导 LLM 使用 <c>/commit</c> 斜杠命令
    /// </para>
    /// </summary>
    /// <param name="TargetTool">目标工具名称(如 "/commit")</param>
    /// <param name="Hint">引导提示文本</param>
    /// <param name="Diagnostic">附加诊断(可选)</param>
    public sealed record Redirect(string TargetTool, string Hint, ToolDiagnostic? Diagnostic = null) : CommandDecision;

    /// <summary>
    /// 降级到拦截器层 — 退出守卫链,进入拦截器链处理(需交互/状态的决策)
    /// </summary>
    public sealed record Handoff : CommandDecision;
}
