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
}
