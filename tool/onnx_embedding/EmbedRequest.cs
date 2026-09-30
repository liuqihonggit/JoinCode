namespace OnnxEmbedding;

/// <summary>
/// 嵌入请求 — IPC 协议消息。
/// </summary>
internal sealed class EmbedRequest {
    /// <summary>请求 ID（用于关联响应）。</summary>
    public int Id { get; set; }
    /// <summary>待嵌入的文本列表。</summary>
    public List<string> Texts { get; set; } = [];
}
