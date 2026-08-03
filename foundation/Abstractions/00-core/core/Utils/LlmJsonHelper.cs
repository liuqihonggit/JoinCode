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
        repairHint = null;

        if (string.IsNullOrWhiteSpace(llmOutput))
            return null;

        var trimmed = llmOutput.Trim();

        var json = ExtractJsonBlock(trimmed);

        if (json is not null)
        {
            var result = TryDeserializeWithRepair(json, jsonTypeInfo, ref repairHint);
            if (result is not null)
                return result;
        }

        var inlineJson = ExtractInlineJson(trimmed);
        if (inlineJson is not null)
        {
            var result = TryDeserializeWithRepair(inlineJson, jsonTypeInfo, ref repairHint);
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

        var trimmed = llmOutput.Trim();

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
        => ToolCallRepairService.RepairJson(rawJson);

    /// <summary>
    /// 工具名归一化（大小写不敏感匹配到标准名）
    /// 统一门控入口，所有 LLM 输出的工具名修复必须通过此方法
    /// </summary>
    public static string RepairToolName(string? toolName)
        => ToolCallRepairService.RepairToolName(toolName);

    /// <summary>
    /// 参数名归一化 + 参数类型自动转换
    /// 统一门控入口，所有 LLM 输出的工具参数修复必须通过此方法
    /// </summary>
    public static ArgumentRepairResult RepairArguments(
        string toolName,
        Dictionary<string, JsonElement> arguments,
        ToolSchema? schema)
        => ToolCallRepairService.RepairArguments(toolName, arguments, schema);

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
