
namespace McpToolRegistry;

/// <summary>
/// 权限检查中间件 — Order=500 — 检查工具执行权限
/// </summary>
[Register]
public sealed partial class PermissionCheckMiddleware : ServiceEntity, IToolExecutionMiddleware
{

    private readonly IPermissionCheckingInterceptor? _permissionInterceptor;
    [Inject] private readonly ILogger<PermissionCheckMiddleware> _logger;

    public PermissionCheckMiddleware(
        IPermissionCheckingInterceptor? permissionInterceptor,
        ILogger<PermissionCheckMiddleware> logger)
    {
        _permissionInterceptor = permissionInterceptor;
        _logger = logger;
    }

    public async Task InvokeAsync(
        ToolExecutionContext context,
        MiddlewareDelegate<ToolExecutionContext> next,
        CancellationToken ct)
    {
        if (_permissionInterceptor is null)
        {
            _logger.LogDebug(L.T(StringKey.PermissionCheckSkippedLog));
        }
        else
        {
            var invokeContext = new ToolInvokeContext(context.ToolName, context.Arguments);
            _logger.LogDebug(L.T(StringKey.PermissionCheckStartLog, context.ToolName, invokeContext.RequestId));
            try
            {
                await _permissionInterceptor.CheckPermissionOrThrowAsync(invokeContext, ct).ConfigureAwait(false);
            }
            catch (PermissionPendingConfirmationException ex) when (string.Equals(context.ToolName, WebToolNameConstants.WebFetch, StringComparison.OrdinalIgnoreCase))
            {
                var ruleContent = ExtractWebFetchRuleContent(context.Arguments);
                throw new PermissionPendingConfirmationException(ex.ToolName, ex.ConfirmationPrompt, ex.RequestId, ruleContent);
            }
            _logger.LogInformation(L.T(StringKey.PermissionCheckPassedLog, context.ToolName, invokeContext.RequestId));
        }

        await next(context, ct).ConfigureAwait(false);
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
