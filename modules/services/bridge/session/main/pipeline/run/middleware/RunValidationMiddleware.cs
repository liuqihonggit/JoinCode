namespace Core.Bridge;

/// <summary>
/// Bridge 运行验证中间件 — 对齐 TS 端 RunAsync 早期参数验证
/// 检查帮助标志、参数错误、权限模式、访问令牌、远程控制确认、HTTPS URL
/// </summary>
[Register(typeof(IBridgeRunMiddleware), ServiceLifetime.Singleton)]
public sealed partial class RunValidationMiddleware : ServiceEntity, IBridgeRunMiddleware {
    private static readonly FrozenSet<string> ValidPermissionModes = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase, "default", "plan", "auto-accept", "bubble");

    /// <summary>
    /// 构造运行验证中间件
    /// </summary>
    /// <param name="deps">BridgeMain 依赖包</param>
    /// <param name="logger">日志记录器</param>
    public RunValidationMiddleware(BridgeMainDeps deps, ILogger<RunValidationMiddleware> logger) {
        _deps = deps;
        _logger = logger;
    }
    private readonly BridgeMainDeps _deps;
    private readonly ILogger<RunValidationMiddleware> _logger;


    /// <summary>
    /// 执行验证中间件 — 依次检查帮助、参数错误、权限模式、Token、远程确认、HTTPS
    /// </summary>
    /// <param name="ctx">Bridge 运行上下文</param>
    /// <param name="next">下一个中间件委托</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task InvokeAsync(BridgeRunContext ctx, MiddlewareDelegate<BridgeRunContext> next, CancellationToken ct) {
        if (ctx.Args.Help) {
            ctx.EarlyResult = new BridgeMainResult { HelpText = BridgeMainArgsParser.GetHelpText() };
            return;
        }

        if (ctx.Args.HasError) {
            ctx.EarlyResult = new BridgeMainResult { Error = ctx.Args.Error };
            return;
        }

        if (_deps.PermissionMode is not null) {
            if (!ValidPermissionModes.Contains(_deps.PermissionMode)) {
                ctx.EarlyResult = new BridgeMainResult { Error = $"Invalid permission mode '{_deps.PermissionMode}'. Valid modes: default, plan, auto-accept, bubble" };
                return;
            }
        }

        var accessToken = _deps.GetAccessToken();
        if (string.IsNullOrEmpty(accessToken)) {
            _logger.LogDebug("BridgeMain: no access token — skipping");
            ctx.EarlyResult = new BridgeMainResult { Error = "No access token available. Please login first." };
            return;
        }
        ctx.AccessToken = accessToken;

        var remoteDialogSeen = _deps.CheckRemoteDialogAccepted?.Invoke() ?? true;
        if (!remoteDialogSeen) {
            if (_deps.RemoteControlDialog is null) {
                _logger.LogDebug("BridgeMain: remote control not accepted — skipping");
                ctx.EarlyResult = new BridgeMainResult { Error = "Remote control not accepted." };
                return;
            }

            var accepted = await _deps.RemoteControlDialog(ct).ConfigureAwait(false);
            _deps.MarkRemoteDialogSeen?.Invoke();
            if (!accepted) {
                _logger.LogDebug("BridgeMain: remote control declined by user");
                ctx.EarlyResult = new BridgeMainResult { Error = "Remote control not accepted." };
                return;
            }
        }

        var baseUrl = _deps.GetBaseUrl();
        var httpsError = ValidateHttpsUrl(baseUrl);
        if (httpsError is not null) {
            _logger.LogDebug("BridgeMain: non-HTTPS URL — skipping");
            ctx.EarlyResult = new BridgeMainResult { Error = httpsError };
            return;
        }
        ctx.BaseUrl = baseUrl;

        await next(ctx, ct).ConfigureAwait(false);
    }

    private static string? ValidateHttpsUrl(string baseUrl) {
        if (!baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
            !baseUrl.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase) &&
            !baseUrl.StartsWith("http://127.0.0.1", StringComparison.OrdinalIgnoreCase)) {
            return "Bridge requires HTTPS (or localhost).";
        }
        return null;
    }
}