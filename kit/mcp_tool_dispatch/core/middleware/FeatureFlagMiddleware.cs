
namespace McpToolRegistry;

/// <summary>
/// FeatureFlag 检查中间件 — Order=700 — 检查工具是否被 FeatureFlag 禁用
/// </summary>
[Register(typeof(IToolExecutionMiddleware), ServiceLifetime.Singleton)]
public sealed partial class FeatureFlagMiddleware : ServiceEntity, IToolExecutionMiddleware
{

    private readonly IFeatureFlagService? _featureFlagService;
    private readonly ILogger<FeatureFlagMiddleware> _logger;

    /// <summary>
    /// 构造函数 — 注入 FeatureFlag 服务和日志记录器
    /// </summary>
    /// <param name="featureFlagService">FeatureFlag 服务实例，为 null 则跳过特性开关检查</param>
    /// <param name="logger">日志记录器实例</param>
    public FeatureFlagMiddleware(
        IFeatureFlagService? featureFlagService,
        ILogger<FeatureFlagMiddleware> logger)
    {
        _featureFlagService = featureFlagService;
        _logger = logger;
    }

    /// <summary>
    /// 查询 tool.&lt;工具名&gt;.enabled 特性开关；若禁用则拒绝工具执行，否则调用下一层中间件
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
        if (_featureFlagService is not null)
        {
            var featureKey = $"tool.{context.ToolName}.enabled";
            var isEnabled = await _featureFlagService.IsEnabledAsync(
                featureKey, cancellationToken: ct).ConfigureAwait(false);

            if (!isEnabled)
            {
                _logger.LogWarning(L.T(StringKey.FeatureFlagDisabledLog, context.ToolName, featureKey));
                context.Deny(L.T(StringKey.FeatureFlagDisabledTool, context.ToolName));
                return;
            }
        }

        await next(context, ct).ConfigureAwait(false);
    }
}
