namespace Services.Web;

/// <summary>
/// 缓存检查中间件 — 查询URL缓存，命中时短路管道直接返回
/// Order=200 在验证之后执行
/// </summary>
[Register]
public sealed partial class WebCacheCheckMiddleware : IWebMiddleware
{
    [Inject] private readonly IWebFetchCache _cache;
    [Inject] private readonly ITelemetryService? _telemetryService;

    /// <inheritdoc />

    /// <inheritdoc />
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <inheritdoc />
    public Task InvokeAsync(WebContext context, MiddlewareDelegate<WebContext> next, CancellationToken ct)
    {
        // 用原始URL作键，非升级后的HTTPS URL
        var cached = _cache.TryGet(context.Url);
        if (cached != null)
        {
            RecordWebMetrics("fetch", true, cached.ContentBytes);
            context.CachedResult = new WebFetchResult(
                true, context.Url,
                Content: cached.Content,
                ContentType: cached.ContentType,
                Bytes: cached.ContentBytes,
                StatusCode: cached.StatusCode,
                StatusText: cached.StatusText,
                Truncated: cached.Truncated,
                PersistedPath: cached.PersistedPath,
                PersistedSize: cached.PersistedSize);
            context.Result = context.CachedResult;
            return Task.CompletedTask; // 短路
        }

        return next(context, ct);
    }

    private void RecordWebMetrics(string operation, bool isSuccess, int size = 0)
        => _telemetryService?.RecordCount("web.operation.count", new Dictionary<string, string> { ["operation"] = operation, ["success"] = isSuccess.ToString() }, description: "Web operation count");
}
