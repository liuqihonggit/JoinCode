
namespace McpClient;

/// <summary>
/// MCP Channel 消息通知参数 DTO — 从 Channel 通知 params 中反序列化 content 和 meta
/// <para>meta 值类型可能混合，用 JsonElement 保留原值再按 string ValueKind 过滤 string 项</para>
/// </summary>
public sealed class McpChannelNotificationParams {
    /// <summary>消息文本内容</summary>
    [JsonPropertyName("content")]
    public string? Content { get; set; }

    /// <summary>消息元数据（值类型可能混合，反序列化后按 string ValueKind 过滤为 string 字典）</summary>
    [JsonPropertyName("meta")]
    public Dictionary<string, JsonElement> Meta { get; set; } = [];
}

/// <summary>
/// MCP Channel 权限响应通知参数 DTO — 从 Channel 权限通知 params 中反序列化 request_id 和 behavior
/// </summary>
public sealed class McpChannelPermissionNotificationParams {
    /// <summary>请求标识（对应 WaitForPermissionResponseAsync 注册的 requestId）</summary>
    [JsonPropertyName("request_id")]
    public string? RequestId { get; set; }

    /// <summary>权限行为（allow/deny 等）</summary>
    [JsonPropertyName("behavior")]
    public string? Behavior { get; set; }
}

/// <summary>
/// MCP Channel 通知参数的 JSON 序列化上下文 — 为 AOT 编译预生成元数据
/// </summary>
[JsonSerializable(typeof(McpChannelNotificationParams))]
[JsonSerializable(typeof(McpChannelPermissionNotificationParams))]
[JsonSourceGenerationOptions(
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    PropertyNameCaseInsensitive = true)]
public partial class McpChannelNotificationJsonContext : JsonSerializerContext;
