namespace JoinCode.Abstractions.Entity;

/// <summary>
/// Web 请求实体 — 派生自 ToolExecutionEntity，追踪 HTTP 请求生命周期
/// 额外字段: Url, HttpStatusCode, ContentLength
/// </summary>
public sealed class WebFetchEntity : ToolExecutionEntity
{
    public string? Url { get; init; }
    public int? HttpStatusCode { get; set; }
    public long? ContentLength { get; set; }

    public WebFetchEntity(
        string? url = null,
        string? toolUseId = null,
        string? spanId = null,
        string? displayName = null,
        ObjectId sessionId = default)
        : base("web_fetch", toolUseId, spanId, displayName ?? url, sessionId)
    {
        Url = url;
    }

    /// <summary>
    /// 跨会话深拷贝 — 保留 Url/HttpStatusCode/ContentLength 等 Web 请求特有字段
    /// </summary>
    public override Entity Clone(CloneContext context)
    {
        var cloned = new WebFetchEntity(
            url: Url,
            toolUseId: ToolUseId,
            spanId: SpanId,
            displayName: DisplayName,
            sessionId: context.TargetSessionId)
        {
            HttpStatusCode = HttpStatusCode,
            ContentLength = ContentLength,
        };
        ApplyCloneState(cloned, context);
        return cloned;
    }
}
