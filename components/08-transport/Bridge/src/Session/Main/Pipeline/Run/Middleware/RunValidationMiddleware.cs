namespace Core.Bridge;

[Register(typeof(IBridgeRunMiddleware))]
public sealed partial class RunValidationMiddleware : IBridgeRunMiddleware
{
    [Inject] private readonly BridgeMainDeps _deps;
    [Inject] private readonly ILogger<RunValidationMiddleware> _logger;

    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    public async Task InvokeAsync(BridgeRunContext ctx, MiddlewareDelegate<BridgeRunContext> next, CancellationToken ct)
    {
        if (ctx.Args.Help)
        {
            ctx.EarlyResult = new BridgeMainResult { HelpText = BridgeMainArgsParser.GetHelpText() };
            return;
        }

        if (ctx.Args.HasError)
        {
            ctx.EarlyResult = new BridgeMainResult { Error = ctx.Args.Error };
            return;
        }

        if (_deps.PermissionMode is not null)
        {
            var validModes = new[] { "default", "plan", "auto-accept", "bubble" };
            if (!validModes.Contains(_deps.PermissionMode, StringComparer.OrdinalIgnoreCase))
            {
                ctx.EarlyResult = new BridgeMainResult { Error = $"Invalid permission mode '{_deps.PermissionMode}'. Valid modes: {string.Join(", ", validModes)}" };
                return;
            }
        }

        var accessToken = _deps.GetAccessToken();
        if (string.IsNullOrEmpty(accessToken))
        {
            _logger.LogDebug("BridgeMain: no access token — skipping");
            ctx.EarlyResult = new BridgeMainResult { Error = "No access token available. Please login first." };
            return;
        }
        ctx.AccessToken = accessToken;

        var remoteDialogSeen = _deps.CheckRemoteDialogAccepted?.Invoke() ?? true;
        if (!remoteDialogSeen)
        {
            if (_deps.RemoteControlDialog is not null)
            {
                var accepted = await _deps.RemoteControlDialog(ct).ConfigureAwait(false);
                _deps.MarkRemoteDialogSeen?.Invoke();
                if (!accepted)
                {
                    _logger.LogDebug("BridgeMain: remote control declined by user");
                    ctx.EarlyResult = new BridgeMainResult { Error = "Remote control not accepted." };
                    return;
                }
            }
            else
            {
                _logger.LogDebug("BridgeMain: remote control not accepted — skipping");
                ctx.EarlyResult = new BridgeMainResult { Error = "Remote control not accepted." };
                return;
            }
        }

        var baseUrl = _deps.GetBaseUrl();
        var httpsError = ValidateHttpsUrl(baseUrl);
        if (httpsError is not null)
        {
            _logger.LogDebug("BridgeMain: non-HTTPS URL — skipping");
            ctx.EarlyResult = new BridgeMainResult { Error = httpsError };
            return;
        }
        ctx.BaseUrl = baseUrl;

        await next(ctx, ct).ConfigureAwait(false);
    }

    private static string? ValidateHttpsUrl(string baseUrl)
    {
        if (!baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
            !baseUrl.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase) &&
            !baseUrl.StartsWith("http://127.0.0.1", StringComparison.OrdinalIgnoreCase))
        {
            return "Bridge requires HTTPS (or localhost).";
        }
        return null;
    }
}
