
namespace McpToolRegistry;

/// <summary>
/// Agent 工具限制检查中间件 — Order=400 — 检查当前 Agent 模式是否允许使用该工具。
/// 内部通过 IToolFilterPolicy 统一 3 层过滤检查（对齐 claude code filterToolsForAgent）。
/// </summary>
[Register]
public sealed partial class AgentRestrictionMiddleware : ServiceEntity, IToolExecutionMiddleware
{

    private readonly IAgentToolRestrictions? _agentToolRestrictions;
    private readonly IToolFilterPolicy? _toolFilterPolicy;
    [Inject] private readonly ILogger<AgentRestrictionMiddleware> _logger;

    public AgentRestrictionMiddleware(
        IAgentToolRestrictions? agentToolRestrictions,
        ILogger<AgentRestrictionMiddleware> logger,
        IToolFilterPolicy? toolFilterPolicy = null)
    {
        _agentToolRestrictions = agentToolRestrictions;
        _logger = logger;
        _toolFilterPolicy = toolFilterPolicy;
    }

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
                    throw new PermissionDeniedException(
                        PermissionResourceType.Tool,
                        context.ToolName,
                        filterResult.Reason ?? L.T(StringKey.ToolNotAllowedInMode, context.ToolName, context.AgentMode));
                }

                _logger.LogDebug(L.T(StringKey.AgentToolLimitPassedLog, context.ToolName, context.AgentMode));
            }
            else if (!_agentToolRestrictions.IsToolAllowedForMode(context.ToolName, context.AgentMode))
            {
                _logger.LogWarning(L.T(StringKey.AgentToolLimitDeniedLog, context.ToolName, context.AgentMode));
                throw new PermissionDeniedException(
                    PermissionResourceType.Tool,
                    context.ToolName,
                    L.T(StringKey.ToolNotAllowedInMode, context.ToolName, context.AgentMode));
            }
            else
            {
                _logger.LogDebug(L.T(StringKey.AgentToolLimitPassedLog, context.ToolName, context.AgentMode));
            }
        }

        await next(context, ct).ConfigureAwait(false);
    }
}
