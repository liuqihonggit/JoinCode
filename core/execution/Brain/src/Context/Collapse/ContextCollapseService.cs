
namespace Core.Context.Collapse;

[Register]
public sealed partial class ContextCollapseService : IContextCollapseService
{
    [Inject] private readonly ILogger<ContextCollapseService>? _logger;
    private readonly SemaphoreSlim _collapseLock = new(1, 1);

    public ContextCollapseService(ILogger<ContextCollapseService>? logger = null)
    {
        _logger = logger;
    }

    public async Task<ContextCollapseResult> CollapseAsync(
        string content,
        ContextCollapseOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(content);
        options ??= ContextCollapseOptions.Balanced;

        await _collapseLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var segments = await IdentifyCollapsibleSegmentsAsync(content, options, cancellationToken).ConfigureAwait(false);
            var originalTokenCount = EstimateTokenCount(content);

            if (segments.Count == 0)
            {
                _logger?.LogDebug("未发现可折叠段");
                return new ContextCollapseResult
                {
                    Collapsed = false,
                    CollapsedContent = content,
                    OriginalTokenCount = originalTokenCount,
                    CollapsedTokenCount = originalTokenCount,
                    SegmentsCollapsed = 0,
                    SegmentsPreserved = 0,
                    Strategy = options.Strategy
                };
            }

            var eligibleSegments = segments
                .Where(s => s.CollapsePriority >= options.MinCollapsePriority && s.TokenCount >= options.MinSegmentTokenCount)
                .OrderByDescending(s => s.CollapsePriority)
                .Take(options.MaxSegmentsToCollapse)
                .ToList();

            if (eligibleSegments.Count == 0)
            {
                _logger?.LogDebug("没有满足折叠条件的段");
                return new ContextCollapseResult
                {
                    Collapsed = false,
                    CollapsedContent = content,
                    OriginalTokenCount = originalTokenCount,
                    CollapsedTokenCount = originalTokenCount,
                    SegmentsCollapsed = 0,
                    SegmentsPreserved = segments.Count,
                    Strategy = options.Strategy
                };
            }

            var collapsedContent = content;
            var collapsedSegmentInfos = new List<CollapsedSegmentInfo>();

            foreach (var segment in eligibleSegments)
            {
                var summary = await GenerateSummaryAsync(segment, options, cancellationToken).ConfigureAwait(false);
                var summaryTokenCount = EstimateTokenCount(summary);

                collapsedContent = ReplaceSegment(collapsedContent, segment.Content, summary);

                var preservedRefs = options.PreserveKeyReferences
                    ? segment.KeyReferences
                    : Array.Empty<string>();

                collapsedSegmentInfos.Add(new CollapsedSegmentInfo
                {
                    SegmentId = segment.Id,
                    Type = segment.Type,
                    OriginalTokenCount = segment.TokenCount,
                    SummaryTokenCount = summaryTokenCount,
                    Summary = summary,
                    PreservedReferences = preservedRefs
                });
            }

            var collapsedTokenCount = EstimateTokenCount(collapsedContent);

            _logger?.LogInformation(
                "上下文折叠完成: 策略={Strategy}, 折叠段数={Collapsed}, 保留段数={Preserved}, 压缩比={Ratio:P}",
                options.Strategy, eligibleSegments.Count, segments.Count - eligibleSegments.Count,
                (double)collapsedTokenCount / originalTokenCount);

            return new ContextCollapseResult
            {
                Collapsed = true,
                CollapsedContent = collapsedContent,
                OriginalTokenCount = originalTokenCount,
                CollapsedTokenCount = collapsedTokenCount,
                SegmentsCollapsed = eligibleSegments.Count,
                SegmentsPreserved = segments.Count - eligibleSegments.Count,
                Strategy = options.Strategy,
                CollapsedSegments = collapsedSegmentInfos
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "上下文折叠失败");
            return new ContextCollapseResult
            {
                Collapsed = false,
                CollapsedContent = content,
                OriginalTokenCount = EstimateTokenCount(content),
                CollapsedTokenCount = EstimateTokenCount(content),
                SegmentsCollapsed = 0,
                SegmentsPreserved = 0,
                Strategy = options.Strategy,
                ErrorMessage = ex.Message
            };
        }
        finally
        {
            _collapseLock.Release();
        }
    }

    public async Task<IReadOnlyList<CollapsibleSegment>> IdentifyCollapsibleSegmentsAsync(
        string content,
        ContextCollapseOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(content);
        options ??= ContextCollapseOptions.Balanced;

        await Task.CompletedTask.ConfigureAwait(false);

        var segments = new List<CollapsibleSegment>();
        var segmentId = 0;

        var codeBlockRanges = ExtractCodeBlockRanges(content);
        foreach (var (start, end) in codeBlockRanges)
        {
            var blockContent = content[start..end];
            var tokenCount = EstimateTokenCount(blockContent);
            if (tokenCount >= options.MinSegmentTokenCount)
            {
                segments.Add(new CollapsibleSegment
                {
                    Id = $"seg_{++segmentId}",
                    Content = blockContent,
                    Type = CollapsibleSegmentType.CodeBlock,
                    StartOffset = start,
                    EndOffset = end,
                    TokenCount = tokenCount,
                    CollapsePriority = CalculateCodeBlockPriority(blockContent, tokenCount, options.Strategy),
                    KeyReferences = ExtractKeyReferences(blockContent)
                });
            }
        }

        var repetitivePatterns = DetectRepetitivePatterns(content);
        foreach (var pattern in repetitivePatterns)
        {
            var tokenCount = EstimateTokenCount(pattern.Content);
            if (tokenCount >= options.MinSegmentTokenCount)
            {
                segments.Add(new CollapsibleSegment
                {
                    Id = $"seg_{++segmentId}",
                    Content = pattern.Content,
                    Type = CollapsibleSegmentType.RepetitivePattern,
                    StartOffset = pattern.StartOffset,
                    EndOffset = pattern.EndOffset,
                    TokenCount = tokenCount,
                    CollapsePriority = CalculateRepetitivePriority(tokenCount, pattern.RepetitionCount, options.Strategy),
                    KeyReferences = Array.Empty<string>()
                });
            }
        }

        var toolOutputs = ExtractToolOutputRanges(content);
        foreach (var (start, end) in toolOutputs)
        {
            var blockContent = content[start..end];
            var tokenCount = EstimateTokenCount(blockContent);
            if (tokenCount >= options.MinSegmentTokenCount)
            {
                segments.Add(new CollapsibleSegment
                {
                    Id = $"seg_{++segmentId}",
                    Content = blockContent,
                    Type = CollapsibleSegmentType.ToolOutput,
                    StartOffset = start,
                    EndOffset = end,
                    TokenCount = tokenCount,
                    CollapsePriority = CalculateToolOutputPriority(tokenCount, options.Strategy),
                    KeyReferences = ExtractKeyReferences(blockContent)
                });
            }
        }

        return segments.OrderBy(s => s.StartOffset).ToList();
    }

    public async Task<string> GenerateSummaryAsync(
        CollapsibleSegment segment,
        ContextCollapseOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(segment);
        options ??= ContextCollapseOptions.Balanced;

        await Task.CompletedTask.ConfigureAwait(false);

        var maxLen = options.MaxSummaryLength;
        var summary = segment.Type switch
        {
            CollapsibleSegmentType.CodeBlock => GenerateCodeBlockSummary(segment, maxLen),
            CollapsibleSegmentType.RepetitivePattern => GenerateRepetitiveSummary(segment, maxLen),
            CollapsibleSegmentType.ToolOutput => GenerateToolOutputSummary(segment, maxLen),
            CollapsibleSegmentType.HistoricalDialogue => GenerateDialogueSummary(segment, maxLen),
            CollapsibleSegmentType.LongProse => GenerateProseSummary(segment, maxLen),
            CollapsibleSegmentType.SystemMessage => segment.Content.Length > maxLen
                ? segment.Content[..maxLen] + "..."
                : segment.Content,
            _ => segment.Content.Length > maxLen ? segment.Content[..maxLen] + "..." : segment.Content
        };

        return summary;
    }

    private static string GenerateCodeBlockSummary(CollapsibleSegment segment, int maxLen)
    {
        var lines = segment.Content.Split('\n');
        var firstLine = lines.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l)) ?? "";
        var refs = segment.KeyReferences.Count > 0
            ? $" | Refs: {string.Join(", ", segment.KeyReferences.Take(5))}"
            : "";
        var summary = $"[Code: {lines.Length} lines{refs}] {StringTruncator.Truncate(firstLine.Trim(), maxLen - 50)}";
        return StringTruncator.Truncate(summary, maxLen);
    }

    private static string GenerateRepetitiveSummary(CollapsibleSegment segment, int maxLen)
    {
        var lines = segment.Content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var sample = lines.FirstOrDefault() ?? "";
        var summary = $"[Repetitive pattern: {lines.Length} lines] {StringTruncator.Truncate(sample.Trim(), maxLen - 60)}";
        return StringTruncator.Truncate(summary, maxLen);
    }

    private static string GenerateToolOutputSummary(CollapsibleSegment segment, int maxLen)
    {
        var refs = segment.KeyReferences.Count > 0
            ? $" | Refs: {string.Join(", ", segment.KeyReferences.Take(3))}"
            : "";
        var summary = $"[Tool output: ~{segment.TokenCount} tokens{refs}]";
        return StringTruncator.Truncate(summary, maxLen);
    }

    private static string GenerateDialogueSummary(CollapsibleSegment segment, int maxLen)
    {
        var lines = segment.Content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var summary = $"[Historical dialogue: {lines.Length} messages]";
        return StringTruncator.Truncate(summary, maxLen);
    }

    private static string GenerateProseSummary(CollapsibleSegment segment, int maxLen)
    {
        var content = segment.Content.Replace('\n', ' ').Trim();
        if (content.Length <= maxLen)
        {
            return content;
        }

        var truncateAt = content.LastIndexOf(' ', maxLen - 3);
        if (truncateAt <= 0)
        {
            truncateAt = maxLen - 3;
        }

        return content[..truncateAt] + "...";
    }

    private static int EstimateTokenCount(string content)
    {
        return (int)Math.Ceiling(content.Length / 4.0);
    }

    private static double CalculateCodeBlockPriority(string content, int tokenCount, CollapseStrategy strategy)
    {
        var basePriority = strategy switch
        {
            CollapseStrategy.Aggressive => 0.7,
            CollapseStrategy.Conservative => 0.3,
            _ => 0.5
        };

        var lengthBonus = Math.Min(0.2, tokenCount / 5000.0);
        var hasManyLines = content.Split('\n').Length > 50 ? 0.1 : 0;

        return Math.Min(1.0, basePriority + lengthBonus + hasManyLines);
    }

    private static double CalculateRepetitivePriority(int tokenCount, int repetitionCount, CollapseStrategy strategy)
    {
        var basePriority = strategy switch
        {
            CollapseStrategy.Aggressive => 0.8,
            CollapseStrategy.Conservative => 0.4,
            _ => 0.6
        };

        var repetitionBonus = Math.Min(0.15, repetitionCount * 0.03);
        return Math.Min(1.0, basePriority + repetitionBonus);
    }

    private static double CalculateToolOutputPriority(int tokenCount, CollapseStrategy strategy)
    {
        return strategy switch
        {
            CollapseStrategy.Aggressive => 0.9,
            CollapseStrategy.Conservative => 0.5,
            _ => 0.7
        };
    }

    private static List<(int Start, int End)> ExtractCodeBlockRanges(string content)
    {
        var ranges = new List<(int, int)>();
        var index = 0;

        while (index < content.Length)
        {
            var start = content.IndexOf("```", index, StringComparison.Ordinal);
            if (start < 0) break;

            var end = content.IndexOf("```", start + 3, StringComparison.Ordinal);
            if (end < 0) break;

            ranges.Add((start, end + 3));
            index = end + 3;
        }

        return ranges;
    }

    private static List<PatternRange> DetectRepetitivePatterns(string content)
    {
        var patterns = new List<PatternRange>();
        var lines = content.Split('\n');
        var lineCounts = new Dictionary<string, List<int>>();

        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.Length < 10) continue;

            var key = trimmed.Length > 50 ? trimmed[..50] : trimmed;
            if (!lineCounts.ContainsKey(key))
            {
                lineCounts[key] = new List<int>();
            }
            lineCounts[key].Add(i);
        }

        foreach (var kvp in lineCounts.Where(k => k.Value.Count >= 3))
        {
            var firstLine = kvp.Value[0];
            var lastLine = kvp.Value[^1];
            var startOffset = GetOffsetForLine(content, firstLine);
            var endOffset = GetOffsetForLine(content, lastLine) + lines[lastLine].Length;

            if (endOffset > startOffset)
            {
                patterns.Add(new PatternRange
                {
                    Content = content[startOffset..Math.Min(endOffset, content.Length)],
                    StartOffset = startOffset,
                    EndOffset = Math.Min(endOffset, content.Length),
                    RepetitionCount = kvp.Value.Count
                });
            }
        }

        return patterns;
    }

    private static List<(int Start, int End)> ExtractToolOutputRanges(string content)
    {
        var ranges = new List<(int, int)>();
        var index = 0;

        while (index < content.Length)
        {
            var start = content.IndexOf("<tool_result>", index, StringComparison.Ordinal);
            if (start < 0)
            {
                start = content.IndexOf("<tool-output>", index, StringComparison.Ordinal);
            }
            if (start < 0) break;

            var endTag = "</tool_result>";
            var end = content.IndexOf(endTag, start, StringComparison.Ordinal);
            if (end < 0)
            {
                endTag = "</tool-output>";
                end = content.IndexOf(endTag, start, StringComparison.Ordinal);
            }
            if (end < 0) break;

            ranges.Add((start, end + endTag.Length));
            index = end + endTag.Length;
        }

        return ranges;
    }

    private static IReadOnlyList<string> ExtractKeyReferences(string content)
    {
        var refs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var patterns = new[]
        {
            @"class\s+(\w+)",
            @"interface\s+I(\w+)",
            @"(\w+)\s*\(",
            @"using\s+([\w.]+)",
            @"namespace\s+([\w.]+)"
        };

        foreach (var pattern in patterns)
        {
            var regex = new Regex(pattern);
            var matches = regex.Matches(content);
            foreach (Match match in matches)
            {
                if (match.Groups.Count > 1)
                {
                    refs.Add(match.Groups[1].Value);
                }
            }
        }

        return refs.Take(10).ToList();
    }

    private static string ReplaceSegment(string content, string segment, string replacement)
    {
        var index = content.IndexOf(segment, StringComparison.Ordinal);
        if (index < 0) return content;

        return string.Concat(content.AsSpan(0, index), replacement, content.AsSpan(index + segment.Length));
    }

    private static int GetOffsetForLine(string content, int lineIndex)
    {
        var offset = 0;
        for (var i = 0; i < lineIndex && offset < content.Length; i++)
        {
            offset = content.IndexOf('\n', offset) + 1;
            if (offset <= 0) return content.Length;
        }
        return offset;
    }

    private sealed class PatternRange
    {
        public string Content { get; init; } = string.Empty;
        public int StartOffset { get; init; }
        public int EndOffset { get; init; }
        public int RepetitionCount { get; init; }
    }
}
