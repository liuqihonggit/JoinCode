namespace Services.Web;

/// <summary>
/// 域名黑名单预检中间件 — 检查域名是否被Anthropic安全策略拦截
/// Order=300 在缓存检查之后执行，Blocked时短路管道
/// </summary>
[Register]
public sealed partial class WebDomainCheckMiddleware : IWebMiddleware
{
    [Inject] private readonly IDomainBlocklistChecker _domainBlocklistChecker;

    /// <inheritdoc />

    /// <inheritdoc />
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <inheritdoc />
    public async Task InvokeAsync(WebContext context, MiddlewareDelegate<WebContext> next, CancellationToken ct)
    {
        if (Uri.TryCreate(context.UpgradedUrl, UriKind.Absolute, out var parsed))
        {
            context.Host = parsed.Host;
            var domainCheck = await _domainBlocklistChecker.CheckAsync(parsed.Host, ct).ConfigureAwait(false);
            context.DomainCheckResult = domainCheck;

            if (domainCheck == DomainCheckResult.Blocked)
            {
                context.Result = new WebFetchResult(false, context.Url,
                    ErrorMessage: $"Domain {parsed.Host} is blocked by safety policy. This domain may contain copyrighted or harmful content.");
                return; // 短路
            }
            // CheckFailed: 不阻止，允许继续（企业内网可能无法访问api.anthropic.com）
        }

        await next(context, ct).ConfigureAwait(false);
    }
}
