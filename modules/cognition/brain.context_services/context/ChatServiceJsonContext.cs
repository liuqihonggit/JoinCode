namespace Core.Context;

/// <summary>
/// 聊天服务 JSON 序列化上下文，覆盖 TokenUsage 与字典类型
/// </summary>
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(TokenUsage))]
[JsonSerializable(typeof(List<Dictionary<string, JsonElement>>))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
[JsonSerializable(typeof(DiagnosticLogEntryDto))]
[JsonSerializable(typeof(KeywordMissLogDto))]
[JsonSerializable(typeof(MessageOriginMetadataDto))]
public partial class ChatServiceJsonContext : JsonSerializerContext;

/// <summary>
/// 消息来源元数据 DTO — 用于 origin metadata JSON 序列化
/// </summary>
public sealed class MessageOriginMetadataDto {
    /// <summary>来源类型</summary>
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }
}
