
namespace McpToolRegistry;

/// <summary>
/// 权限检查中间件 — Order=500 — 检查工具执行权限
/// </summary>
[Register]
public sealed partial class PermissionCheckMiddleware : IToolExecutionMiddleware
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
            await _permissionInterceptor.CheckPermissionOrThrowAsync(invokeContext, ct).ConfigureAwait(false);
            _logger.LogInformation(L.T(StringKey.PermissionCheckPassedLog, context.ToolName, invokeContext.RequestId));
        }

        await next(context, ct).ConfigureAwait(false);
    }
}
