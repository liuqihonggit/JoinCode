namespace Infrastructure.IO.Services.FileOps;

/// <summary>
/// oldString 匹配失败的分类原因。
/// </summary>
public enum EditMismatchReason
{
    /// <summary>oldString 在文件中完全不存在（连首行都找不到）。</summary>
    StringNotFound,

    /// <summary>oldString 首行在文件中存在，但后续行从某行开始分叉。</summary>
    PartialMatch,

    /// <summary>去除所有空白字符后能匹配，说明是空格/制表符/缩进差异。</summary>
    WhitespaceMismatch,

    /// <summary>找到高相似度片段（Jaccard 相似度 > 0.3），但非精确匹配。</summary>
    SimilarFound,
}

/// <summary>
/// 文件编辑匹配失败的纵深诊断结果。
/// </summary>
public sealed record EditDiagnostic
{
    /// <summary>失败原因分类。</summary>
    public required EditMismatchReason Reason { get; init; }

    /// <summary>oldString 首行在文件中匹配到的行号（1-based），未找到为 null。</summary>
    public int? MatchedLine { get; init; }

    /// <summary>从 MatchedLine 开始连续匹配的行数。</summary>
    public int? MatchedLineCount { get; init; }

    /// <summary>分叉发生的文件行号（1-based）。</summary>
    public int? DivergeLine { get; init; }

    /// <summary>分叉处文件该行的内容。</summary>
    public string? FileLineAtDiverge { get; init; }

    /// <summary>分叉处 oldString 该行的内容。</summary>
    public string? OldStringLineAtDiverge { get; init; }

    /// <summary>最相似片段起始行号（1-based）。</summary>
    public int? SimilarStartLine { get; init; }

    /// <summary>最相似片段结束行号（1-based）。</summary>
    public int? SimilarEndLine { get; init; }

    /// <summary>最相似片段的文本内容。</summary>
    public string? SimilarSnippet { get; init; }

    /// <summary>最相似片段的 Jaccard 相似度（0.0-1.0）。</summary>
    public double? SimilarityScore { get; init; }

    /// <summary>格式化后的完整诊断消息（可直接拼接到错误信息中）。</summary>
    public required string FormattedMessage { get; init; }
}

/// <summary>
/// 当 oldString 在文件内容中找不到时，生成增强诊断信息。
/// 对齐 FileSuggestionHelper 的模式 — 为匹配失败提供可操作的定位信息。
/// </summary>
public static class EditDiagnosticBuilder
{
    /// <summary>超过此行数的文件跳过相似度计算（性能保护）。</summary>
    private const int MaxFileLinesForSimilarity = 5000;

    /// <summary>超过此行数的 oldString 跳过行级 diff（性能保护）。</summary>
    private const int MaxOldStringLinesForDiff = 100;

    /// <summary>相似度阈值，超过此值才报告 SimilarFound。</summary>
    private const double SimilarityThreshold = 0.3;

    /// <summary>
    /// 分析 oldString 在 fileContent 中找不到的原因，生成诊断信息。
    /// 调用前提：fileContent 不包含 oldString（已经过 FindActualString + Desanitize 失败）。
    /// </summary>
    /// <param name="fileContent">文件内容（已归一化 CRLF → LF）。</param>
    /// <param name="oldString">待匹配字符串（已归一化 CRLF → LF）。</param>
    /// <returns>诊断结果，包含失败原因和定位信息。</returns>
    public static EditDiagnostic BuildDiagnostic(string fileContent, string oldString)
    {
        var fileLines = SplitLines(fileContent);
        var oldLines = SplitLines(oldString);

        // 策略1: 空白差异检测 — 去除所有空白后能匹配
        if (TryDetectWhitespaceMismatch(fileContent, oldString))
        {
            return new EditDiagnostic
            {
                Reason = EditMismatchReason.WhitespaceMismatch,
                FormattedMessage = BuildWhitespaceMismatchMessage(oldLines),
            };
        }

        // 策略2: 部分匹配定位 — 首行在文件中存在，后续行分叉
        var partialMatch = TryDetectPartialMatch(fileLines, oldLines);
        if (partialMatch is not null)
        {
            return partialMatch;
        }

        // 策略3: 最相似片段 — 滑动窗口 Jaccard 相似度
        if (fileLines.Length <= MaxFileLinesForSimilarity && oldLines.Length > 0)
        {
            var similar = TryFindSimilarSnippet(fileLines, oldLines);
            if (similar is not null)
            {
                return similar;
            }
        }

        // 兜底: 完全找不到
        return new EditDiagnostic
        {
            Reason = EditMismatchReason.StringNotFound,
            FormattedMessage = BuildStringNotFoundMessage(oldLines),
        };
    }

