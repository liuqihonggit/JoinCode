namespace Core.Context.Compact.Guard;

/// <summary>
/// 乱码检测选项
/// </summary>
public sealed class GibberishDetectionOptions {
    /// <summary>高熵阈值，归一化熵超过此值判定为乱码（随机字符）</summary>
    public double HighEntropyThreshold { get; init; } = 0.95;
    /// <summary>低熵阈值，归一化熵低于此值判定为重复内容</summary>
    public double LowEntropyThreshold { get; init; } = 0.15;
    /// <summary>启用熵检查的最小唯一字符数</summary>
    public int MinUniqueCharsForCheck { get; init; } = 50;
    /// <summary>非 ASCII 字符占比阈值</summary>
    public double NonAsciiRatioThreshold { get; init; } = 0.80;
    /// <summary>启用检测的最小文本长度</summary>
    public int MinLengthForCheck { get; init; } = 100;
}

/// <summary>
/// 乱码检测结果
/// </summary>
public sealed class GibberishDetectionResult {
    /// <summary>是否判定为乱码</summary>
    public required bool IsGibberish { get; init; }
    /// <summary>是否判定为重复内容</summary>
    public required bool IsRepetition { get; init; }
    /// <summary>是否跳过检测（文本过短或唯一字符不足）</summary>
    public required bool Skipped { get; init; }
    /// <summary>归一化香农熵</summary>
    public double NormalizedEntropy { get; init; }
    /// <summary>诊断原因</summary>
    public string? Reason { get; init; }
}

/// <summary>
/// 乱码检测器 — 基于香农熵判断文本是否为随机字符或重复内容
/// </summary>
public static class GibberishDetector {
    /// <summary>
    /// 检测文本是否为乱码或重复内容
    /// </summary>
    /// <param name="text">待检测文本</param>
    /// <param name="options">可选检测选项，null 时使用默认值</param>
    /// <returns>检测结果，包含是否乱码、是否重复、归一化熵和原因</returns>
    public static GibberishDetectionResult Detect(string text, GibberishDetectionOptions? options = null) {
        options ??= new GibberishDetectionOptions();

        if (string.IsNullOrEmpty(text) || text.Length < options.MinLengthForCheck) {
            return new GibberishDetectionResult {
                IsGibberish = false,
                IsRepetition = false,
                Skipped = true,
                Reason = "Text too short for gibberish detection"
            };
        }

        var freq = new Dictionary<char, int>();
        foreach (var c in text) {
            ref var count = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(freq, c, out _);
            count++;
        }

        var uniqueChars = freq.Count;

        if (uniqueChars <= 2 && text.Length >= options.MinLengthForCheck) {
            return new GibberishDetectionResult {
                IsGibberish = false,
                IsRepetition = true,
                Skipped = false,
                NormalizedEntropy = 0,
                Reason = $"Only {uniqueChars} unique character(s) in {text.Length} chars"
            };
        }

        if (uniqueChars < options.MinUniqueCharsForCheck) {
            return new GibberishDetectionResult {
                IsGibberish = false,
                IsRepetition = false,
                Skipped = true,
                Reason = $"Too few unique characters ({uniqueChars}) for entropy check"
            };
        }

        var entropy = CalculateShannonEntropy(freq, text.Length);
        var maxEntropy = Math.Log2(uniqueChars);
        var normalizedEntropy = maxEntropy > 0 ? entropy / maxEntropy : 0;

        if (normalizedEntropy > options.HighEntropyThreshold) {
            return new GibberishDetectionResult {
                IsGibberish = true,
                IsRepetition = false,
                Skipped = false,
                NormalizedEntropy = normalizedEntropy,
                Reason = $"High normalized entropy ({normalizedEntropy:F3}) suggests random characters"
            };
        }

        if (normalizedEntropy < options.LowEntropyThreshold) {
            return new GibberishDetectionResult {
                IsGibberish = false,
                IsRepetition = true,
                Skipped = false,
                NormalizedEntropy = normalizedEntropy,
                Reason = $"Low normalized entropy ({normalizedEntropy:F3}) suggests repetitive content"
            };
        }

        return new GibberishDetectionResult {
            IsGibberish = false,
            IsRepetition = false,
            Skipped = false,
            NormalizedEntropy = normalizedEntropy
        };
    }

    private static double CalculateShannonEntropy(Dictionary<char, int> freq, int totalLength) {
        var entropy = 0.0;
        foreach (var (_, count) in freq) {
            if (count <= 0) continue;
            var p = (double)count / totalLength;
            entropy -= p * Math.Log2(p);
        }
        return entropy;
    }
}