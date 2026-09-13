namespace JoinCode.Reasoning.Compression;

/// <summary>
/// Prompt Token 估算器 — 基于字符数估算 LLM prompt 的 token 消耗
/// 保守策略：中文约 1.5 字/token，英文约 4 字符/token，取除以 3.5 作为折中
/// </summary>
public static class PromptBudgetEstimator
{
    /// <summary>
    /// 每个字符对应的 token 数的倒数（保守估算）
    /// </summary>
    private const double CharsPerToken = 3.5;

    /// <summary>
    /// 估算一段或多段文本的总 token 数
    /// </summary>
    public static int Estimate(params string[] texts)
    {
        if (texts is null || texts.Length == 0) return 0;

        var totalTokens = 0;
        foreach (var text in texts)
        {
            if (string.IsNullOrEmpty(text)) continue;

            var cjkCount = 0;
            var nonCjkCount = 0;

            foreach (var c in text)
            {
                if (IsCjkCharacter(c))
                    cjkCount++;
                else
                    nonCjkCount++;
            }

            var estimatedTokens = (int)Math.Ceiling(
                cjkCount / 1.5 + nonCjkCount / CharsPerToken);

            totalTokens += Math.Max(1, estimatedTokens);
        }

        return totalTokens;
    }

    private static bool IsCjkCharacter(char c)
    {
        var code = (int)c;
        return code is >= 0x4E00 and <= 0x9FFF    // CJK Unified Ideographs
            or >= 0x3400 and <= 0x4DBF             // CJK Unified Ideographs Extension A
            or >= 0x3000 and <= 0x303F             // CJK Symbols and Punctuation
            or >= 0x3040 and <= 0x309F             // Hiragana
            or >= 0x30A0 and <= 0x30FF             // Katakana
            or >= 0xAC00 and <= 0xD7AF;            // Hangul Syllables
    }
}
