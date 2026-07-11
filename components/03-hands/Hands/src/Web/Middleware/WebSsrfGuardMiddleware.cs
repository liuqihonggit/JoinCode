namespace Services.Web;

/// <summary>
/// SSRF 私有网络地址检查中间件 — 阻止请求访问内网/链路本地/回环地址
/// Order=150 在 URL 验证(100)之后、缓存检查(200)之前执行
/// 对齐 Reasonix web_fetch SSRF 防护：在 DNS 解析后检查解析后的 IP 地址
/// </summary>
[Register]
public sealed partial class WebSsrfGuardMiddleware : IWebMiddleware
{
    [Inject] private readonly ILogger<WebSsrfGuardMiddleware>? _logger;

    /// <inheritdoc />

    /// <inheritdoc />
    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    /// <inheritdoc />
    public Task InvokeAsync(WebContext context, MiddlewareDelegate<WebContext> next, CancellationToken ct)
    {
        var url = context.UpgradedUrl ?? context.Url;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed))
        {
            return next(context, ct);
        }

        var host = parsed.Host;

        if (IPAddress.TryParse(host, out var directIp))
        {
            if (PrivateNetworkGuard.IsPrivateAddress(directIp))
            {
                context.Result = new WebFetchResult(false, context.Url,
                    ErrorMessage: $"SSRF protection: direct IP address {directIp} is a private/reserved address. Access to internal networks is blocked.");
                return Task.CompletedTask;
            }
        }
        else
        {
            try
            {
                var addresses = Dns.GetHostAddresses(host);
                foreach (var addr in addresses)
                {
                    if (PrivateNetworkGuard.IsPrivateAddress(addr))
                    {
                        context.Result = new WebFetchResult(false, context.Url,
                            ErrorMessage: $"SSRF protection: hostname {host} resolves to private/reserved address {addr}. Access to internal networks is blocked.");
                        return Task.CompletedTask;
                    }
                }
            }
            catch (SocketException ex)
            {
                _logger?.LogDebug(ex, "SSRF guard: DNS resolution failed for {Host}, allowing request to proceed", host);
            }
        }

        return next(context, ct);
    }
}
