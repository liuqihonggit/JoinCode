namespace Services.Web;

/// <summary>
/// 缓存写入中间件 — 将处理后的内容写入缓存
/// Order=600 在内容处理之后执行
/// </summary>
[Register]
public sealed partial class WebCacheWriteMiddleware : IWebMiddleware
{
    [Inject] private readonly IWebFetchCache _cache;

    /// <inheritdoc />

    /// <inheritdoc />
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <inheritdoc />
    public Task InvokeAsync(WebContext context, MiddlewareDelegate<WebContext> next, CancellationToken ct)
    {
        var fetchResult = context.FetchResult!;

        // 写入缓存 — 对齐 TS 版: size 使用 Markdown 转换后的字节长度
        var markdownBytes = Encoding.UTF8.GetByteCount(context.ProcessedContent ?? string.Empty);
        var cacheEntry = new WebFetchCacheEntry(
            context.ProcessedContent ?? string.Empty,
            context.ContentType ?? "text/html",
            markdownBytes,
            fetchResult.StatusCode,
            fetchResult.StatusText ?? "",
            context.Truncated,
            PersistedPath: context.PersistedPath,
            PersistedSize: context.PersistedSize);
        _cache.Set(context.Url, cacheEntry);

        // 构建最终结果
        context.Result = new WebFetchResult(
            true, context.Url,
            Content: context.ProcessedContent,
            ContentType: context.ContentType,
            Bytes: context.ContentBytes,
            StatusCode: fetchResult.StatusCode,
            StatusText: fetchResult.StatusText,
            Truncated: context.Truncated,
            PersistedPath: context.PersistedPath,
            PersistedSize: context.PersistedSize);

        return next(context, ct);
    }
}
