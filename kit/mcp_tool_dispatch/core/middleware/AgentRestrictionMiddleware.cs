
namespace McpToolRegistry;

/// <summary>
/// Agent 工具限制检查中间件 — Order=400 — 检查当前 Agent 模式是否允许使用该工具。
/// 内部通过 IToolFilterPolicy 统一 3 层过滤检查（对齐 TS 原版 filterToolsForAgent）。
/// </summary>
[Register(typeof(IToolExecutionMiddleware), ServiceLifetime.Singleton)]
public sealed partial class AgentRestrictionMiddleware : ServiceEntity, IToolExecutionMiddleware
{

    private readonly IAgentToolRestrictions? _agentToolRestrictions;
    private readonly IToolFilterPolicy? _toolFilterPolicy;
    private readonly ILogger<AgentRestrictionMiddleware> _logger;

    /// <summary>
    /// 构造函数 — 注入 Agent 工具限制策略、日志记录器和工具过滤策略
    /// </summary>
    /// <param name="agentToolRestrictions">Agent 工具限制策略实例，为 null 则跳过限制检查</param>
    /// <param name="logger">日志记录器实例</param>
    /// <param name="toolFilterPolicy">工具过滤策略实例，为 null 则回退到 IsToolAllowedForMode 简单判断</param>
    public AgentRestrictionMiddleware(
        IAgentToolRestrictions? agentToolRestrictions,
        ILogger<AgentRestrictionMiddleware> logger,
        IToolFilterPolicy? toolFilterPolicy = null)
    {
        _agentToolRestrictions = agentToolRestrictions;
        _logger = logger;
        _toolFilterPolicy = toolFilterPolicy;
    }

    /// <summary>
    /// Bypass 模式直接放行；否则按 IToolFilterPolicy 三层过滤（或回退到 IsToolAllowedForMode）检查当前 Agent 模式是否允许使用该工具，不允许则拒绝并返回原因
    /// </summary>
    /// <param name="context">工具执行上下文</param>
    /// <param name="next">下一层中间件委托</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task InvokeAsync(
        ToolExecutionContext context,
        MiddlewareDelegate<ToolExecutionContext> next,
        CancellationToken ct)
    {
        if (context.AgentMode == PermissionMode.Bypass)
        {
            await next(context, ct).ConfigureAwait(false);
            return;
        }

        if (_agentToolRestrictions is not null)
        {
            if (_toolFilterPolicy is not null)
            {
                var deniedTools = _agentToolRestrictions.GetDeniedTools(context.AgentMode);
                var filterContext = new ToolFilterContext(
                    context.ToolName,
                    context.AgentMode,
                    deniedTools,
                    null,
                    null);
                var filterResult = _toolFilterPolicy.Check(filterContext);
                if (!filterResult.IsAllowed)
                {
                    _logger.LogWarning(L.T(StringKey.AgentToolLimitDeniedLog, context.ToolName, context.AgentMode));
                    context.Deny(filterResult.Reason ?? L.T(StringKey.ToolNotAllowedInMode, context.ToolName, context.AgentMode));
                    return;
                }

                _logger.LogDebug(L.T(StringKey.AgentToolLimitPassedLog, context.ToolName, context.AgentMode));
            }
            else if (!_agentToolRestrictions.IsToolAllowedForMode(context.ToolName, context.AgentMode))
            {
                _logger.LogWarning(L.T(StringKey.AgentToolLimitDeniedLog, context.ToolName, context.AgentMode));
                context.Deny(L.T(StringKey.ToolNotAllowedInMode, context.ToolName, context.AgentMode));
                return;
            }
            else
            {
                _logger.LogDebug(L.T(StringKey.AgentToolLimitPassedLog, context.ToolName, context.AgentMode));
            }
        }

        await next(context, ct).ConfigureAwait(false);
    }
}
