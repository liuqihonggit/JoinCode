
namespace McpToolRegistry;

/// <summary>
/// 权限检查中间件 — Order=500 — 检查工具执行权限
/// </summary>
[Register(typeof(IToolExecutionMiddleware), ServiceLifetime.Singleton)]
public sealed partial class PermissionCheckMiddleware : ServiceEntity, IToolExecutionMiddleware
{

    private readonly IPermissionCheckingInterceptor? _permissionInterceptor;
    private readonly ILogger<PermissionCheckMiddleware> _logger;

    /// <summary>
    /// 构造函数 — 注入权限检查拦截器和日志记录器
    /// </summary>
    /// <param name="permissionInterceptor">权限检查拦截器实例，为 null 则跳过权限检查</param>
    /// <param name="logger">日志记录器实例</param>
    public PermissionCheckMiddleware(
        IPermissionCheckingInterceptor? permissionInterceptor,
        ILogger<PermissionCheckMiddleware> logger)
    {
        _permissionInterceptor = permissionInterceptor;
        _logger = logger;
    }

    /// <summary>
    /// 调用权限拦截器检查工具执行权限；按决定 Allowed/Denied/PendingConfirmation 分别放行、拒绝或要求确认（WebFetch 自动提取域名作为 ruleContent）
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
        if (_permissionInterceptor is null)
        {
            _logger.LogDebug(L.T(StringKey.PermissionCheckSkippedLog));
            await next(context, ct).ConfigureAwait(false);
            return;
        }

        var invokeContext = new ToolInvokeContext(context.ToolName, context.Arguments);
        _logger.LogDebug(L.T(StringKey.PermissionCheckStartLog, context.ToolName, invokeContext.RequestId));

        var outcome = await _permissionInterceptor.CheckPermissionAsync(invokeContext, ct).ConfigureAwait(false);

        switch (outcome.Decision)
        {
            case PermissionDecision.Allowed:
                _logger.LogInformation(L.T(StringKey.PermissionCheckPassedLog, context.ToolName, invokeContext.RequestId));
                await next(context, ct).ConfigureAwait(false);
                break;

            case PermissionDecision.Denied:
                _logger.LogWarning("工具权限被拒绝: Tool={ToolName}, Reason={Reason}", context.ToolName, outcome.DenyReason);
                context.Deny(outcome.DenyReason ?? "权限被拒绝");
                break;

            case PermissionDecision.PendingConfirmation:
                var ruleContent = outcome.RuleContent;
                if (string.IsNullOrEmpty(ruleContent) && string.Equals(context.ToolName, WebToolNameEnumConstants.WebFetch, StringComparison.OrdinalIgnoreCase))
                {
                    ruleContent = ExtractWebFetchRuleContent(context.Arguments);
                }
                _logger.LogInformation("工具需要确认: Tool={ToolName}, Prompt={Prompt}", context.ToolName, outcome.ConfirmationPrompt);
                context.RequireConfirmation(outcome.ConfirmationPrompt ?? "需要确认", ruleContent);
                break;
        }
    }

    /// <summary>
    /// 提取 WebFetch 的 ruleContent — domain:hostname 格式，用于域名级白名单持久化
    /// 对齐 ChatToolOrchestrator 原有逻辑
    /// </summary>
    private static string? ExtractWebFetchRuleContent(Dictionary<string, JsonElement> arguments)
    {
        if (arguments.TryGetValue("url", out var urlEl) && urlEl.ValueKind == JsonValueKind.String)
        {
            if (Uri.TryCreate(urlEl.GetString(), UriKind.Absolute, out var parsed))
            {
                return $"domain:{parsed.Host}";
            }
        }
        return null;
    }
}
