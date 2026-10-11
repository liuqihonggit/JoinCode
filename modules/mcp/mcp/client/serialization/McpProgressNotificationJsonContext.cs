
namespace McpClient;

/// <summary>
/// MCP 进度通知参数 DTO — 从 NotificationProgress 通知 params 中反序列化 progressToken/progress/total/message
/// <para>progressToken 为 number|string 联合类型，用 JsonElement? 保留原值以精确判别 ValueKind</para>
/// </summary>
public sealed class McpProgressNotificationParams {
    /// <summary>进度令牌（number|string 联合类型，用于匹配请求方注册的 token）</summary>
    [JsonPropertyName("progressToken")]
    public JsonElement? ProgressToken { get; set; }

    /// <summary>当前进度值（0~total 之间）</summary>
    [JsonPropertyName("progress")]
    public double? Progress { get; set; }

    /// <summary>进度总量上限</summary>
    [JsonPropertyName("total")]
    public double? Total { get; set; }

    /// <summary>进度描述消息</summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

/// <summary>
/// McpProgressNotificationParams 的 JSON 序列化上下文 — 为 AOT 编译预生成元数据
/// </summary>
[JsonSerializable(typeof(McpProgressNotificationParams))]
[JsonSourceGenerationOptions(
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    PropertyNameCaseInsensitive = true)]
public partial class McpProgressNotificationJsonContext : JsonSerializerContext;
