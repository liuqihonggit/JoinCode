namespace Core.Context.Compact.Guard;

/// <summary>
/// 摘要格式校验选项 — 定义各类污染关键词和截断标记
/// </summary>
public sealed class SummaryFormatOptions
{
    /// <summary>干预关键词 — 出现这些词说明摘要被干预指令污染</summary>
    public IEnumerable<string> InterventionKeywords { get; init; } = new[] { "请用序号箭头方式", "请总结", "⚠️", "重连后仍检测到循环" };
    /// <summary>自引用关键词 — 出现这些词说明摘要包含助手自述</summary>
    public IEnumerable<string> SelfReferenceKeywords { get; init; } = new[] { "我会继续", "让我来", "I'll continue", "Let me" };
    /// <summary>截断标记 — 出现这些标记说明摘要被截断</summary>
    public IEnumerable<string> TruncationMarkers { get; init; } = new[] { "[被截断]", "[truncated]", "..." };
}

/// <summary>
/// 摘要格式校验结果
/// </summary>
public sealed class SummaryFormatResult
{
    /// <summary>摘要是否合法（无格式错误、无干预污染、无自引用）</summary>
    public required bool IsValid { get; init; }
    /// <summary>是否存在格式错误（如未闭合标签）</summary>
    public required bool HasFormatError { get; init; }
    /// <summary>是否包含干预关键词污染</summary>
    public required bool HasInterventionContamination { get; init; }
    /// <summary>是否包含自引用关键词</summary>
    public required bool HasSelfReference { get; init; }
    /// <summary>是否包含截断标记</summary>
    public required bool HasTruncationMarker { get; init; }
    /// <summary>诊断原因</summary>
    public string? Reason { get; init; }
}

/// <summary>
/// 摘要格式校验器 — 检测未闭合标签、干预污染、自引用和截断标记
/// </summary>
public static class SummaryFormatValidator
{
    /// <summary>
    /// 校验摘要格式合法性
    /// </summary>
    /// <param name="summary">待校验的摘要文本</param>
    /// <param name="options">可选校验选项，null 时使用默认值</param>
    /// <returns>校验结果，包含各类问题标志和诊断原因</returns>
    public static SummaryFormatResult Validate(string summary, SummaryFormatOptions? options = null)
    {
        options ??= new SummaryFormatOptions();

        if (string.IsNullOrEmpty(summary))
        {
            return new SummaryFormatResult
            {
                IsValid = false,
                HasFormatError = true,
                HasInterventionContamination = false,
                HasSelfReference = false,
                HasTruncationMarker = false,
                Reason = "Empty summary"
            };
        }

        var hasFormatError = false;
        var hasIntervention = false;
        var hasSelfReference = false;
        var hasTruncation = false;
        var reasons = new List<string>(4);

        if (HasUnclosedTag(summary, "summary"))
        {
            hasFormatError = true;
            reasons.Add("Unclosed <summary> tag");
        }

        if (HasUnclosedTag(summary, "analysis"))
        {
            hasFormatError = true;
            reasons.Add("Unclosed <analysis> tag");
        }

        foreach (var keyword in options.InterventionKeywords)
        {
            if (summary.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                hasIntervention = true;
                reasons.Add($"Intervention keyword: '{keyword}'");
                break;
            }
        }

        foreach (var keyword in options.SelfReferenceKeywords)
        {
            if (summary.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                hasSelfReference = true;
                reasons.Add($"Self-reference keyword: '{keyword}'");
                break;
            }
        }

        foreach (var marker in options.TruncationMarkers)
        {
            if (summary.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                hasTruncation = true;
                reasons.Add($"Truncation marker: '{marker}'");
                break;
            }
        }

        var isValid = !hasFormatError && !hasIntervention && !hasSelfReference;

        return new SummaryFormatResult
        {
            IsValid = isValid,
            HasFormatError = hasFormatError,
            HasInterventionContamination = hasIntervention,
            HasSelfReference = hasSelfReference,
            HasTruncationMarker = hasTruncation,
            Reason = reasons.Count > 0 ? string.Join("; ", reasons) : null
        };
    }

    private static bool HasUnclosedTag(string text, string tagName)
    {
        var openTag = $"<{tagName}>";
        var closeTag = $"</{tagName}>";
        var hasOpen = text.Contains(openTag, StringComparison.OrdinalIgnoreCase);
        var hasClose = text.Contains(closeTag, StringComparison.OrdinalIgnoreCase);
        return hasOpen && !hasClose;
    }
}
