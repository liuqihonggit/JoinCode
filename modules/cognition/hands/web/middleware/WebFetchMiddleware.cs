namespace Services.Web;

/// <summary>
/// HTTP获取中间件 — 执行HTTP请求并处理重定向安全策略
/// Order=400 在域名检查之后执行，包含重定向递归和egress代理检测
/// </summary>
[Register(typeof(IWebMiddleware), ServiceLifetime.Singleton)]
public sealed partial class WebFetchMiddleware : ServiceEntity, IWebMiddleware {

    /// <summary>
    /// 初始化 <see cref="WebFetchMiddleware"/> 实例。
    /// </summary>
    /// <param name="apiClient">API 客户端抽象，用于执行 HTTP 请求。</param>
    /// <param name="dnsResolver">可选的 DNS 解析函数(惰性注入仅测试使用,生产默认 Dns.GetHostAddresses)。</param>
    public WebFetchMiddleware(IApiClient apiClient, Func<string, IPAddress[]>? dnsResolver = null) {
        _apiClient = apiClient;
        _dnsResolver = dnsResolver ?? Dns.GetHostAddresses;
    }
    private const int MaxRedirects = 10;
    private const int MaxHttpContentLength = 10 * 1024 * 1024;

    private readonly IApiClient _apiClient;

    private readonly Func<string, IPAddress[]> _dnsResolver;

    /// <inheritdoc />

    /// <inheritdoc />

    /// <inheritdoc />
    public async Task InvokeAsync(WebContext context, MiddlewareDelegate<WebContext> next, CancellationToken ct) {
        WebFetchResult fetchResult;
        try {
            fetchResult = await FetchWithRedirectSafetyAsync(context.UpgradedUrl ?? throw new InvalidOperationException("UpgradedUrl is not available in WebContext."), ct).ConfigureAwait(false);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            context.Result = new WebFetchResult(false, context.Url,
                ErrorMessage: $"Failed to fetch {context.Url}: {ex.Message}");
            return;
        }
        context.FetchResult = fetchResult;

        // 重定向到不同主机 → 返回重定向结果
        if (fetchResult.RedirectUrl != null) {
            context.Result = fetchResult;
            return; // 短路
        }

        // egress代理检测
        if (fetchResult.StatusCode == 403 && fetchResult.EgressBlocked) {
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

    private async Task<WebFetchResult> FetchWithRedirectSafetyAsync(string url, CancellationToken cancellationToken, int depth = 0) {
        if (depth > MaxRedirects) {
            return new WebFetchResult(false, url, ErrorMessage: L.T(StringKey.WebRedirectLimitExceeded, MaxRedirects));
        }

        // SSRF 复检 — 每次实际请求(含重定向跟随)前重新解析宿主再检查。
        // HttpClient 内部仍会再解析一次 DNS(TOCTOU 窗口收窄到此),本层保证重定向链全跳复检(ADR 0140)。
        if (!Uri.TryCreate(url, UriKind.Absolute, out var requestUri)) {
            return new WebFetchResult(false, url, ErrorMessage: $"Invalid URL: {url}");
        }

        var guardDecision = PrivateNetworkGuard.ValidateHost(requestUri.Host, _dnsResolver);
        if (!guardDecision.Allowed) {
            var message = $"{guardDecision.Reason}\n\n" +
                          $"拦截层: SSRF guard (WebFetchMiddleware 复检)\n" +
                          "正确做法: 确认目标主机可公网解析且非内网地址后再请求，内网地址不允许通过 web 工具访问";
            return new WebFetchResult(false, url, ErrorMessage: message);
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

        if (statusCode is 301 or 302 or 307 or 308) {
            var redirectLocation = response.Headers.Location?.ToString();
            if (string.IsNullOrEmpty(redirectLocation)) {
                return new WebFetchResult(false, url, ErrorMessage: L.T(StringKey.WebRedirectMissingLocation));
            }

            var redirectUrl = Uri.TryCreate(redirectLocation, UriKind.Absolute, out var absoluteUri)
                ? absoluteUri.ToString()
                : new Uri(new Uri(url), redirectLocation).ToString();

            if (IsPermittedRedirect(url, redirectUrl)) {
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
        var charset = response.Content.Headers.ContentType?.CharSet;

        // 文本内容：按 Content-Type charset 解码（回退 UTF-8）；二进制内容：保留原始字节供持久化
        var encoding = ResolveEncoding(charset);
        var content = encoding.GetString(rawBytes);
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

    /// <summary>
    /// 解析 HTTP 响应的 charset 名称为 Encoding。不支持时回退 UTF-8（保守，不崩溃）。
    /// </summary>
    private static Encoding ResolveEncoding(string? charset) {
        if (string.IsNullOrWhiteSpace(charset))
            return Encoding.UTF8;
        try {
            return Encoding.GetEncoding(charset);
        } catch (ArgumentException) {
            System.Diagnostics.Debug.WriteLine($"[WebFetchMiddleware] 不支持的 charset '{charset}'，回退 UTF-8");
            return Encoding.UTF8;
        }
    }

    private static bool IsPermittedRedirect(string originalUrl, string redirectUrl) {
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

    private static string GetStatusText(int statusCode) => statusCode switch {
        301 => "Moved Permanently",
        302 => "Found",
        307 => "Temporary Redirect",
        308 => "Permanent Redirect",
        _ => statusCode.ToString()
    };
}