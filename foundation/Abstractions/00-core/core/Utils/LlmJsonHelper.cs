namespace JoinCode.Abstractions.Utils;

/// <summary>
/// LLM 结构化输出 JSON 统一门控 — 提取 + 修复 + 宽容反序列化
/// 所有 LLM 返回的结构化 JSON 必须通过此入口反序列化，确保全局宽容处理一致
/// </summary>
public static class LlmJsonHelper
{
    /// <summary>
    /// 从 LLM 输出文本中提取 ```json ... ``` 代码块内容
    /// 支持大小写不敏感匹配（```json、```JSON、```Json 等）
    /// </summary>
    public static string? ExtractJsonBlock(string output)
    {
        if (string.IsNullOrEmpty(output))
            return null;

        var jsonStart = output.IndexOf("```json", StringComparison.OrdinalIgnoreCase);
        if (jsonStart < 0)
            return null;

        var contentStart = jsonStart + 7;
        var jsonEnd = output.IndexOf("```", contentStart, StringComparison.Ordinal);
        if (jsonEnd <= contentStart)
            return null;

        return output[contentStart..jsonEnd].Trim();
    }

    /// <summary>
    /// 从 LLM 输出中提取内联 JSON（第一个 { 到最后一个 } 之间的内容）
    /// 作为 ExtractJsonBlock 的回退策略
    /// </summary>
    public static string? ExtractInlineJson(string output)
    {
        if (string.IsNullOrEmpty(output))
            return null;

        var jsonStart = output.IndexOf('{');
        if (jsonStart < 0)
            return null;

        var jsonEnd = output.LastIndexOf('}');
        if (jsonEnd <= jsonStart)
            return null;

        return output[jsonStart..(jsonEnd + 1)];
    }

    /// <summary>
    /// 从 LLM 输出中提取内联数组 JSON（第一个 [ 到最后一个 ] 之间的内容）
    /// 用于数组类型的 LLM 输出
    /// </summary>
    public static string? ExtractArrayJson(string output)
    {
        if (string.IsNullOrEmpty(output))
            return null;

        var jsonStart = output.IndexOf('[');
        if (jsonStart < 0)
            return null;

        var jsonEnd = output.LastIndexOf(']');
        if (jsonEnd <= jsonStart)
            return null;

        return output[jsonStart..(jsonEnd + 1)];
    }

    /// <summary>
    /// LLM 结构化输出统一反序列化入口（引用类型）
    /// 内置三层宽容策略：ExtractJsonBlock → ExtractInlineJson → RepairJson
    /// 配合 JsonContext 的 AllowTrailingCommas/ReadCommentHandling/PropertyNameCaseInsensitive 实现完整宽容
    /// </summary>
    public static T? Deserialize<T>(string? llmOutput, JsonTypeInfo<T> jsonTypeInfo, out string? repairHint) where T : class
    {
        var result = DeserializeWithReport(llmOutput, jsonTypeInfo, out var report);
        repairHint = report.RepairHint ?? (report.CoercionIssues.Count > 0 ? report.FormatForLlm() : null);
        return result;
    }

    /// <summary>
    /// LLM 结构化输出统一反序列化入口（引用类型，纵深防御 + 精确报错版）
    /// 层次：第1层 严格反序列化 → 第2层 RepairJson 语法修复 → 第3层 JsonLenientCoercer 类型强制转换 → 第4层 精确报错
    /// 任一字段不可转换时降级为默认值，同时把单字段失败写进 report.CoercionIssues，供报告给 LLM 自我修正。
    /// </summary>
    public static T? DeserializeWithReport<T>(string? llmOutput, JsonTypeInfo<T> jsonTypeInfo, out JsonLeniencyReport report) where T : class
    {
        report = new JsonLeniencyReport { Deserialized = false, RepairHint = null };

        if (string.IsNullOrWhiteSpace(llmOutput))
            return null;

        var trimmed = StripBomAndTrim(llmOutput);

        var json = ExtractJsonBlock(trimmed);

        if (json is not null)
        {
            var result = TryDeserializeDefensive(json, jsonTypeInfo, ref report);
            if (result is not null)
                return result;
        }

        var inlineJson = ExtractInlineJson(trimmed);
        if (inlineJson is not null)
        {
            var result = TryDeserializeDefensive(inlineJson, jsonTypeInfo, ref report);
            if (result is not null)
                return result;
        }

        return null;
    }

