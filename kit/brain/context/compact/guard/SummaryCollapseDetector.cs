namespace Core.Context.Compact.Guard;

/// <summary>
/// 摘要塌缩检测选项
/// </summary>
public sealed class SummaryCollapseOptions
{
    /// <summary>最小压缩比，低于此值判定为塌缩</summary>
    public double MinCompressionRatio { get; init; } = 0.02;
    /// <summary>启用压缩比检查的原始消息最小字符数</summary>
    public int MinOriginalCharsForRatioCheck { get; init; } = 2000;
    /// <summary>摘要绝对最小字符数，低于此值直接判定为塌缩</summary>
    public int AbsoluteMinSummaryChars { get; init; } = 50;
}

/// <summary>
/// 摘要塌缩检测结果
/// </summary>
public sealed class SummaryCollapseResult
{
    /// <summary>是否判定为塌缩</summary>
    public required bool IsCollapsed { get; init; }
    /// <summary>压缩比（摘要长度 / 原始长度）</summary>
    public double CompressionRatio { get; init; }
    /// <summary>诊断原因</summary>
    public string? Reason { get; init; }
}

/// <summary>
/// 摘要塌缩检测器 — 检测摘要是否过短、仅剩模板标签或压缩比过低
/// </summary>
public static class SummaryCollapseDetector
{
    private static readonly FrozenSet<string> TemplateOnlyLabels = new[] { "summary:", "摘要：", "摘要:" }
        .ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 检测摘要是否塌缩
    /// </summary>
    /// <param name="summary">待检测的摘要文本</param>
    /// <param name="originalMessageChars">原始消息总字符数</param>
    /// <param name="options">可选检测选项，null 时使用默认值</param>
    /// <returns>检测结果，包含是否塌缩、压缩比和原因</returns>
    public static SummaryCollapseResult Detect(string summary, int originalMessageChars, SummaryCollapseOptions? options = null)
    {
        options ??= new SummaryCollapseOptions();

        if (string.IsNullOrEmpty(summary))
        {
            return new SummaryCollapseResult
            {
                IsCollapsed = true,
                CompressionRatio = 0,
                Reason = "Empty summary"
            };
        }

        if (summary.Length < options.AbsoluteMinSummaryChars)
        {
            return new SummaryCollapseResult
            {
                IsCollapsed = true,
                CompressionRatio = (double)summary.Length / Math.Max(1, originalMessageChars),
                Reason = $"Summary too short ({summary.Length} chars, minimum {options.AbsoluteMinSummaryChars})"
            };
        }

        if (TemplateOnlyLabels.Contains(summary.TrimEnd()))
        {
            return new SummaryCollapseResult
            {
                IsCollapsed = true,
                CompressionRatio = (double)summary.Length / Math.Max(1, originalMessageChars),
                Reason = "Summary contains only template label"
            };
        }

        if (originalMessageChars >= options.MinOriginalCharsForRatioCheck)
        {
            var ratio = (double)summary.Length / originalMessageChars;
            if (ratio < options.MinCompressionRatio)
            {
                return new SummaryCollapseResult
                {
                    IsCollapsed = true,
                    CompressionRatio = ratio,
                    Reason = $"Compression ratio too low ({ratio:P2}, minimum {options.MinCompressionRatio:P2})"
                };
            }
        }

        return new SummaryCollapseResult
        {
            IsCollapsed = false,
            CompressionRatio = (double)summary.Length / Math.Max(1, originalMessageChars)
        };
    }
}
