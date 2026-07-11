
namespace JoinCode.ChatCommands;

/// <summary>
/// 模型名称规范化工具 — 对齐 TS 的 utils/model/model.ts firstPartyNameToCanonical + getCanonicalName
/// 将完整模型名（含日期/提供商后缀）映射为短规范名，用于成本聚合和显示
/// 匹配关键字由 CanonicalModel 枚举的 [EnumValue] 定义，ToValue() 即为匹配串
/// </summary>
public static class ModelNameHelper
{
    /// <summary>
    /// 按匹配优先级排列的规范模型枚举（更具体的关键字在前）
    /// 例如 ClaudeOpus46 必须在 ClaudeOpus4 前面，否则 "claude-opus-4-6" 会被错误匹配为 "claude-opus-4"
    /// </summary>
    private static readonly CanonicalModel[] s_models =
    [
        CanonicalModel.ClaudeOpus46,
        CanonicalModel.ClaudeOpus45,
        CanonicalModel.ClaudeOpus41,
        CanonicalModel.ClaudeOpus4,
        CanonicalModel.ClaudeSonnet46,
        CanonicalModel.ClaudeSonnet45,
        CanonicalModel.ClaudeSonnet4,
        CanonicalModel.ClaudeHaiku45,
        CanonicalModel.Claude37Sonnet,
        CanonicalModel.Claude35Sonnet,
        CanonicalModel.Claude35Haiku,
        CanonicalModel.Claude3Opus,
        CanonicalModel.Claude3Sonnet,
        CanonicalModel.Claude3Haiku,
        CanonicalModel.Gpt4oMini,
        CanonicalModel.Gpt4o,
        CanonicalModel.Gpt4Turbo,
        CanonicalModel.Gpt4,
        CanonicalModel.Gpt35Turbo,
        CanonicalModel.DeepSeek
    ];

    /// <summary>
    /// 将完整模型名映射为短规范名（对齐 TS getCanonicalName）
    /// 例如: 'claude-3-5-haiku-20241022' → 'claude-3-5-haiku'
    ///       'gpt-4o-2024-08-06' → 'gpt-4o'
    /// </summary>
    public static string GetCanonicalName(string fullModelName)
    {
        return FirstPartyNameToCanonical(fullModelName);
    }

    /// <summary>
    /// 纯字符串匹配，剥离日期/提供商后缀（对齐 TS firstPartyNameToCanonical）
    /// 输入必须是模型 ID 格式（如 'claude-3-7-sonnet-20250219'）
    /// </summary>
    internal static string FirstPartyNameToCanonical(string fullModelName)
    {
        var name = fullModelName.ToLowerInvariant();

        foreach (var model in s_models)
        {
            var keyword = model.ToValue();
            if (name.Contains(keyword, StringComparison.Ordinal))
            {
                return keyword;
            }
        }

        // Claude 正则回退 — 对齐 TS 的 /(claude-(\d+-\d+-)?\w+)/ 模式
        var claudeMatch = System.Text.RegularExpressions.Regex.Match(
            name, @"claude-(\d+-\d+-)?\w+");
        if (claudeMatch.Success)
        {
            return claudeMatch.Value;
        }

        return fullModelName;
    }
}
