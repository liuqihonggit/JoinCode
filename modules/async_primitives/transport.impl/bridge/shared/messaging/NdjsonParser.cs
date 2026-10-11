// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC11003
namespace JoinCode.Transport.Bridge;

/// <summary>
/// NDJSON 活动类型 — 对齐 TS 端 SessionActivity.type
/// </summary>
public enum NdjsonActivityType {
    /// <summary>工具开始执行 — 对齐 TS 端 tool_start</summary>
    [EnumValue("tool_start")] ToolStart,
    /// <summary>文本输出 — 对齐 TS 端 text</summary>
    [EnumValue("text")] Text,
    /// <summary>会话完成 — 对齐 TS 端 result</summary>
    [EnumValue("result")] Result,
    /// <summary>会话错误 — 对齐 TS 端 error</summary>
    [EnumValue("error")] Error,
}

/// <summary>
/// NDJSON 活动记录 — 对齐 TS 端 SessionActivity
/// 从子进程 stdout 的 NDJSON 行中提取的结构化活动信息
/// </summary>
public sealed class NdjsonActivity {
    /// <summary>活动类型</summary>
    public required NdjsonActivityType Type { get; init; }

    /// <summary>活动摘要 — 对齐 TS 端 summary</summary>
    public required string Summary { get; init; }

    /// <summary>时间戳</summary>
    public long Timestamp { get; init; }
}

/// <summary>
/// 权限请求 — 对齐 TS 端 PermissionRequest
/// 从子进程 stdout 的 NDJSON 行中检测到的 control_request/can_use_tool 消息
/// </summary>
public sealed class NdjsonPermissionRequest {
    /// <summary>消息类型 — 固定为 control_request</summary>
    public required string Type { get; init; }

    /// <summary>请求 ID — 对齐 TS 端 request_id</summary>
    public required string RequestId { get; init; }

    /// <summary>工具名称 — 对齐 TS 端 request.tool_name</summary>
    public required string ToolName { get; init; }

    /// <summary>工具输入 — 对齐 TS 端 request.input（使用 JsonElement 避免 NativeAOT 不兼容）</summary>
    public required Dictionary<string, JsonElement> Input { get; init; }

    /// <summary>工具使用 ID — 对齐 TS 端 request.tool_use_id</summary>
    public required string ToolUseId { get; init; }
}

/// <summary>
/// control_request.request 中 can_use_tool 的固定字段 — DTO 化提取
/// 对齐 TS 端 request 的 tool_name/tool_use_id/input
/// </summary>
public sealed class NdjsonControlRequestDto {
    /// <summary>工具名称 — 对齐 TS 端 request.tool_name</summary>
    [JsonPropertyName("tool_name")]
    public string? ToolName { get; set; }

    /// <summary>工具使用 ID — 对齐 TS 端 request.tool_use_id</summary>
    [JsonPropertyName("tool_use_id")]
    public string? ToolUseId { get; set; }

    /// <summary>工具输入 — 对齐 TS 端 request.input（JsonElement 避免 NativeAOT 不兼容）</summary>
    [JsonPropertyName("input")]
    public Dictionary<string, JsonElement> Input { get; set; } = [];
}

/// <summary>
/// assistant message 对象 — DTO 化 content 数组遍历
/// content 为 JsonElement 列表以保留 block.type discriminated union 判断
/// </summary>
public sealed class NdjsonAssistantMessageDto {
    /// <summary>内容块数组 — 对齐 TS 端 message.content（JsonElement 保留 type 判断）</summary>
    [JsonPropertyName("content")]
    public List<JsonElement> Content { get; set; } = [];
}

/// <summary>
/// tool_use block 的固定字段 — DTO 化提取
/// 对齐 TS 端 tool_use block 的 name/input
/// </summary>
public sealed class NdjsonToolUseBlockDto {
    /// <summary>工具名称 — 对齐 TS 端 block.name</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>工具输入 — 对齐 TS 端 block.input（JsonElement 避免 NativeAOT 不兼容）</summary>
    [JsonPropertyName("input")]
    public Dictionary<string, JsonElement> Input { get; set; } = [];
}

/// <summary>
/// text block 的固定字段 — DTO 化提取
/// 对齐 TS 端 text block 的 text
/// </summary>
public sealed class NdjsonTextBlockDto {
    /// <summary>文本内容 — 对齐 TS 端 block.text</summary>
    [JsonPropertyName("text")]
    public string? Text { get; set; }
}

