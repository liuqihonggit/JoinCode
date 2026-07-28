namespace Services.Web;

/// <summary>
/// HTTP获取中间件 — 执行HTTP请求并处理重定向安全策略
/// Order=400 在域名检查之后执行，包含重定向递归和egress代理检测
/// </summary>
[Register]
public sealed partial class WebFetchMiddleware : IWebMiddleware
{
    private const int MaxRedirects = 10;
    private const int MaxHttpContentLength = 10 * 1024 * 1024;

    [Inject] private readonly IApiClient _apiClient;

    /// <inheritdoc />

    /// <inheritdoc />

    /// <inheritdoc />
    public async Task InvokeAsync(WebContext context, MiddlewareDelegate<WebContext> next, CancellationToken ct)
    {
        var fetchResult = await FetchWithRedirectSafetyAsync(context.UpgradedUrl ?? throw new InvalidOperationException("UpgradedUrl is not available in WebContext."), ct).ConfigureAwait(false);
        context.FetchResult = fetchResult;

        // 重定向到不同主机 → 返回重定向结果
        if (fetchResult.RedirectUrl != null)
        {
            context.Result = fetchResult;
            return; // 短路
        }

        // egress代理检测
        if (fetchResult.StatusCode == 403 && fetchResult.EgressBlocked)
        {
            context.Result = new WebFetchResult(false, context.Url,
                ErrorMessage: $"Access to {context.Url} is blocked by your organization's network policy (egress proxy). " +
                              "The domain is not in the allowed list. Contact your network administrator.");
            return; // 短路
        }

        // 填充内容处理所需的上下文
        context.ContentType = fetchResult.ContentType ?? "text/html";
        context.ContentBytes = fetchResult.Bytes;

        await next(context, ct).ConfigureAwait(false);
    }

    private async Task<WebFetchResult> FetchWithRedirectSafetyAsync(string url, CancellationToken cancellationToken, int depth = 0)
    {
        if (depth > MaxRedirects)
        {
            return new WebFetchResult(false, url, ErrorMessage: L.T(StringKey.WebRedirectLimitExceeded, MaxRedirects));
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd("text/markdown, text/html, */*");
        request.Headers.UserAgent.ParseAdd("JoinCode/WebFetch");

        var response = await _apiClient.SendAsync(
            ApiRequest.Get(url) with { SkipRetry = true },
            cancellationToken).ConfigureAwait(false);

        var statusCode = (int)response.StatusCode;

        // egress代理检测: 403 + X-Proxy-Error: blocked-by-allowlist
        var egressBlocked = statusCode == 403 &&
            response.Headers.Contains("X-Proxy-Error") &&
            response.Headers.GetValues("X-Proxy-Error").Any(v => v.Contains("blocked-by-allowlist", StringComparison.OrdinalIgnoreCase));

        if (statusCode is 301 or 302 or 307 or 308)
        {
            var redirectLocation = response.Headers.Location?.ToString();
            if (string.IsNullOrEmpty(redirectLocation))
            {
                return new WebFetchResult(false, url, ErrorMessage: L.T(StringKey.WebRedirectMissingLocation));
            }

            var redirectUrl = Uri.TryCreate(redirectLocation, UriKind.Absolute, out var absoluteUri)
                ? absoluteUri.ToString()
                : new Uri(new Uri(url), redirectLocation).ToString();

            if (IsPermittedRedirect(url, redirectUrl))
            {
                return await FetchWithRedirectSafetyAsync(redirectUrl, cancellationToken, depth + 1).ConfigureAwait(false);
            }

            return new WebFetchResult(
                true, url,
                StatusCode: statusCode,
                StatusText: GetStatusText(statusCode),
                RedirectUrl: redirectUrl,
                RedirectStatusCode: statusCode);
        }

        response.EnsureSuccessStatusCode();

        // 对齐TS版: 始终以 arraybuffer 获取，后续根据 Content-Type 决定处理方式
        var rawBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "text/html";

        // 文本内容：UTF-8解码；二进制内容：保留原始字节供持久化
        var content = Encoding.UTF8.GetString(rawBytes);
        var bytes = rawBytes.Length;

        return new WebFetchResult(
            true, url,
            Content: content,
            ContentType: contentType,
            Bytes: bytes,
            StatusCode: statusCode,
            StatusText: response.ReasonPhrase ?? "",
            EgressBlocked: egressBlocked,
            RawBytes: rawBytes.ToArray());
    }

    private static bool IsPermittedRedirect(string originalUrl, string redirectUrl)
    {
        if (!Uri.TryCreate(originalUrl, UriKind.Absolute, out var parsedOriginal))
            return false;
        if (!Uri.TryCreate(redirectUrl, UriKind.Absolute, out var parsedRedirect))
            return false;

        if (parsedRedirect.Scheme != parsedOriginal.Scheme)
            return false;

        if (parsedRedirect.Port != parsedOriginal.Port)
            return false;

        if (!string.IsNullOrEmpty(parsedRedirect.UserInfo))
            return false;

        var originalHost = StripWww(parsedOriginal.Host);
        var redirectHost = StripWww(parsedRedirect.Host);
        return string.Equals(originalHost, redirectHost, StringComparison.OrdinalIgnoreCase);
    }

    private static string StripWww(string hostname) =>
        hostname.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
            ? hostname[4..]
            : hostname;

    private static string GetStatusText(int statusCode) => statusCode switch
    {
        301 => "Moved Permanently",
        302 => "Found",
        307 => "Temporary Redirect",
        308 => "Permanent Redirect",
        _ => statusCode.ToString()
    };
}
