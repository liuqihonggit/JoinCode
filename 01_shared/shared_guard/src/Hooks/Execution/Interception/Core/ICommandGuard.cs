namespace Core.Hooks.Execution.Interception;

/// <summary>
/// 命令守卫 — 无状态瞬时决策器,对命令做放行/改写/拒绝/转交决策
/// <para>
/// 特征:纯函数、无副作用、可同步。适合拒绝、改写、转交、放行等瞬时决策。
/// 由 <see cref="CommandInterceptionDispatcher"/> 按优先级统一调度。
/// </para>
/// <para>
/// 实现示例:
/// <list type="bullet">
/// <item><c>GitCommitGuard</c> — 拦截 git commit,Redirect 到 /commit</item>
/// <item><c>GhPrBodyGuard</c> — 为 gh pr create 补全 --body 参数(Rewrite)</item>
/// <item><c>VpnRouteGuard</c> — VPN 激活时为 git/curl 加代理(Rewrite)</item>
/// </list>
/// </para>
/// </summary>
public interface ICommandGuard
{
    /// <summary>
    /// 优先级 — 数值越大越先评估(对齐旧 ICommandRewriter.Priority 语义)
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// 守卫名称 — 用于日志和调试
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 是否处理该命令 — 快速过滤,避免对无关命令调用 <see cref="Evaluate"/>
    /// </summary>
    /// <param name="command">待评估的命令</param>
    /// <param name="context">执行上下文(如 ShellKind 等)</param>
    /// <returns>处理该命令返回 true,否则 false</returns>
    bool CanHandle(string command, IReadOnlyDictionary<string, object> context);

    /// <summary>
    /// 评估决策 — 纯函数,无副作用,不执行任何实际操作
    /// </summary>
    /// <param name="command">待评估的命令</param>
    /// <param name="context">执行上下文</param>
    /// <returns>干预决策(Allow/Rewrite/Deny/Redirect/Handoff)</returns>
    CommandDecision Evaluate(string command, IReadOnlyDictionary<string, object> context);
}
