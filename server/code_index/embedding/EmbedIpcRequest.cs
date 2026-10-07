namespace JoinCode.CodeIndex.Embedding;

/// <summary>
/// ONNX 嵌入 IPC 请求 — JSON 序列化用 DTO。
/// </summary>
internal sealed class EmbedIpcRequest {
    /// <summary>请求 ID。</summary>
    public int Id { get; set; }
    /// <summary>待嵌入文本列表。</summary>
    public List<string> Texts { get; set; } = [];
}

/// <summary>
/// 嵌入 IPC JSON 上下文 — AOT 源生成器。
/// </summary>
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(EmbedIpcRequest))]
internal sealed partial class EmbedIpcJsonContext : JsonSerializerContext;
