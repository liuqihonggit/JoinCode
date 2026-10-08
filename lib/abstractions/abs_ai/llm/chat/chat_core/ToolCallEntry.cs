namespace JoinCode.Abstractions.LLM.Chat;

/// <summary>
/// 工具调用条目 — 统一 ToolCalls Metadata 的序列化格式
/// 对齐 ApiMessageExtensions.ExtractToolCalls 读取: [{Id, Name, Arguments}]
/// </summary>
public sealed record ToolCallEntry {
    /// <summary>工具调用 ID</summary>
    public required string? Id { get; init; }

    /// <summary>工具名称</summary>
    public required string Name { get; init; }

    /// <summary>工具调用参数 JSON</summary>
    public string Arguments { get; init; } = "{}";

    /// <summary>
    /// 将工具调用列表序列化为 Metadata["ToolCalls"] 所需的 JsonElement
    /// 手动构建 JSON 以兼容 NativeAOT（无需 JsonContext）
    /// </summary>
    public static JsonElement ToToolCallsJson(IReadOnlyList<ToolCallEntry> entries) {
        var sb = new StringBuilder();
        sb.Append('[');
        for (var i = 0; i < entries.Count; i++) {
            if (i > 0) sb.Append(',');
            var e = entries[i];
            sb.Append("{\"Id\":");
            AppendJsonString(sb, e.Id);
            sb.Append(",\"Name\":");
            AppendJsonString(sb, e.Name);
            sb.Append(",\"Arguments\":");
            AppendJsonString(sb, e.Arguments);
            sb.Append('}');
        }
        sb.Append(']');
        return JsonElementHelper.FromJson(sb.ToString());
    }

    /// <summary>
    /// AOT 安全的 JSON 字符序列化 — 手动拼引号 + 转义
    /// </summary>
    private static void AppendJsonString(StringBuilder sb, string? value) {
        if (value is null) {
            sb.Append("null");
            return;
        }
        sb.Append('"');
        sb.Append(JsonEncodedText.Encode(value).Value);
        sb.Append('"');
    }

    /// <summary>
    /// 构建包含 ToolCalls 的 Assistant 消息 Metadata 字典
    /// </summary>
    public static Dictionary<string, JsonElement> BuildAssistantMetadata(IReadOnlyList<ToolCallEntry> entries) {
        return new Dictionary<string, JsonElement> {
            [MessageMetadataKeyEnumConstants.ToolCalls] = ToToolCallsJson(entries)
        };
    }

    /// <summary>
    /// 构建 Tool 结果消息的 Metadata 字典
    /// </summary>
    public static Dictionary<string, JsonElement> BuildToolResultMetadata(string? toolCallId, string toolName) {
        return new Dictionary<string, JsonElement> {
            [MessageMetadataKeyEnumConstants.ToolCallId] = JsonElementHelper.FromString(toolCallId),
            [MessageMetadataKeyEnumConstants.ToolName] = JsonElementHelper.FromString(toolName)
        };
    }
}

/// <summary>
/// 工具调用条目反序列化 DTO — 宽松解析 ToolCallEntry.ToToolCallsJson 生成的 JSON
/// 所有字段 nullable 以容错缺失字段，对齐 TryGetProperty 的 null 兜底语义
/// </summary>
public sealed class ToolCallItemJson {
    /// <summary>工具调用 ID</summary>
    [JsonPropertyName("Id")]
    public string? Id { get; set; }

    /// <summary>工具名称</summary>
    [JsonPropertyName("Name")]
    public string? Name { get; set; }

    /// <summary>工具调用参数 JSON</summary>
    [JsonPropertyName("Arguments")]
    public string? Arguments { get; set; }
}