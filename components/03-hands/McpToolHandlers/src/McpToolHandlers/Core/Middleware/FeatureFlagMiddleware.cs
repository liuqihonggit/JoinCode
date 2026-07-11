
namespace McpToolRegistry;

/// <summary>
/// FeatureFlag 检查中间件 — Order=700 — 检查工具是否被 FeatureFlag 禁用
/// </summary>
[Register]
public sealed partial class FeatureFlagMiddleware : IToolExecutionMiddleware
{
    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    private readonly IFeatureFlagService? _featureFlagService;
    [Inject] private readonly ILogger<FeatureFlagMiddleware> _logger;

    public FeatureFlagMiddleware(
        IFeatureFlagService? featureFlagService,
        ILogger<FeatureFlagMiddleware> logger)
    {
        _featureFlagService = featureFlagService;
        _logger = logger;
    }

    public async Task InvokeAsync(
        ToolExecutionContext context,
        MiddlewareDelegate<ToolExecutionContext> next,
        CancellationToken ct)
    {
        if (_featureFlagService is not null)
        {
            var featureKey = $"tool.{context.ToolName}.enabled";
            var isEnabled = await _featureFlagService.IsEnabledAsync(
                featureKey, cancellationToken: ct).ConfigureAwait(false);

            if (!isEnabled)
            {
                _logger.LogWarning(L.T(StringKey.FeatureFlagDisabledLog, context.ToolName, featureKey));
                throw new PermissionDeniedException(
                    PermissionResourceType.Tool,
                    context.ToolName,
                    L.T(StringKey.FeatureFlagDisabledTool, context.ToolName));
            }
        }

        await next(context, ct).ConfigureAwait(false);
    }
}
