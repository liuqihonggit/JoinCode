namespace JoinCode.Abstractions.Entity;

/// <summary>
/// Web 请求实体 — 派生自 ToolExecutionEntity，追踪 HTTP 请求生命周期
/// 额外字段: Url, HttpStatusCode, ContentLength
/// </summary>
public sealed class WebFetchEntity : ToolExecutionEntity {
    /// <summary>获取或设置请求 URL。</summary>
    public string? Url { get; init; }
    /// <summary>获取或设置 HTTP 状态码。</summary>
    public int? HttpStatusCode { get; set; }
    /// <summary>获取或设置内容长度。</summary>
    public long? ContentLength { get; set; }

    /// <summary>构造 Web 请求实体。</summary>
    public WebFetchEntity(
        string? url = null,
        string? toolUseId = null,
        string? spanId = null,
        string? displayName = null,
        ObjectId sessionId = default)
        : base(WebToolName.WebFetch.ToValue(), toolUseId, spanId, displayName ?? url, sessionId) {
        Url = url;
    }

    /// <summary>
    /// 跨会话深拷贝 — 保留 Url/HttpStatusCode/ContentLength 等 Web 请求特有字段
    /// </summary>
    public override Entity Clone(CloneContext context) {
        var cloned = new WebFetchEntity(
            url: Url,
            toolUseId: ToolUseId,
            spanId: SpanId,
            displayName: DisplayName,
            sessionId: context.TargetSessionId) {
            HttpStatusCode = HttpStatusCode,
            ContentLength = ContentLength,
        };
        ApplyCloneState(cloned, context);
        return cloned;
    }
}