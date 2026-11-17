namespace Core.Hooks.Execution.Interception;

/// <summary>
/// 命令守卫 — 无状态瞬时决策器,对命令做放行/改写/拒绝/转交决策
/// <para>
/// 特征:纯函数、无副作用、可同步。适合拒绝、改写、转交、放行等瞬时决策。
/// 由 <see cref="CommandInterceptionDispatcher"/> 按注册顺序表统一调度
/// (顺序来自 resources/orders.yaml 的 command_guards 列表)。
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
public interface ICommandGuard {
    /// <summary>
    /// 守卫名称 — 用于日志和调试,同时作为注册顺序表里的注册名
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 执行位次 — 数值越大越先评估。
    /// <para>值必须由注册顺序表派生（<c>OrderIndex.Default.PriorityOf("command_guards", ...)</c>）,
    /// 实现类不得写死数字：数字只能表达相对大小,想知道谁先执行必须横向比对所有实现,
    /// 而且数字与注册名是两套标识,改类名要同步改数字。</para>
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// 是否处理该命令 — 快速过滤,避免对无关命令调用 <see cref="Evaluate"/>
    /// </summary>
    /// <param name="command">待评估的命令</param>
    /// <param name="context">执行上下文(强类型,含 ShellKind 等)</param>
    /// <returns>处理该命令返回 true,否则 false</returns>
    bool CanHandle(string command, GuardContext context);

    /// <summary>
    /// 评估决策 — 纯函数,无副作用,不执行任何实际操作
    /// </summary>
    /// <param name="command">待评估的命令</param>
    /// <param name="context">执行上下文(强类型)</param>
    /// <returns>干预决策(Allow/Rewrite/Deny/Redirect/Handoff)</returns>
    CommandDecision Evaluate(string command, GuardContext context);
}