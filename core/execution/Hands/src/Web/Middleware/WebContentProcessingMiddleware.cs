namespace Services.Web;

/// <summary>
/// 内容处理中间件 — 二进制持久化、HTML转Markdown、内容截断
/// Order=500 在HTTP获取之后执行
/// </summary>
[Register]
public sealed partial class WebContentProcessingMiddleware : IWebMiddleware
{
    private const int MaxMarkdownLength = 100_000;

    private readonly IHtmlToMarkdownConverter _htmlToMarkdownConverter;
    private readonly IBinaryContentStorage _binaryContentStorage;
    [Inject] private readonly ILogger<WebContentProcessingMiddleware>? _logger;

    /// <inheritdoc />

    /// <inheritdoc />

    /// <summary>
    /// 创建 WebContentProcessingMiddleware
    /// </summary>
    public WebContentProcessingMiddleware(IHtmlToMarkdownConverter htmlToMarkdownConverter, IBinaryContentStorage binaryContentStorage, ILogger<WebContentProcessingMiddleware>? logger = null)
    {
        _htmlToMarkdownConverter = htmlToMarkdownConverter;
        _binaryContentStorage = binaryContentStorage;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task InvokeAsync(WebContext context, MiddlewareDelegate<WebContext> next, CancellationToken ct)
    {
        var fetchResult = context.FetchResult ?? throw new InvalidOperationException("FetchResult is not available in WebContext.");
        var html = fetchResult.Content ?? string.Empty;
        var contentType = context.ContentType ?? "text/html";
        var rawBytes = fetchResult.RawBytes;

        // 二进制内容持久化 — 对齐TS版: isBinaryContentType + persistBinaryContent
        string? persistedPath = null;
        var persistedSize = 0;
        if (rawBytes is { Length: > 0 } && BinaryContentTypeDetector.IsBinaryContentType(contentType))
        {
            var persistId = _binaryContentStorage.GeneratePersistId();
            var persistResult = await _binaryContentStorage.PersistAsync(
                rawBytes, contentType, persistId, ct).ConfigureAwait(false);
            if (persistResult.Success)
            {
                persistedPath = persistResult.FilePath;
                persistedSize = persistResult.Size;
                _logger?.LogDebug("二进制内容已持久化: {Path}, Size={Size}", persistedPath, persistedSize);
            }
        }

        var markdownContent = contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase)
            ? _htmlToMarkdownConverter.Convert(html)
            : html;

        var truncated = markdownContent.Length > MaxMarkdownLength;
        if (truncated)
        {
            markdownContent = markdownContent[..MaxMarkdownLength] + "\n\n[Content truncated due to length...]";
        }

        // 填充上下文供后续中间件使用
        context.ProcessedContent = markdownContent;
        context.Truncated = truncated;
        context.PersistedPath = persistedPath;
        context.PersistedSize = persistedSize;

        await next(context, ct).ConfigureAwait(false);
    }
}