    /// <summary>
    /// 将文本按 \n 分割为行数组（不保留行尾符）。
    /// </summary>
    private static string[] SplitLines(string text)
    {
        if (string.IsNullOrEmpty(text))
            return [];

        // 优化：大多数情况不需要分配 List
        var lines = new List<string>(capacity: Math.Min(text.Length / 32 + 1, 256));
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                lines.Add(text.Substring(start, i - start));
                start = i + 1;
            }
        }

        // 末尾无换行符的最后一行
        if (start < text.Length)
        {
            lines.Add(text.Substring(start, text.Length - start));
        }

        return lines.ToArray();
    }

    /// <summary>
    /// 空白差异检测：去除所有空白字符后，fileContent 是否包含 oldString。
    /// </summary>
    private static bool TryDetectWhitespaceMismatch(string fileContent, string oldString)
    {
        // 快速检查：如果去空白后长度差距太大，跳过（避免无谓的分配）
        var oldNonSpaceLength = CountNonWhitespace(oldString.AsSpan());
        if (oldNonSpaceLength == 0)
            return false;

        var strippedFile = StripWhitespace(fileContent);
        var strippedOld = StripWhitespace(oldString);

        return strippedFile.Contains(strippedOld, StringComparison.Ordinal);
    }

    private static int CountNonWhitespace(ReadOnlySpan<char> span)
    {
        var count = 0;
        foreach (var c in span)
        {
            if (!char.IsWhiteSpace(c))
                count++;
        }
        return count;
    }

    private static string StripWhitespace(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (!char.IsWhiteSpace(c))
                sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// 部分匹配检测：取 oldString 首行，在文件中搜索精确匹配，
    /// 找到后逐行比较，报告连续匹配行数和分叉位置。
    /// </summary>
    private static EditDiagnostic? TryDetectPartialMatch(string[] fileLines, string[] oldLines)
    {
        if (oldLines.Length == 0)
            return null;

        var firstOldLine = oldLines[0];

        // 在文件中搜索首行精确匹配
        for (var fileIdx = 0; fileIdx < fileLines.Length; fileIdx++)
        {
            if (!fileLines[fileIdx].Equals(firstOldLine, StringComparison.Ordinal))
                continue;

            // 首行匹配，逐行比较后续行
            var matchedCount = 1;
            var divergeIdx = -1;

            for (var i = 1; i < oldLines.Length && fileIdx + i < fileLines.Length; i++)
            {
                if (fileLines[fileIdx + i].Equals(oldLines[i], StringComparison.Ordinal))
                {
                    matchedCount++;
                }
                else
                {
                    divergeIdx = i;
                    break;
                }
            }

            // 如果匹配了至少 1 行但未全部匹配，报告部分匹配
            if (matchedCount >= 1 && matchedCount < oldLines.Length)
            {
                var divergeFileLine = fileIdx + matchedCount + 1; // 1-based
                var fileLineAtDiverge = fileIdx + matchedCount < fileLines.Length
                    ? fileLines[fileIdx + matchedCount]
                    : null;
                var oldLineAtDiverge = matchedCount < oldLines.Length
                    ? oldLines[matchedCount]
                    : null;

                return new EditDiagnostic
                {
                    Reason = EditMismatchReason.PartialMatch,
                    MatchedLine = fileIdx + 1, // 1-based
                    MatchedLineCount = matchedCount,
                    DivergeLine = divergeFileLine,
                    FileLineAtDiverge = fileLineAtDiverge,
                    OldStringLineAtDiverge = oldLineAtDiverge,
                    FormattedMessage = BuildPartialMatchMessage(
                        fileIdx + 1, matchedCount, oldLines.Length,
                        divergeFileLine, fileLineAtDiverge, oldLineAtDiverge),
                };
            }

            // 如果全部匹配了，说明 oldString 实际存在于文件中（不应该发生，但防御性处理）
            // 继续搜索下一个首行匹配位置
        }

        return null;
    }

    /// <summary>
    /// 最相似片段检测：滑动窗口 + 行级 Jaccard 相似度。
    /// 窗口大小 = oldString 行数，在文件上滑动，找相似度最高的窗口。
    /// </summary>
    private static EditDiagnostic? TryFindSimilarSnippet(string[] fileLines, string[] oldLines)
    {
        if (oldLines.Length == 0 || fileLines.Length < oldLines.Length)
            return null;

        var windowSize = oldLines.Length;
        var bestScore = 0.0;
        var bestStartIdx = -1;

        // 构建 oldLines 的行集合（用于 Jaccard 交集计算）
        var oldLineSet = new HashSet<string>(oldLines, StringComparer.Ordinal);

        for (var start = 0; start <= fileLines.Length - windowSize; start++)
        {
            var score = ComputeJaccardSimilarity(fileLines, start, windowSize, oldLineSet, oldLines.Length);
            if (score > bestScore)
            {
                bestScore = score;
                bestStartIdx = start;
            }
        }

        if (bestStartIdx < 0 || bestScore < SimilarityThreshold)
            return null;

        // 提取最相似片段（含上下文行）
        var snippetStart = Math.Max(0, bestStartIdx - SimilarSnippetContextLines);
        var snippetEnd = Math.Min(fileLines.Length - 1, bestStartIdx + windowSize - 1 + SimilarSnippetContextLines);
        var snippetLines = new string[snippetEnd - snippetStart + 1];
        Array.Copy(fileLines, snippetStart, snippetLines, 0, snippetLines.Length);
        var snippet = string.Join("\n", snippetLines);

        return new EditDiagnostic
        {
            Reason = EditMismatchReason.SimilarFound,
            SimilarStartLine = bestStartIdx + 1, // 1-based
            SimilarEndLine = bestStartIdx + windowSize,
            SimilarSnippet = snippet,
            SimilarityScore = bestScore,
            FormattedMessage = BuildSimilarFoundMessage(
                bestStartIdx + 1, bestStartIdx + windowSize, bestScore, snippet),
        };
    }

    private const int SimilarSnippetContextLines = 3;

    /// <summary>
    /// 计算 fileLines[start..start+windowSize] 与 oldLines 的 Jaccard 相似度。
    /// Jaccard = |交集| / |并集|
    /// </summary>
    private static double ComputeJaccardSimilarity(
        string[] fileLines, int start, int windowSize,
        HashSet<string> oldLineSet, int oldLineCount)
    {
        var intersection = 0;
        for (var i = 0; i < windowSize; i++)
        {
            if (oldLineSet.Contains(fileLines[start + i]))
                intersection++;
        }

        // 并集 = oldLineCount + windowSize - intersection
        var union = oldLineCount + windowSize - intersection;
        if (union == 0)
            return 0.0;

        return (double)intersection / union;
    }

    // ── 消息格式化 ──

    private static string BuildStringNotFoundMessage(string[] oldLines)
    {
        var sb = new StringBuilder(256);
        sb.Append("String to replace not found in file.");
        sb.Append("\n[诊断] 失败原因: StringNotFound");
        sb.Append($"\noldString 共 {oldLines.Length} 行，首行在文件中未找到任何匹配。");
        sb.Append("\n提示: 检查拼写、大小写、编码(BOM/UTF-16)，或先 Read 文件确认当前内容。");
        return sb.ToString();
    }

    private static string BuildPartialMatchMessage(
        int matchedLine, int matchedCount, int totalOldLines,
        int divergeLine, string? fileLineAtDiverge, string? oldLineAtDiverge)
    {
        var sb = new StringBuilder(512);
        sb.Append("String to replace not found in file.");
        sb.Append("\n[诊断] 失败原因: PartialMatch");
        sb.Append($"\noldString 首行在文件第 {matchedLine} 行找到，前 {matchedCount}/{totalOldLines} 行匹配，从第 {divergeLine} 行开始分叉:");

        if (fileLineAtDiverge is not null)
        {
            sb.Append($"\n  文件第 {divergeLine} 行: {TruncateForDisplay(fileLineAtDiverge)}");
        }
        if (oldLineAtDiverge is not null)
        {
            sb.Append($"\n  oldString 第 {matchedCount + 1} 行: {TruncateForDisplay(oldLineAtDiverge)}");
        }

        sb.Append("\n提示: 检查分叉处的空白/缩进/大小写差异，或扩大上下文范围使匹配唯一。");
        return sb.ToString();
    }

    private static string BuildWhitespaceMismatchMessage(string[] oldLines)
    {
        var sb = new StringBuilder(256);
        sb.Append("String to replace not found in file.");
        sb.Append("\n[诊断] 失败原因: WhitespaceMismatch");
        sb.Append($"\noldString 去除所有空白字符后能在文件中找到匹配。");
        sb.Append("\n说明: 内容相同但空白字符（空格/制表符/缩进/空行）不同。");
        sb.Append("\n提示: 先 Read 文件查看实际缩进（空格 vs Tab），复制精确文本作为 oldString。");
        return sb.ToString();
    }

    private static string BuildSimilarFoundMessage(
        int startLine, int endLine, double score, string snippet)
    {
        var sb = new StringBuilder(512 + snippet.Length);
        sb.Append("String to replace not found in file.");
        sb.Append("\n[诊断] 失败原因: SimilarFound");
        sb.Append($"\n最相似片段在文件第 {startLine}-{endLine} 行，Jaccard 相似度 {score:P1}:");
        sb.Append($"\n{snippet}");
        sb.Append("\n提示: 此片段与 oldString 高度相似但不完全相同，检查差异行。");
        return sb.ToString();
    }

    /// <summary>
    /// 截断过长的行用于错误消息显示（避免单行 10KB 把消息撑爆）。
    /// </summary>
    private static string TruncateForDisplay(string line, int maxLength = 200)
    {
        if (line.Length <= maxLength)
            return line;

        return string.Concat(line.AsSpan(0, maxLength), "...[truncated]");
    }
}
