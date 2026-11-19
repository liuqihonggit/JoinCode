namespace Core.Context;

/// <summary>
/// 聊天服务 JSON 序列化上下文，覆盖 TokenUsage 与字典类型
/// </summary>
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(TokenUsage))]
[JsonSerializable(typeof(List<Dictionary<string, JsonElement>>))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(DiagnosticLogEntryDto))]
[JsonSerializable(typeof(KeywordMissLogDto))]
[JsonSerializable(typeof(MessageOriginMetadataDto))]
[JsonSerializable(typeof(AgentToolCallExampleDto))]
public partial class ChatServiceJsonContext : JsonSerializerContext;

/// <summary>
/// 消息来源元数据 DTO — 用于 origin metadata JSON 序列化
/// </summary>
public sealed class MessageOriginMetadataDto {
    /// <summary>来源类型</summary>
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }
}

/// <summary>
/// Agent 工具调用示例 DTO — 模态不匹配提示中内嵌 JSON 示例的唯一数据源
/// </summary>
public sealed record AgentToolCallExampleDto {
    /// <summary>子代理描述</summary>
    [JsonPropertyName("description")]
    public required string Description { get; init; }
    /// <summary>子代理提示词占位</summary>
    [JsonPropertyName("prompt")]
    public required string Prompt { get; init; }
    /// <summary>目标模型占位</summary>
    [JsonPropertyName("model")]
    public required string Model { get; init; }
}
