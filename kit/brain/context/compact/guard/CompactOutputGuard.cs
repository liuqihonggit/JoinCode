namespace Core.Context.Compact.Guard;

/// <summary>
/// 压缩输出校验失败原因枚举
/// </summary>
public enum CompactGuardFailureReason
{
    /// <summary>无失败</summary>
    [EnumValue("none")] None,
    /// <summary>检测到乱码</summary>
    [EnumValue("gibberish_detected")] GibberishDetected,
    /// <summary>检测到重复内容</summary>
    [EnumValue("repetition_detected")] RepetitionDetected,
    /// <summary>摘要塌缩（过短或仅剩模板）</summary>
    [EnumValue("summary_collapsed")] SummaryCollapsed,
    /// <summary>格式错误（如未闭合标签）</summary>
    [EnumValue("format_invalid")] FormatInvalid,
    /// <summary>干预关键词污染</summary>
    [EnumValue("intervention_contamination")] InterventionContamination
}

/// <summary>
/// 压缩失败后采用的兜底级别
/// </summary>
public enum CompactFallbackLevel
{
    /// <summary>无需兜底</summary>
    [EnumValue("none")] None = 0,
    /// <summary>清洗摘要（去重/去污染）</summary>
    [EnumValue("sanitize")] Sanitize = 1,
    /// <summary>降级为微压缩</summary>
    [EnumValue("microcompact")] Microcompact = 2,
    /// <summary>截断处理</summary>
    [EnumValue("truncate")] Truncate = 3,
    /// <summary>中止压缩</summary>
    [EnumValue("abort")] Abort = 4
}

/// <summary>
/// 压缩输出校验结果
/// </summary>
public sealed record CompactGuardResult
{
    /// <summary>摘要是否通过校验</summary>
    public required bool IsValid { get; init; }
    /// <summary>失败原因</summary>
    public required CompactGuardFailureReason FailureReason { get; init; }
    /// <summary>建议的兜底级别</summary>
    public required CompactFallbackLevel FallbackLevel { get; init; }
    /// <summary>清洗后的摘要文本</summary>
    public required string SanitizedSummary { get; init; }
    /// <summary>诊断信息（如失败原因详情）</summary>
    public string? DiagnosticInfo { get; init; }
}

/// <summary>
/// 压缩输出守卫 — 对 LLM 生成的摘要进行质量校验，检测乱码、重复、塌缩、格式错误和干预污染
/// </summary>
[Register(typeof(CompactOutputGuard), ServiceLifetime.Singleton)]
public sealed class CompactOutputGuard : ServiceEntity
{
    private readonly ILogger<CompactOutputGuard>? _logger;

    /// <summary>
    /// 初始化 <see cref="CompactOutputGuard"/> 实例
    /// </summary>
    /// <param name="logger">可选日志记录器</param>
    public CompactOutputGuard(ILogger<CompactOutputGuard>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// 校验摘要质量，依次检测乱码、塌缩、重复、格式错误和干预污染
    /// </summary>
    /// <param name="summary">待校验的摘要文本</param>
    /// <param name="originalMessageChars">原始消息总字符数，用于压缩比判断</param>
    /// <returns>校验结果，包含合法性、失败原因、兜底级别和清洗后的摘要</returns>
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