    /// <summary>
    /// LLM 结构化输出统一反序列化入口（数组/值类型）
    /// 与 Deserialize 相同的宽容策略，但支持数组类型（如 GraphDefineNode[]）
    /// </summary>
    public static T? DeserializeValue<T>(string? llmOutput, JsonTypeInfo<T> jsonTypeInfo, out string? repairHint)
    {
        repairHint = null;

        if (string.IsNullOrWhiteSpace(llmOutput))
            return default;

        var trimmed = StripBomAndTrim(llmOutput);

        var json = ExtractJsonBlock(trimmed);

        if (json is not null)
        {
            var result = TryDeserializeValueWithRepair(json, jsonTypeInfo, ref repairHint);
            if (result is not null)
                return result;
        }

        var arrayJson = ExtractArrayJson(trimmed);
        if (arrayJson is not null)
        {
            var result = TryDeserializeValueWithRepair(arrayJson, jsonTypeInfo, ref repairHint);
            if (result is not null)
                return result;
        }

        return default;
    }

    /// <summary>
    /// 修复 JSON 格式问题（尾随逗号、未加引号的键、单引号、截断等）
    /// 统一门控入口，所有 LLM 输出的 JSON 修复必须通过此方法
    /// </summary>
    public static ToolCallRepairResult RepairJson(string? rawJson)
    {
        var result = ToolCallRepairService.RepairJson(rawJson);
        if (result.RepairHint is not null)
            System.Diagnostics.Trace.WriteLine($"[LlmJsonHelper] RepairJson: {result.RepairHint}");
        return result;
    }

    /// <summary>
    /// 工具名归一化（大小写不敏感匹配到标准名）
    /// 统一门控入口，所有 LLM 输出的工具名修复必须通过此方法
    /// </summary>
    public static string RepairToolName(string? toolName)
    {
        var result = ToolCallRepairService.RepairToolName(toolName);
        if (!string.IsNullOrEmpty(toolName) && !string.Equals(toolName, result, StringComparison.Ordinal))
            System.Diagnostics.Trace.WriteLine($"[LlmJsonHelper] RepairToolName: '{toolName}' -> '{result}'");
        return result;
    }

    /// <summary>
    /// 参数名归一化 + 参数类型自动转换
    /// 统一门控入口，所有 LLM 输出的工具参数修复必须通过此方法
    /// </summary>
    public static ArgumentRepairResult RepairArguments(
        string toolName,
        Dictionary<string, JsonElement> arguments,
        ToolSchema? schema)
    {
        var result = ToolCallRepairService.RepairArguments(toolName, arguments, schema);
        if (result.RepairHint is not null)
            System.Diagnostics.Trace.WriteLine($"[LlmJsonHelper] RepairArguments({toolName}): {result.RepairHint}");
        return result;
    }

