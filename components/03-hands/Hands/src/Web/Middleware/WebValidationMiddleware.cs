namespace Services.Web;

/// <summary>
/// URL验证中间件 — 检查URL格式合法性并升级为HTTPS
/// Order=100 确保最先执行，验证失败时短路管道
/// </summary>
[Register]
public sealed partial class WebValidationMiddleware : IWebMiddleware
{
    private const int MaxUrlLength = 2000;

    /// <inheritdoc />

    /// <inheritdoc />

    /// <summary>
    /// 创建 WebValidationMiddleware
    /// </summary>
    public WebValidationMiddleware() { }

    /// <inheritdoc />
    public Task InvokeAsync(WebContext context, MiddlewareDelegate<WebContext> next, CancellationToken ct)
    {
        if (!ValidateUrl(context.Url))
        {
            context.Result = new WebFetchResult(false, context.Url,
                ErrorMessage: L.T(StringKey.WebInvalidUrl, context.Url));
            return Task.CompletedTask; // 短路
        }

        context.UpgradedUrl = UpgradeToHttps(context.Url);
        return next(context, ct);
    }

    private static bool ValidateUrl(string url)
    {
        if (string.IsNullOrEmpty(url) || url.Length > MaxUrlLength)
            return false;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed))
            return false;

        if (parsed.Scheme is not ("http" or "https"))
            return false;

        if (!string.IsNullOrEmpty(parsed.UserInfo))
            return false;

        var hostname = parsed.Host;
        var parts = hostname.Split('.');
        return parts.Length >= 2;
    }

    private static string UpgradeToHttps(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed))
            return url;

        if (parsed.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase))
        {
            var builder = new UriBuilder(parsed) { Scheme = "https" };
            return builder.Uri.ToString();
        }

        return url;
    }
}
