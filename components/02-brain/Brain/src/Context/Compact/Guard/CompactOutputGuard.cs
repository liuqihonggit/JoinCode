namespace Core.Context.Compact.Guard;

public enum CompactGuardFailureReason
{
    None,
    GibberishDetected,
    RepetitionDetected,
    SummaryCollapsed,
    FormatInvalid,
    InterventionContamination
}

public enum CompactFallbackLevel
{
    None = 0,
    Sanitize = 1,
    Microcompact = 2,
    Truncate = 3,
    Abort = 4
}

public sealed record CompactGuardResult
{
    public required bool IsValid { get; init; }
    public required CompactGuardFailureReason FailureReason { get; init; }
    public required CompactFallbackLevel FallbackLevel { get; init; }
    public required string SanitizedSummary { get; init; }
    public string? DiagnosticInfo { get; init; }
}

[Register]
public sealed class CompactOutputGuard
{
    private readonly ILogger<CompactOutputGuard>? _logger;

    public CompactOutputGuard(ILogger<CompactOutputGuard>? logger = null)
    {
        _logger = logger;
    }

    public CompactGuardResult Validate(string summary, int originalMessageChars)
    {
        if (string.IsNullOrEmpty(summary))
        {
            return new CompactGuardResult
            {
                IsValid = false,
                FailureReason = CompactGuardFailureReason.SummaryCollapsed,
                FallbackLevel = CompactFallbackLevel.Truncate,
                SanitizedSummary = string.Empty,
                DiagnosticInfo = "Empty summary"
            };
        }

        var gibberishResult = GibberishDetector.Detect(summary);
        if (gibberishResult.IsGibberish)
        {
            _logger?.LogWarning("CompactOutputGuard: gibberish detected - {Reason}", gibberishResult.Reason);
            return new CompactGuardResult
            {
                IsValid = false,
                FailureReason = CompactGuardFailureReason.GibberishDetected,
                FallbackLevel = CompactFallbackLevel.Microcompact,
                SanitizedSummary = summary,
                DiagnosticInfo = gibberishResult.Reason
            };
        }

        var collapseResult = SummaryCollapseDetector.Detect(summary, originalMessageChars);
        if (collapseResult.IsCollapsed)
        {
            _logger?.LogWarning("CompactOutputGuard: summary collapsed - {Reason}", collapseResult.Reason);
            return new CompactGuardResult
            {
                IsValid = false,
                FailureReason = CompactGuardFailureReason.SummaryCollapsed,
                FallbackLevel = CompactFallbackLevel.Truncate,
                SanitizedSummary = summary,
                DiagnosticInfo = collapseResult.Reason
            };
        }

        var repetitionResult = SummaryRepetitionDetector.Detect(summary);
        if (repetitionResult.IsRepetition)
        {
            _logger?.LogWarning("CompactOutputGuard: repetition detected - {Reason}", repetitionResult.Reason);
            var sanitized = DeduplicateParagraphs(summary);
            return new CompactGuardResult
            {
                IsValid = false,
                FailureReason = CompactGuardFailureReason.RepetitionDetected,
                FallbackLevel = CompactFallbackLevel.Sanitize,
                SanitizedSummary = sanitized,
                DiagnosticInfo = repetitionResult.Reason
            };
        }

        var formatResult = SummaryFormatValidator.Validate(summary);
        if (formatResult.HasInterventionContamination)
        {
            _logger?.LogWarning("CompactOutputGuard: intervention contamination detected");
            var sanitized = StripInterventionKeywords(summary);
            return new CompactGuardResult
            {
                IsValid = false,
                FailureReason = CompactGuardFailureReason.InterventionContamination,
                FallbackLevel = CompactFallbackLevel.Sanitize,
                SanitizedSummary = sanitized,
                DiagnosticInfo = formatResult.Reason
            };
        }

        if (formatResult.HasFormatError)
        {
            _logger?.LogWarning("CompactOutputGuard: format error - {Reason}", formatResult.Reason);
            return new CompactGuardResult
            {
                IsValid = false,
                FailureReason = CompactGuardFailureReason.FormatInvalid,
                FallbackLevel = CompactFallbackLevel.Sanitize,
                SanitizedSummary = summary,
                DiagnosticInfo = formatResult.Reason
            };
        }

        return new CompactGuardResult
        {
            IsValid = true,
            FailureReason = CompactGuardFailureReason.None,
            FallbackLevel = CompactFallbackLevel.None,
            SanitizedSummary = summary
        };
    }

    private static string DeduplicateParagraphs(string summary)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var line in summary.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;
            if (seen.Add(trimmed))
            {
                result.Add(line);
            }
        }
        return string.Join("\n", result);
    }

    private static string StripInterventionKeywords(string summary)
    {
        var keywords = new SummaryFormatOptions().InterventionKeywords;
        var result = summary;
        foreach (var keyword in keywords)
        {
            var idx = result.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
            while (idx >= 0)
            {
                var lineStart = result.LastIndexOf('\n', idx);
                lineStart = lineStart < 0 ? 0 : lineStart + 1;
                var lineEnd = result.IndexOf('\n', idx);
                if (lineEnd < 0) lineEnd = result.Length;
                result = result[..lineStart] + result[lineEnd..];
                idx = result.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
            }
        }
        return result.Trim();
    }
}
