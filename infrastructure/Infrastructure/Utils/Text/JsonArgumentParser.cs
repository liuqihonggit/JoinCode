namespace Infrastructure.Utils.Text;

/// <summary>
/// JSON 参数解析器 — 融合 LlmJsonHelper 宽容体系
/// <para>
/// 解析失败时调用 LlmJsonHelper.RepairJson 进行语法修复（尾随逗号/单引号/未加引号键/截断等），
/// 修复后再次尝试解析。仍失败则返回空字典（保持原 API 契约）。
/// </para>
/// </summary>
public static class JsonArgumentParser
{
    public static Dictionary<string, JsonElement> Parse(string? rawArguments)
    {
        if (string.IsNullOrEmpty(rawArguments))
            return new Dictionary<string, JsonElement>();

        // 第1层：直接解析（ContractsJsonContext 已带 AllowTrailingCommas + CommentHandling + CaseInsensitive）
        if (TryDeserialize(rawArguments, out var result))
            return result!;

        // 第2层：调用 LlmJsonHelper.RepairJson 进行语法修复
        var repairResult = LlmJsonHelper.RepairJson(rawArguments);
        if (repairResult.Success && TryDeserialize(repairResult.RepairedJson, out var repairedResult))
            return repairedResult!;

        // 第3层：尝试提取内联 JSON（第一个 { 到最后一个 }）
        var inlineJson = LlmJsonHelper.ExtractInlineJson(rawArguments);
        if (inlineJson is not null && TryDeserialize(inlineJson, out var inlineResult))
            return inlineResult!;

        // 全部失败，返回空字典（保持原 API 契约）
        return new Dictionary<string, JsonElement>();
    }

    private static bool TryDeserialize(string json, out Dictionary<string, JsonElement>? result)
    {
        try
        {
            result = JsonSerializer.Deserialize(json, ContractsJsonContext.Default.DictionaryStringJsonElement);
            return result is not null;
        }
        catch (JsonException)
        {
            result = null;
            return false;
        }
    }
}