    /// <summary>
    /// 带修复的重试反序列化：先直接反序列化，失败后 RepairJson 再试
    /// </summary>
    private static T? TryDeserializeWithRepair<T>(string json, JsonTypeInfo<T> jsonTypeInfo, ref string? repairHint) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize(json, jsonTypeInfo);
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Trace.WriteLine($"[LlmJsonHelper] Direct deserialize failed, will try repair: {ex.Message}");
        }

        var repairResult = ToolCallRepairService.RepairJson(json);
        if (!repairResult.Success)
            return null;

        try
        {
            var result = JsonSerializer.Deserialize(repairResult.RepairedJson, jsonTypeInfo);
            if (result is not null)
            {
                repairHint = repairResult.RepairHint;
                return result;
            }
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Trace.WriteLine($"[LlmJsonHelper] Repaired JSON deserialize still failed: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// 纵深防御反序列化：严格 → 语法修复 → 类型强制转换，失败字段降级并精确记录。
    /// 贯穿三层，任一字段的失败都被聚合进 report.CoercionIssues 供上层报告给 LLM。
    /// </summary>
    private static T? TryDeserializeDefensive<T>(string json, JsonTypeInfo<T> jsonTypeInfo, ref JsonLeniencyReport report) where T : class
    {
        var issues = new List<JsonCoercionIssue>();

        // 第1层：严格反序列化（JsonContext 已带尾随逗号/注释/大小写宽容）
        try
        {
            var direct = JsonSerializer.Deserialize(json, jsonTypeInfo);
            if (direct is not null)
            {
                report = new JsonLeniencyReport { Deserialized = true, RepairHint = report.RepairHint, CoercionIssues = issues };
                return direct;
            }
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Trace.WriteLine($"[LlmJsonHelper] Direct deserialize failed, will try repair: {ex.Message}");
        }

        // 第2层：语法修复（尾随逗号/未加引号键/单引号/截断）
        var repairResult = ToolCallRepairService.RepairJson(json);
        var repaired = repairResult.Success ? repairResult.RepairedJson : json;
        var repairHint = repairResult.RepairHint;

        try
        {
            var afterRepair = JsonSerializer.Deserialize(repaired, jsonTypeInfo);
            if (afterRepair is not null)
            {
                report = new JsonLeniencyReport { Deserialized = true, RepairHint = repairHint, CoercionIssues = issues };
                return afterRepair;
            }
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Trace.WriteLine($"[LlmJsonHelper] Repaired JSON deserialize still failed, will try type coercion: {ex.Message}");
        }

        // 第3层：类型强制转换（number↔bool、number→string、bool→string、string→number、Trim）
        try
        {
            if (JsonLenientCoercer.TryCoerceObjectJson(repaired, jsonTypeInfo, out var coercedJson, out var coercionIssues))
            {
                issues.AddRange(coercionIssues);
                var coercedResult = JsonSerializer.Deserialize(coercedJson!, jsonTypeInfo);
                if (coercedResult is not null)
                {
                    report = new JsonLeniencyReport { Deserialized = true, RepairHint = repairHint, CoercionIssues = issues };
                    return coercedResult;
                }
            }
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Trace.WriteLine($"[LlmJsonHelper] Type coercion deserialize failed: {ex.Message}");
        }

        // 第4层：精确报错 — 把失败的字段明细写入报告，供调用方回喂 LLM
        if (issues.Count == 0)
        {
            issues.Add(new JsonCoercionIssue
            {
                PropertyPath = "(root)",
                ExpectedType = typeof(T).Name,
                ActualValueKind = "Unknown",
                Reason = "严格解析、语法修复与类型转换均失败，JSON 无法映射到目标类型"
            });
        }

        report = new JsonLeniencyReport { Deserialized = false, RepairHint = repairHint, CoercionIssues = issues };
        return null;
    }

    /// <summary>
    /// 底层 IO 传输宽容：剥离 UTF-8/UTF-16 BOM 头后 Trim，避免 BOM 字符导致 JSON 解析失败。
    /// </summary>
    private static string StripBomAndTrim(string input)
    {
        var span = input.AsSpan();
        while (span.Length > 0 && (span[0] == '\uFEFF' || span[0] == '\uFFFE' || span[0] == '\u0000'))
            span = span[1..];

return span.ToString().Trim();
    }

    /// <summary>
    /// 带修复的重试反序列化（数组/值类型版本）
    /// </summary>
    private static T? TryDeserializeValueWithRepair<T>(string json, JsonTypeInfo<T> jsonTypeInfo, ref string? repairHint)
    {
        try
        {
            return JsonSerializer.Deserialize(json, jsonTypeInfo);
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Trace.WriteLine($"[LlmJsonHelper] Direct deserialize failed, will try repair: {ex.Message}");
        }

        var repairResult = ToolCallRepairService.RepairJson(json);
        if (!repairResult.Success)
            return default;

        try
        {
            var result = JsonSerializer.Deserialize(repairResult.RepairedJson, jsonTypeInfo);
            if (result is not null)
            {
                repairHint = repairResult.RepairHint;
                return result;
            }
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Trace.WriteLine($"[LlmJsonHelper] Repaired JSON deserialize still failed: {ex.Message}");
        }

        return default;
    }
}
