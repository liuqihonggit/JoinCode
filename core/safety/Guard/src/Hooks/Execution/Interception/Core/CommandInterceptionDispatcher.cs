namespace Core.Hooks.Execution.Interception;

/// <summary>
/// 命令拦截调度结果 — 包含最终命令(可能被改写)和短路结果
/// </summary>
/// <param name="FinalCommand">最终命令(经过守卫链式改写后)</param>
/// <param name="ShortCircuitResult">短路结果,null 表示放行(调用方应继续管道)</param>
public sealed record DispatchOutcome(string FinalCommand, ToolResult? ShortCircuitResult);

/// <summary>
/// 命令拦截调度器 — 统一调度守卫链和拦截器链
/// <para>
/// 调度流程:
/// <list type="number">
/// <item>守卫链(按优先级降序):Allow 继续 | Rewrite 改写+继续 | Deny/Redirect 短路 | Handoff 跳到阶段2</item>
/// <item>拦截器链(按优先级降序):Handled 短路 | Continue 继续</item>
/// <item>放行 — 返回 ShortCircuitResult = null,调用方继续管道</item>
/// </list>
/// </para>
/// <para>
/// 设计要点:
/// <list type="bullet">
/// <item>守卫无状态纯决策,拦截器有状态异步处理,分离关注点</item>
/// <item>链式改写 — Rewrite 后继续评估下一个守卫(如 VPN 加代理后 gh pr 再加 --body)</item>
/// <item>Deny/Redirect 优先短路 — 安全拒绝在补全参数之前生效</item>
/// <item>状态机局部化 — 拦截器内部状态(如 sed 两阶段)不暴露给调度器</item>
/// </list>
/// </para>
/// </summary>
[Register]
public sealed partial class CommandInterceptionDispatcher : ServiceEntity
{
    private readonly ICommandGuard[] _guards;
    private readonly ICommandInterceptor[] _interceptors;
    [Inject] private readonly ILogger<CommandInterceptionDispatcher>? _logger;

    /// <summary>
    /// 构造调度器 — DI 注入所有守卫和拦截器,按优先级降序预排序
    /// </summary>
    /// <param name="guards">守卫集合(DI 自动收集所有 <see cref="ICommandGuard"/> 实现)</param>
    /// <param name="interceptors">拦截器集合(DI 自动收集所有 <see cref="ICommandInterceptor"/> 实现)</param>
    /// <param name="logger">日志器(可选)</param>
    public CommandInterceptionDispatcher(
        IEnumerable<ICommandGuard> guards,
        IEnumerable<ICommandInterceptor> interceptors,
        ILogger<CommandInterceptionDispatcher>? logger = null)
    {
        _guards = guards.OrderByDescending(static g => g.Priority).ToArray();
        _interceptors = interceptors.OrderByDescending(static i => i.Priority).ToArray();
        _logger = logger;
    }

    /// <summary>
    /// 调度命令 — 守卫链 → 拦截器链 → 放行
    /// </summary>
    /// <param name="command">待调度的命令</param>
    /// <param name="context">执行上下文(如 ShellKind 等,透传给守卫和拦截器)</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>调度结果 — ShortCircuitResult 为 null 表示放行,非 null 表示短路</returns>
    public async Task<DispatchOutcome> DispatchAsync(
        string command,
        IReadOnlyDictionary<string, object> context,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        var currentCommand = command;

        // 阶段1: 守卫链(无状态瞬时决策)
        foreach (var guard in _guards)
        {
            if (!guard.CanHandle(currentCommand, context)) continue;

            var decision = guard.Evaluate(currentCommand, context);

            if (decision is CommandDecision.Rewrite r)
            {
                _logger?.LogInformation(
                    "命令改写({Guard}): {Old} → {New}{Reason}",
                    guard.Name, currentCommand, r.NewCommand,
                    r.Reason is null ? null : $" ({r.Reason})");
                currentCommand = r.NewCommand;
                continue;
            }

            if (decision is CommandDecision.Deny d)
            {
                _logger?.LogInformation("命令拒绝({Guard}): {Reason}", guard.Name, d.Diagnostic.Reason);
                return new DispatchOutcome(currentCommand, BuildDenyResult(d));
            }

            if (decision is CommandDecision.Redirect red)
            {
                _logger?.LogInformation("命令转交({Guard}): → {Target}", guard.Name, red.TargetTool);
                return new DispatchOutcome(currentCommand, BuildRedirectResult(red));
            }

            if (decision is CommandDecision.Handoff)
            {
                _logger?.LogDebug("守卫 {Guard} Handoff,降级到拦截器层", guard.Name);
                break;
            }

            // Allow: 继续下一个守卫
        }

        // 阶段2: 拦截器链(有状态异步处理)
        foreach (var interceptor in _interceptors)
        {
            if (!interceptor.CanHandle(currentCommand, context)) continue;

            InterceptResult result;
            try
            {
                result = await interceptor.HandleAsync(currentCommand, context, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "拦截器 {Name} 处理命令时抛异常,跳过", interceptor.Name);
                continue;
            }

            if (result is InterceptResult.Handled h)
            {
                _logger?.LogDebug("拦截器 {Name} 处理命令(短路)", interceptor.Name);
                return new DispatchOutcome(currentCommand, h.Result);
            }

            // Continue: 继续下一个拦截器
        }

        // 阶段3: 放行
        return new DispatchOutcome(currentCommand, null);
    }

    /// <summary>
    /// 构建拒绝结果 — 对齐 <see cref="ToolResultBuilder.Error"/> 模式
    /// </summary>
    private static ToolResult BuildDenyResult(CommandDecision.Deny d) =>
        ToolResultBuilder.Error()
            .WithText(d.Diagnostic.FormattedMessage)
            .WithDiagnostic(d.Diagnostic)
            .Build();

    /// <summary>
    /// 构建转交引导结果 — 软引导,返回提示文本由 LLM 自行调用目标工具
    /// </summary>
    private static ToolResult BuildRedirectResult(CommandDecision.Redirect red)
    {
        var builder = ToolResultBuilder.Error().WithText(red.Hint);
        if (red.Diagnostic is not null)
            builder = builder.WithDiagnostic(red.Diagnostic);
        return builder.Build();
    }

    /// <summary>
    /// 获取已排序的守卫列表(诊断/测试用)
    /// </summary>
    public IReadOnlyList<ICommandGuard> GetGuards() => _guards;

    /// <summary>
    /// 获取已排序的拦截器列表(诊断/测试用)
    /// </summary>
    public IReadOnlyList<ICommandInterceptor> GetInterceptors() => _interceptors;
}
