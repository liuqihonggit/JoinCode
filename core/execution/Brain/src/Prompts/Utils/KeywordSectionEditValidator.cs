using System.Text.Json;
using Core.Prompts.Utils;

namespace Core.Prompts.Utils;

/// <summary>
/// 关键词配置文件编辑校验器 — 限制只能编辑 keyword-sections.json，防止后台 Agent 误改其他文件
/// 复用 SettingsEditValidator 模式：路径限制 + 格式校验 + 增量保护
/// </summary>
public static class KeywordSectionEditValidator
{
    private const string TargetFileName = "keyword-sections.json";

    /// <summary>
    /// 校验编辑操作是否合法
    /// </summary>
    /// <param name="filePath">被编辑文件路径</param>
    /// <param name="originalContent">编辑前内容</param>
    /// <param name="updatedContent">编辑后内容</param>
    /// <returns>null 表示合法，非 null 为错误消息</returns>
    public static string? ValidateEdit(string filePath, string originalContent, string updatedContent)
    {
        if (!IsKeywordSectionsPath(filePath))
            return $"关键词维护Agent只能编辑 {TargetFileName}，禁止修改其他文件";

        if (string.IsNullOrWhiteSpace(updatedContent))
            return "禁止清空 keyword-sections.json";

        try
        {
            _ = JsonSerializer.Deserialize(updatedContent, DynamicKeywordConfigJsonContext.Default.DynamicKeywordConfig);
        }
        catch (JsonException ex)
        {
            return $"keyword-sections.json 格式非法: {ex.Message}";
        }

        var afterConfig = JsonSerializer.Deserialize(updatedContent, DynamicKeywordConfigJsonContext.Default.DynamicKeywordConfig);

        var beforeConfig = JsonSerializer.Deserialize(originalContent, DynamicKeywordConfigJsonContext.Default.DynamicKeywordConfig);
        if (beforeConfig is not null && afterConfig is not null && afterConfig.Sections.Count < beforeConfig.Sections.Count)
            return "禁止删除已有 Section，只能追加关键词或新增 Section";

        return null;
    }

    /// <summary>
    /// 判断路径是否为 keyword-sections.json
    /// </summary>
    public static bool IsKeywordSectionsPath(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return false;

        var normalized = filePath.Replace('/', '\\');
        return normalized.EndsWith($"\\{TargetFileName}", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith($"/{TargetFileName}", StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(filePath).Equals(TargetFileName, StringComparison.OrdinalIgnoreCase);
    }
}
