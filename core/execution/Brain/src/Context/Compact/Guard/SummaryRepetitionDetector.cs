namespace Core.Context.Compact.Guard;

public sealed class SummaryRepetitionOptions
{
    public double RepetitionRatioThreshold { get; init; } = 0.4;
    public int WindowSize { get; init; } = 3;
    public double SimilarityThreshold { get; init; } = 0.8;
}

public sealed class SummaryRepetitionResult
{
    public required bool IsRepetition { get; init; }
    public double RepetitionRatio { get; init; }
    public string? Reason { get; init; }
}

public static class SummaryRepetitionDetector
{
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
