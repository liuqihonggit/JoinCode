namespace Core.Context.Compact.Guard;

public sealed class SummaryFormatOptions
{
    public string[] InterventionKeywords { get; init; } = ["请用序号箭头方式", "请总结", "⚠️", "重连后仍检测到循环"];
    public string[] SelfReferenceKeywords { get; init; } = ["我会继续", "让我来", "I'll continue", "Let me"];
    public string[] TruncationMarkers { get; init; } = ["[被截断]", "[truncated]", "..."];
}

public sealed class SummaryFormatResult
{
    public required bool IsValid { get; init; }
    public required bool HasFormatError { get; init; }
    public required bool HasInterventionContamination { get; init; }
    public required bool HasSelfReference { get; init; }
    public required bool HasTruncationMarker { get; init; }
    public string? Reason { get; init; }
}

public static class SummaryFormatValidator
{
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
