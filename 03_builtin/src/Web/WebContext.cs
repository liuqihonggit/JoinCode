namespace Services.Web;

/// <summary>
/// Web获取中间件共享上下文 — 在管道各阶段间传递状态
/// </summary>
public sealed class WebContext : PipelineContextBase, IMetricsContext
{
    // === IMetricsContext ===

    public string MetricsPrefix => "web.operation";
    public bool IsMetricsSuccess => Result?.Success ?? false;
    public long? MetricsDurationMs => null;
    public Dictionary<string, string> BuildMetricsTags() => new() { ["operation"] = "fetch" };

    // === 输入 ===

    /// <summary>
    /// 请求的原始URL
    /// </summary>
    public required string Url { get; init; }

    /// <summary>
    /// 取消令牌
    /// </summary>
    public CancellationToken CancellationToken { get; init; }

    // === WebValidationMiddleware 填充 ===

    /// <summary>
    /// HTTPS升级后的URL — 由 WebValidationMiddleware 设置
    /// </summary>
    public string? UpgradedUrl { get; set; }

    // === WebCacheCheckMiddleware 填充 ===

    /// <summary>
    /// 缓存命中结果 — 非null表示缓存命中，管道短路
    /// </summary>
    public WebFetchResult? CachedResult { get; set; }

    // === WebDomainCheckMiddleware 填充 ===

    /// <summary>
    /// 域名检查结果 — Blocked时管道短路
    /// </summary>
    public DomainCheckResult? DomainCheckResult { get; set; }

    /// <summary>
    /// 解析后的URI主机名
    /// </summary>
    public string? Host { get; set; }

    // === WebFetchMiddleware 填充 ===

    /// <summary>
    /// HTTP获取原始结果 — 包含原始内容、状态码、重定向信息等
    /// </summary>
    public WebFetchResult? FetchResult { get; set; }

    // === WebContentProcessingMiddleware 填充 ===

    /// <summary>
    /// 处理后的Markdown内容
    /// </summary>
    public string? ProcessedContent { get; set; }

    /// <summary>
    /// 内容类型
    /// </summary>
    public string? ContentType { get; set; }

    /// <summary>
    /// 内容字节数（原始响应）
    /// </summary>
    public int ContentBytes { get; set; }

    /// <summary>
    /// 是否截断
    /// </summary>
    public bool Truncated { get; set; }

    /// <summary>
    /// 二进制内容持久化路径
    /// </summary>
    public string? PersistedPath { get; set; }

    /// <summary>
    /// 二进制内容持久化大小
    /// </summary>
    public int PersistedSize { get; set; }

    // === 输出 ===

    /// <summary>
    /// 最终结果 — 由最后一个中间件或短路中间件设置
    /// </summary>
    public WebFetchResult? Result { get; set; }
}
