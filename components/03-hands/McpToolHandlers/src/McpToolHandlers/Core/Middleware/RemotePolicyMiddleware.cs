
namespace McpToolRegistry;

/// <summary>
/// 远程策略检查中间件 — Order=600 — 检查远程策略是否允许工具执行
/// </summary>
[Register]
public sealed partial class RemotePolicyMiddleware : IToolExecutionMiddleware
{
    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    private readonly IRemotePolicyService? _remotePolicyService;
    [Inject] private readonly ILogger<RemotePolicyMiddleware> _logger;

    public RemotePolicyMiddleware(
        IRemotePolicyService? remotePolicyService,
        ILogger<RemotePolicyMiddleware> logger)
    {
        _remotePolicyService = remotePolicyService;
        _logger = logger;
    }

    public async Task InvokeAsync(
        ToolExecutionContext context,
        MiddlewareDelegate<ToolExecutionContext> next,
        CancellationToken ct)
    {
        if (_remotePolicyService is not null)
        {
            var policyContext = new Dictionary<string, string>
            {
                ["toolName"] = context.ToolName
            };

            foreach (var kvp in context.Arguments)
            {
                policyContext[$"arg_{kvp.Key}"] = kvp.Value.ValueKind == JsonValueKind.String
                    ? kvp.Value.GetString() ?? string.Empty
                    : kvp.Value.GetRawText();
            }

            var result = await _remotePolicyService.EvaluateAsync(
                context.ToolName, policyContext, ct).ConfigureAwait(false);

            if (!result.Allowed)
            {
                _logger.LogWarning(L.T(StringKey.RemotePolicyDeniedLog,
                    context.ToolName, result.RuleId, result.Reason));
                throw new PermissionDeniedException(
                    PermissionResourceType.Tool,
                    context.ToolName,
                    L.T(StringKey.RemotePolicyDeniedTool, context.ToolName, result.Reason));
            }
        }

        await next(context, ct).ConfigureAwait(false);
    }
}
