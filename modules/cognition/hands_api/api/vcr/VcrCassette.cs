
namespace Services.Api.Vcr;

/// <summary>
/// VCR cassette（录像带），存储一组 HTTP 交互记录
/// </summary>
public sealed class VcrCassette {
    /// <summary>
    /// cassette 名称
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 录制时间戳
    /// </summary>
    [JsonPropertyName("recorded_at")]
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 交互记录列表
    /// </summary>
    [JsonPropertyName("interactions")]
    public List<VcrInteraction> Interactions { get; set; } = new();
}

/// <summary>
/// VCR 单次交互记录（请求+响应）
/// </summary>
public sealed class VcrInteraction {
    /// <summary>
    /// 请求信息
    /// </summary>
    [JsonPropertyName("request")]
    public VcrRequest Request { get; set; } = new();

    /// <summary>
    /// 响应信息
    /// </summary>
    [JsonPropertyName("response")]
    public VcrResponse Response { get; set; } = new();

    /// <summary>
    /// 录制时间戳
    /// </summary>
    [JsonPropertyName("recorded_at")]
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// VCR 请求记录
/// </summary>
public sealed record VcrRequest {
    /// <summary>
    /// HTTP 方法
    /// </summary>
    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    /// <summary>
    /// 请求 URI
    /// </summary>
    [JsonPropertyName("uri")]
    public string Uri { get; set; } = string.Empty;

    /// <summary>
    /// 请求头字典
    /// </summary>
    [JsonPropertyName("headers")]
    public Dictionary<string, string> Headers { get; set; } = new();

    /// <summary>
    /// 请求体内容
    /// </summary>
    [JsonPropertyName("body")]
    public string? Body { get; set; }
}

/// <summary>
/// VCR 响应记录
/// </summary>
public sealed record VcrResponse {
    /// <summary>
    /// HTTP 状态码
    /// </summary>
    [JsonPropertyName("status")]
    public int Status { get; set; }

    /// <summary>
    /// HTTP 状态文本
    /// </summary>
    [JsonPropertyName("status_text")]
    public string StatusText { get; set; } = string.Empty;

    /// <summary>
    /// 响应头字典
    /// </summary>
    [JsonPropertyName("headers")]
    public Dictionary<string, string> Headers { get; set; } = new();

    /// <summary>
    /// 响应体内容
    /// </summary>
    [JsonPropertyName("body")]
    public string? Body { get; set; }

    /// <summary>
    /// 内容类型（MediaType）
    /// </summary>
    [JsonPropertyName("content_type")]
    public string? ContentType { get; set; }
}