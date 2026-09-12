namespace Core.Context.Compact.Guard;

public sealed class GibberishDetectionOptions
{
    public double HighEntropyThreshold { get; init; } = 0.95;
    public double LowEntropyThreshold { get; init; } = 0.15;
    public int MinUniqueCharsForCheck { get; init; } = 50;
    public double NonAsciiRatioThreshold { get; init; } = 0.80;
    public int MinLengthForCheck { get; init; } = 100;
}

public sealed class GibberishDetectionResult
{
    public required bool IsGibberish { get; init; }
    public required bool IsRepetition { get; init; }
    public required bool Skipped { get; init; }
    public double NormalizedEntropy { get; init; }
    public string? Reason { get; init; }
}

public static class GibberishDetector
{
    public static GibberishDetectionResult Detect(string text, GibberishDetectionOptions? options = null)
    {
        options ??= new GibberishDetectionOptions();

        if (string.IsNullOrEmpty(text) || text.Length < options.MinLengthForCheck)
        {
            return new GibberishDetectionResult
            {
                IsGibberish = false,
                IsRepetition = false,
                Skipped = true,
                Reason = "Text too short for gibberish detection"
            };
        }

        var freq = new Dictionary<char, int>();
        foreach (var c in text)
        {
            ref var count = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(freq, c, out _);
            count++;
        }

        var uniqueChars = freq.Count;

        if (uniqueChars <= 2 && text.Length >= options.MinLengthForCheck)
        {
            return new GibberishDetectionResult
            {
                IsGibberish = false,
                IsRepetition = true,
                Skipped = false,
                NormalizedEntropy = 0,
                Reason = $"Only {uniqueChars} unique character(s) in {text.Length} chars"
            };
        }

        if (uniqueChars < options.MinUniqueCharsForCheck)
        {
            return new GibberishDetectionResult
            {
                IsGibberish = false,
                IsRepetition = false,
                Skipped = true,
                Reason = $"Too few unique characters ({uniqueChars}) for entropy check"
            };
        }

        var entropy = CalculateShannonEntropy(freq, text.Length);
        var maxEntropy = Math.Log2(uniqueChars);
        var normalizedEntropy = maxEntropy > 0 ? entropy / maxEntropy : 0;

        if (normalizedEntropy > options.HighEntropyThreshold)
        {
            return new GibberishDetectionResult
            {
                IsGibberish = true,
                IsRepetition = false,
                Skipped = false,
                NormalizedEntropy = normalizedEntropy,
                Reason = $"High normalized entropy ({normalizedEntropy:F3}) suggests random characters"
            };
        }

        if (normalizedEntropy < options.LowEntropyThreshold)
        {
            return new GibberishDetectionResult
            {
                IsGibberish = false,
                IsRepetition = true,
                Skipped = false,
                NormalizedEntropy = normalizedEntropy,
                Reason = $"Low normalized entropy ({normalizedEntropy:F3}) suggests repetitive content"
            };
        }

        return new GibberishDetectionResult
        {
            IsGibberish = false,
            IsRepetition = false,
            Skipped = false,
            NormalizedEntropy = normalizedEntropy
        };
    }

    private static double CalculateShannonEntropy(Dictionary<char, int> freq, int totalLength)
    {
        var entropy = 0.0;
        foreach (var (_, count) in freq)
        {
            if (count <= 0) continue;
            var p = (double)count / totalLength;
            entropy -= p * Math.Log2(p);
        }
        return entropy;
    }
}
