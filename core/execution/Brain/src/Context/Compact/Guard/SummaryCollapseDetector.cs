namespace Core.Context.Compact.Guard;

public sealed class SummaryCollapseOptions
{
    public double MinCompressionRatio { get; init; } = 0.02;
    public int MinOriginalCharsForRatioCheck { get; init; } = 2000;
    public int AbsoluteMinSummaryChars { get; init; } = 50;
}

public sealed class SummaryCollapseResult
{
    public required bool IsCollapsed { get; init; }
    public double CompressionRatio { get; init; }
    public string? Reason { get; init; }
}

public static class SummaryCollapseDetector
{
    private static readonly FrozenSet<string> TemplateOnlyLabels = new[] { "summary:", "摘要：", "摘要:" }
        .ToFrozenSet(StringComparer.OrdinalIgnoreCase);

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
