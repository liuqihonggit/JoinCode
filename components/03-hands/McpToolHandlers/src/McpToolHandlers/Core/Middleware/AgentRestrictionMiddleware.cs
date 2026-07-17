
namespace McpToolRegistry;

/// <summary>
/// Agent 工具限制检查中间件 — Order=400 — 检查当前 Agent 模式是否允许使用该工具
/// </summary>
[Register]
public sealed partial class AgentRestrictionMiddleware : IToolExecutionMiddleware
{

    private readonly IAgentToolRestrictions? _agentToolRestrictions;
    [Inject] private readonly ILogger<AgentRestrictionMiddleware> _logger;

    public AgentRestrictionMiddleware(
        IAgentToolRestrictions? agentToolRestrictions,
        ILogger<AgentRestrictionMiddleware> logger)
    {
        _agentToolRestrictions = agentToolRestrictions;
        _logger = logger;
    }

    public async Task InvokeAsync(
        ToolExecutionContext context,
        MiddlewareDelegate<ToolExecutionContext> next,
        CancellationToken ct)
    {
        if (_agentToolRestrictions is not null)
        {
            if (!_agentToolRestrictions.IsToolAllowedForMode(context.ToolName, context.AgentMode))
            {
                _logger.LogWarning(L.T(StringKey.AgentToolLimitDeniedLog, context.ToolName, context.AgentMode));
                throw new PermissionDeniedException(
                    PermissionResourceType.Tool,
                    context.ToolName,
                    L.T(StringKey.ToolNotAllowedInMode, context.ToolName, context.AgentMode));
            }

            _logger.LogDebug(L.T(StringKey.AgentToolLimitPassedLog, context.ToolName, context.AgentMode));
        }

        await next(context, ct).ConfigureAwait(false);
    }
}
