using System.Text.RegularExpressions;

namespace NativeTui.Core.Repl;

/// <summary>
/// ultrathink 触发词高亮 — 对齐基准 utils/thinking.ts（findThinkingTriggerPositions / getRainbowColor）：
/// 触发词 `/\bultrathink\b/gi`（单词边界 + 大小写不敏感），命中区每个字符按 charIndex % 7 循环
/// 取 rainbow 7 色（rainbow_red → violet）。基准由 isUltrathinkEnabled() 门控（build flag +
/// GrowthBook `tengu_turtle_carbon` 默认 true），官方构建默认启用 — C# 对齐默认行为不加 gate。
/// 零依赖纯函数（高亮位置/色索引计算），渲染分段由 MessageRenderer.RenderUser 消费。
/// </summary>
public static partial class UltrathinkHighlighter
{
    /// <summary>彩虹色数（基准 RAINBOW_COLORS 7 色循环）。</summary>
    public const int RainbowColorCount = 7;

    /// <summary>触发词匹配（编译期 regex，大小写不敏感 + 单词边界）。</summary>
    [GeneratedRegex(@"\bultrathink\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TriggerRegex();

    /// <summary>查找触发词位置（start inclusive / end exclusive，对齐基准 match.index/+length）。</summary>
    public static IReadOnlyList<(int Start, int End)> FindTriggerPositions(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var result = new List<(int, int)>();
        foreach (Match match in TriggerRegex().Matches(text))
        {
            result.Add((match.Index, match.Index + match.Length));
        }
        return result;
    }

    /// <summary>命中区内第 charIndex 个字符的彩虹色索引（基准 charIndex % colors.length）。</summary>
    public static int RainbowColorIndex(int charIndex) =>
        charIndex >= 0 ? charIndex % RainbowColorCount : 0;
}