/// <summary>
/// NDJSON 结构化解析器 — 对齐 TS 端 sessionRunner.ts 的 extractActivities + control_request 检测
/// 从子进程 stdout 的 NDJSON 行中提取活动信息和权限请求
/// </summary>
public static class NdjsonParser {
    private const int MaxSummaryLen = 80; // 对齐 TS 端 toolSummary 截断长度

    /// <summary>
    /// 从 NDJSON 行提取活动 — 对齐 TS 端 extractActivities
    /// 只处理 type=assistant 和 type=result 两种消息
    /// </summary>
    public static List<NdjsonActivity> ExtractActivities(string ndjsonLine) {
        var activities = new List<NdjsonActivity>();
        if (string.IsNullOrWhiteSpace(ndjsonLine)) return activities;

        Dictionary<string, JsonElement>? json;
        try {
            json = RelaxedJsonSerializer.Deserialize(ndjsonLine, TransportBridgeJsonContext.Default.DictionaryStringJsonElement);
        } catch {
            return activities;
        }

        if (json is null) return activities;

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        if (json.TryGetValue("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String) {
            var type = typeEl.GetString();
            switch (type) {
                case "assistant":
                ExtractAssistantActivities(json, now, activities);
                break;
                case "result":
                ExtractResultActivities(json, now, activities);
                break;
            }
        }

        return activities;
    }

    /// <summary>
    /// 从 NDJSON 行检测权限请求 — 对齐 TS 端 control_request 检测
    /// 检测 type=control_request 且 request.subtype=can_use_tool 的消息
    /// </summary>
    public static NdjsonPermissionRequest? ExtractPermissionRequest(string ndjsonLine) {
        if (string.IsNullOrWhiteSpace(ndjsonLine)) return null;

        Dictionary<string, JsonElement>? json;
        try {
            json = RelaxedJsonSerializer.Deserialize(ndjsonLine, TransportBridgeJsonContext.Default.DictionaryStringJsonElement);
        } catch {
            return null;
        }

        if (json is null) return null;

        // 必须是 control_request 类型
        if (!json.TryGetValue("type", out var typeEl) || typeEl.ValueKind != JsonValueKind.String) return null;
        if (typeEl.GetString() != "control_request") return null;

        // 必须有 request_id
        if (!json.TryGetValue("request_id", out var reqIdEl) || reqIdEl.ValueKind != JsonValueKind.String) return null;
        var requestId = reqIdEl.GetString()!;

        // 必须有 request 对象
        if (!json.TryGetValue("request", out var reqEl) || reqEl.ValueKind != JsonValueKind.Object) return null;

        // request.subtype 必须是 can_use_tool — discriminated union 判断保留
        if (!reqEl.TryGetProperty("subtype", out var subtypeEl) || subtypeEl.ValueKind != JsonValueKind.String) return null;
        if (subtypeEl.GetString() != "can_use_tool") return null;

        // 固定字段 DTO 化提取 — tool_name/tool_use_id/input
        var reqDto = DeserializeDto(reqEl, TransportBridgeJsonContext.Default.NdjsonControlRequestDto);
        var toolName = reqDto?.ToolName ?? "Unknown";
        var toolUseId = reqDto?.ToolUseId ?? "";
        var input = reqDto?.Input ?? new Dictionary<string, JsonElement>();

        return new NdjsonPermissionRequest {
            Type = "control_request",
            RequestId = requestId,
            ToolName = toolName,
            Input = input,
            ToolUseId = toolUseId,
        };
    }

    /// <summary>
    /// 工具摘要生成 — 直接用工具名 + 从 input 中提取关键参数
    /// </summary>
    public static string ToolSummary(string toolName, Dictionary<string, JsonElement> input) {
        var target = ExtractStringField(input, "file_path")
            ?? ExtractStringField(input, "filePath")
            ?? ExtractStringField(input, "pattern")
            ?? ExtractStringField(input, "command")
            ?? ExtractStringField(input, "url")
            ?? ExtractStringField(input, "query");

        if (target is not null) {
            var combined = $"{toolName} {target}";
            return combined.Length > MaxSummaryLen ? combined[..MaxSummaryLen] : combined;
        }

        return toolName;
    }

    /// <summary>
    /// 从 JsonElement 字典中提取字符串字段
    /// </summary>
    private static string? ExtractStringField(Dictionary<string, JsonElement> input, string fieldName) {
        if (input.TryGetValue(fieldName, out var el) && el.ValueKind == JsonValueKind.String) {
            return el.GetString();
        }
        return null;
    }

    /// <summary>
    /// 提取 assistant 消息中的活动 — 对齐 TS 端 extractActivities case 'assistant'
    /// </summary>
    private static void ExtractAssistantActivities(
        Dictionary<string, JsonElement> json, long now, List<NdjsonActivity> activities) {
        if (!json.TryGetValue("message", out var msgEl) || msgEl.ValueKind != JsonValueKind.Object) return;

        // content 数组 DTO 化遍历 — JsonElement 列表保留 block.type discriminated union 判断
        var msgDto = DeserializeDto(msgEl, TransportBridgeJsonContext.Default.NdjsonAssistantMessageDto);
        if (msgDto is null) return;

        foreach (var block in msgDto.Content) {
            if (block.ValueKind != JsonValueKind.Object) continue;

            // discriminated union type 判断保留
            if (!block.TryGetProperty("type", out var blockTypeEl) || blockTypeEl.ValueKind != JsonValueKind.String) continue;
            var blockType = blockTypeEl.GetString();

            if (blockType == "tool_use") {
                // 对齐 TS 端: tool_use → tool_start activity
                // 固定字段 DTO 化提取 — name/input
                var blockDto = DeserializeDto(block, TransportBridgeJsonContext.Default.NdjsonToolUseBlockDto);
                var name = blockDto?.Name ?? "Tool";
                var input = blockDto?.Input ?? new Dictionary<string, JsonElement>();

                var summary = ToolSummary(name, input);
                activities.Add(new NdjsonActivity {
                    Type = NdjsonActivityType.ToolStart,
                    Summary = summary,
                    Timestamp = now,
                });
            } else if (blockType == "text") {
                // 对齐 TS 端: text block → text activity
                // 固定字段 DTO 化提取 — text
                var blockDto = DeserializeDto(block, TransportBridgeJsonContext.Default.NdjsonTextBlockDto);
                var text = blockDto?.Text ?? "";
                if (text.Length == 0) continue;
                var summary = text.Length > MaxSummaryLen ? text[..MaxSummaryLen] : text;
                activities.Add(new NdjsonActivity {
                    Type = NdjsonActivityType.Text,
                    Summary = summary,
                    Timestamp = now,
                });
            }
        }
    }

    /// <summary>
    /// 提取 result 消息中的活动 — 对齐 TS 端 extractActivities case 'result'
    /// </summary>
    private static void ExtractResultActivities(
        Dictionary<string, JsonElement> json, long now, List<NdjsonActivity> activities) {
        if (!json.TryGetValue("subtype", out var subtypeEl) || subtypeEl.ValueKind != JsonValueKind.String) return;

        var subtype = subtypeEl.GetString();
        if (subtype == "success") {
            activities.Add(new NdjsonActivity {
                Type = NdjsonActivityType.Result,
                Summary = "Session completed",
                Timestamp = now,
            });
        } else if (subtype is not null) {
            // 对齐 TS 端: errors?.[0] ?? `Error: ${subtype}`
            var errorSummary = ExtractErrorSummary(json, subtype);

            activities.Add(new NdjsonActivity {
                Type = NdjsonActivityType.Error,
                Summary = errorSummary,
                Timestamp = now,
            });
        }
    }

    /// <summary>
    /// 提取错误摘要（提取以扁平化嵌套）
    /// </summary>
    private static string ExtractErrorSummary(Dictionary<string, JsonElement> json, string subtype) {
        var errorSummary = "Error";
        if (json.TryGetValue("errors", out var errorsEl) && errorsEl.ValueKind == JsonValueKind.Array) {
            foreach (var err in errorsEl.EnumerateArray()) {
                if (err.ValueKind != JsonValueKind.String) continue;
                errorSummary = err.GetString() ?? $"Error: {subtype}";
                break;
            }
        }
        return errorSummary == "Error" ? $"Error: {subtype}" : errorSummary;
    }

    /// <summary>
    /// 安全反序列化 JsonElement 为 DTO — 宽容处理类型不匹配（对齐原 TryGetProperty + ValueKind 检查的宽容语义）
    /// </summary>
    private static T? DeserializeDto<T>(JsonElement el, JsonTypeInfo<T> typeInfo) {
        try {
            return el.Deserialize(typeInfo);
        } catch {
            return default;
        }
    }
}