namespace JoinCode.Transport.Bridge;

/// <summary>
/// NDJSON 活动类型 — 对齐 TS 端 SessionActivity.type
/// </summary>
public enum NdjsonActivityType
{
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
public sealed class NdjsonActivity
{
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
public sealed class NdjsonPermissionRequest
{
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
/// NDJSON 结构化解析器 — 对齐 TS 端 sessionRunner.ts 的 extractActivities + control_request 检测
/// 从子进程 stdout 的 NDJSON 行中提取活动信息和权限请求
/// </summary>
public static class NdjsonParser
{
    private const int MaxSummaryLen = 80; // 对齐 TS 端 toolSummary 截断长度

    /// <summary>
    /// 从 NDJSON 行提取活动 — 对齐 TS 端 extractActivities
    /// 只处理 type=assistant 和 type=result 两种消息
    /// </summary>
    public static List<NdjsonActivity> ExtractActivities(string ndjsonLine)
    {
        var activities = new List<NdjsonActivity>();
        if (string.IsNullOrWhiteSpace(ndjsonLine)) return activities;

        Dictionary<string, JsonElement>? json;
        try
        {
            json = JsonSerializer.Deserialize(ndjsonLine, TransportBridgeJsonContext.Default.DictionaryStringJsonElement);
        }
        catch
        {
            return activities;
        }

        if (json is null) return activities;

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        if (json.TryGetValue("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String)
        {
            var type = typeEl.GetString();
            switch (type)
            {
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
    public static NdjsonPermissionRequest? ExtractPermissionRequest(string ndjsonLine)
    {
        if (string.IsNullOrWhiteSpace(ndjsonLine)) return null;

        Dictionary<string, JsonElement>? json;
        try
        {
            json = JsonSerializer.Deserialize(ndjsonLine, TransportBridgeJsonContext.Default.DictionaryStringJsonElement);
        }
        catch
        {
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

        // request.subtype 必须是 can_use_tool
        if (!reqEl.TryGetProperty("subtype", out var subtypeEl) || subtypeEl.ValueKind != JsonValueKind.String) return null;
        if (subtypeEl.GetString() != "can_use_tool") return null;

        // 提取 tool_name
        var toolName = reqEl.TryGetProperty("tool_name", out var toolNameEl) && toolNameEl.ValueKind == JsonValueKind.String
            ? toolNameEl.GetString()! : "Unknown";

        // 提取 tool_use_id
        var toolUseId = reqEl.TryGetProperty("tool_use_id", out var toolUseIdEl) && toolUseIdEl.ValueKind == JsonValueKind.String
            ? toolUseIdEl.GetString()! : "";

        // 提取 input — 使用 JsonElement 避免 NativeAOT 不兼容
        var input = new Dictionary<string, JsonElement>();
        if (reqEl.TryGetProperty("input", out var inputEl) && inputEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in inputEl.EnumerateObject())
            {
                input[prop.Name] = prop.Value;
            }
        }

        return new NdjsonPermissionRequest
        {
            Type = "control_request",
            RequestId = requestId,
            ToolName = toolName,
            Input = input,
            ToolUseId = toolUseId,
        };
    }

    /// <summary>
    /// 工具名 → 动词映射 — 对齐 TS 端 toolSummary
    /// </summary>
    private static readonly FrozenDictionary<string, string> ToolVerbMap = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Read"] = "Reading",
        ["Write"] = "Writing",
        ["Edit"] = "Editing",
        ["MultiEdit"] = "Editing",
        ["Bash"] = "Running",
        ["Glob"] = "Searching",
        ["Grep"] = "Searching",
        ["LS"] = "Listing",
        ["WebFetch"] = "Fetching",
        ["WebSearch"] = "Searching",
        ["TodoRead"] = "Reading todos",
        ["TodoWrite"] = "Writing todos",
        ["Computer"] = "Using computer",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    /// 工具摘要生成 — 对齐 TS 端 toolSummary
    /// 将工具名映射为人类可读动词 + 从 input 中提取关键参数
    /// </summary>
    public static string ToolSummary(string toolName, Dictionary<string, JsonElement> input)
    {
        var verb = ToolVerbMap.GetValueOrDefault(toolName) ?? $"Using {toolName}";

        // 对齐 TS 端: 从 input 中提取关键参数
        var target = ExtractStringField(input, "file_path")
            ?? ExtractStringField(input, "filePath")
            ?? ExtractStringField(input, "pattern")
            ?? ExtractStringField(input, "command")
            ?? ExtractStringField(input, "url")
            ?? ExtractStringField(input, "query");

        if (target is not null)
        {
            var combined = $"{verb} {target}";
            return combined.Length > MaxSummaryLen ? combined[..MaxSummaryLen] : combined;
        }

        return verb;
    }

    /// <summary>
    /// 从 JsonElement 字典中提取字符串字段
    /// </summary>
    private static string? ExtractStringField(Dictionary<string, JsonElement> input, string fieldName)
    {
        if (input.TryGetValue(fieldName, out var el) && el.ValueKind == JsonValueKind.String)
        {
            return el.GetString();
        }
        return null;
    }

    /// <summary>
    /// 提取 assistant 消息中的活动 — 对齐 TS 端 extractActivities case 'assistant'
    /// </summary>
    private static void ExtractAssistantActivities(
        Dictionary<string, JsonElement> json, long now, List<NdjsonActivity> activities)
    {
        if (!json.TryGetValue("message", out var msgEl) || msgEl.ValueKind != JsonValueKind.Object) return;
        if (!msgEl.TryGetProperty("content", out var contentEl) || contentEl.ValueKind != JsonValueKind.Array) return;

        foreach (var block in contentEl.EnumerateArray())
        {
            if (block.ValueKind != JsonValueKind.Object) continue;

            if (block.TryGetProperty("type", out var blockTypeEl) && blockTypeEl.ValueKind == JsonValueKind.String)
            {
                var blockType = blockTypeEl.GetString();

                if (blockType == "tool_use")
                {
                    // 对齐 TS 端: tool_use → tool_start activity
                    var name = block.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String
                        ? nameEl.GetString()! : "Tool";

                    var input = new Dictionary<string, JsonElement>();
                    if (block.TryGetProperty("input", out var inputEl) && inputEl.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in inputEl.EnumerateObject())
                        {
                            input[prop.Name] = prop.Value;
                        }
                    }

                    var summary = ToolSummary(name, input);
                    activities.Add(new NdjsonActivity
                    {
                        Type = NdjsonActivityType.ToolStart,
                        Summary = summary,
                        Timestamp = now,
                    });
                }
                else if (blockType == "text")
                {
                    // 对齐 TS 端: text block → text activity
                    if (block.TryGetProperty("text", out var textEl) && textEl.ValueKind == JsonValueKind.String)
                    {
                        var text = textEl.GetString() ?? "";
                        if (text.Length > 0)
                        {
                            var summary = text.Length > MaxSummaryLen ? text[..MaxSummaryLen] : text;
                            activities.Add(new NdjsonActivity
                            {
                                Type = NdjsonActivityType.Text,
                                Summary = summary,
                                Timestamp = now,
                            });
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// 提取 result 消息中的活动 — 对齐 TS 端 extractActivities case 'result'
    /// </summary>
    private static void ExtractResultActivities(
        Dictionary<string, JsonElement> json, long now, List<NdjsonActivity> activities)
    {
        if (!json.TryGetValue("subtype", out var subtypeEl) || subtypeEl.ValueKind != JsonValueKind.String) return;

        var subtype = subtypeEl.GetString();
        if (subtype == "success")
        {
            activities.Add(new NdjsonActivity
            {
                Type = NdjsonActivityType.Result,
                Summary = "Session completed",
                Timestamp = now,
            });
        }
        else if (subtype is not null)
        {
            // 对齐 TS 端: errors?.[0] ?? `Error: ${subtype}`
            var errorSummary = "Error";
            if (json.TryGetValue("errors", out var errorsEl) && errorsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var err in errorsEl.EnumerateArray())
                {
                    if (err.ValueKind == JsonValueKind.String)
                    {
                        errorSummary = err.GetString() ?? $"Error: {subtype}";
                        break;
                    }
                }
            }

            if (errorSummary == "Error") errorSummary = $"Error: {subtype}";

            activities.Add(new NdjsonActivity
            {
                Type = NdjsonActivityType.Error,
                Summary = errorSummary,
                Timestamp = now,
            });
        }
    }
}
