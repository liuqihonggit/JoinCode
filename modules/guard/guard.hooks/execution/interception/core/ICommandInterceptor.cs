namespace Core.Hooks.Execution.Interception;

/// <summary>
/// 命令拦截器 — 有状态异步处理器,处理需交互/状态的命令拦截
/// <para>
/// 特征:可有状态、可异步、可短路。适合两阶段确认、队列等待、交互对话等场景。
/// 由 <see cref="CommandInterceptionDispatcher"/> 在守卫链之后按优先级统一调度。
/// </para>
/// <para>
/// 实现示例:
/// <list type="bullet">
/// <item><c>SedInterceptor</c> — sed -i 两阶段确认(预览→应用),内部封装状态机</item>
/// <item><c>BuildInterceptor</c> — dotnet build 队列化(提交→等待→返回)</item>
/// </list>
/// </para>
/// <para>
/// 设计原则:状态机局部化 — 框架只看到 <see cref="InterceptResult.Handled"/>/<see cref="InterceptResult.Continue"/>,
/// 复杂状态(如 sed 的 Idle→Previewing→Applying)封装在拦截器内部,不暴露给框架。
/// </para>
/// </summary>
public interface ICommandInterceptor {
    /// <summary>
    /// 优先级 — 数值越大越先评估
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// 是否安全关键拦截器 — 决定内部故障时的失败策略
    /// <para>
    /// true(默认): fail-closed — HandleAsync 抛异常时拒绝命令并返回引导报错,
    /// 安全拦截器故障不得静默放行(纵深防御,见 ADR 0140)。
    /// false: 纯审计/统计类拦截器,异常仅记录日志后跳过,不阻塞命令。
    /// </para>
    /// </summary>
    bool IsSafetyCritical => true;

    /// <summary>
    /// 拦截器名称 — 用于日志和调试
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 是否处理该命令 — 快速过滤
    /// </summary>
    /// <param name="command">待处理的命令</param>
    /// <param name="context">执行上下文(强类型)</param>
    /// <returns>处理该命令返回 true,否则 false</returns>
    bool CanHandle(string command, GuardContext context);

    /// <summary>
    /// 处理命令 — 可异步、可有状态、可短路
    /// </summary>
    /// <param name="command">待处理的命令</param>
    /// <param name="context">执行上下文(强类型)</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>处理结果(Handled 短路 / Continue 继续)</returns>
    Task<InterceptResult> HandleAsync(
        string command,
        GuardContext context,
        CancellationToken cancellationToken);
}