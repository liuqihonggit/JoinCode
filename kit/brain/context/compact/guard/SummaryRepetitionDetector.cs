namespace Core.Context.Compact.Guard;

/// <summary>
/// 摘要重复检测选项
/// </summary>
public sealed class SummaryRepetitionOptions
{
    /// <summary>重复段落占比阈值，超过则判定为重复</summary>
    public double RepetitionRatioThreshold { get; init; } = 0.4;
    /// <summary>滑动窗口大小，比较当前段落前后 N 个段落</summary>
    public int WindowSize { get; init; } = 3;
    /// <summary>段落相似度阈值，基于 Jaccard 相似度</summary>
    public double SimilarityThreshold { get; init; } = 0.8;
}

/// <summary>
/// 摘要重复检测结果
/// </summary>
public sealed class SummaryRepetitionResult
{
    /// <summary>是否检测到重复</summary>
    public required bool IsRepetition { get; init; }
    /// <summary>重复段落占比</summary>
    public double RepetitionRatio { get; init; }
    /// <summary>诊断原因</summary>
    public string? Reason { get; init; }
}

/// <summary>
/// 摘要重复检测器 — 基于段落 Jaccard 相似度和滑动窗口检测重复内容
/// </summary>
public static class SummaryRepetitionDetector
{
    /// <summary>
    /// 检测摘要中是否存在重复段落
    /// </summary>
    /// <param name="summary">待检测的摘要文本</param>
    /// <param name="options">可选检测选项，null 时使用默认值</param>
    /// <returns>检测结果，包含是否重复、重复占比和原因</returns>
    public static SummaryRepetitionResult Detect(string summary, SummaryRepetitionOptions? options = null)
    {
        options ??= new SummaryRepetitionOptions();

        if (string.IsNullOrEmpty(summary))
        {
            return new SummaryRepetitionResult { IsRepetition = false, RepetitionRatio = 0 };
        }

        var paragraphs = summary.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToList();

        if (paragraphs.Count < 3)
        {
            return new SummaryRepetitionResult { IsRepetition = false, RepetitionRatio = 0 };
        }

        var duplicateCount = 0;
        for (var i = 0; i < paragraphs.Count; i++)
        {
            var isDuplicate = false;
            for (var j = Math.Max(0, i - options.WindowSize); j < Math.Min(paragraphs.Count, i + options.WindowSize + 1); j++)
            {
                if (i == j) continue;
                var similarity = ComputeJaccardSimilarity(paragraphs[i], paragraphs[j]);
                if (similarity >= options.SimilarityThreshold)
                {
                    isDuplicate = true;
                    break;
                }
            }
            if (isDuplicate) duplicateCount++;
        }

        var ratio = (double)duplicateCount / paragraphs.Count;

        if (ratio > options.RepetitionRatioThreshold)
        {
            return new SummaryRepetitionResult
            {
                IsRepetition = true,
                RepetitionRatio = ratio,
                Reason = $"Repetition ratio {ratio:P1} exceeds threshold {options.RepetitionRatioThreshold:P1}"
            };
        }

        return new SummaryRepetitionResult { IsRepetition = false, RepetitionRatio = ratio };
    }

    private static double ComputeJaccardSimilarity(string a, string b)
    {
        var wordsA = new HashSet<string>(a.Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
        var wordsB = new HashSet<string>(b.Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);

        if (wordsA.Count == 0 && wordsB.Count == 0) return 1.0;
        if (wordsA.Count == 0 || wordsB.Count == 0) return 0.0;

        var intersection = 0;
        foreach (var word in wordsA)
        {
            if (wordsB.Contains(word)) intersection++;
        }

        var union = wordsA.Count + wordsB.Count - intersection;
        return union == 0 ? 0.0 : (double)intersection / union;
    }
}
