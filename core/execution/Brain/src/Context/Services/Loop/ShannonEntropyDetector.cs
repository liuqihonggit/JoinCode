namespace Core.Context;

/// <summary>
/// Shannon 信息熵减检测器 — 跟踪文本信息熵变化趋势
/// 当连续多轮的 Shannon 熵持续下降时，判定为信息熵减循环
/// 原理：LLM 进入死循环时，输出越来越重复，字符分布趋于集中，熵值持续下降
/// </summary>
public sealed class ShannonEntropyDetector
{
    private readonly int _windowSize;
    private readonly int _declineThreshold;
    private readonly double _minEntropyDelta;
    private readonly RingBuffer<double> _entropyHistory;
    private int _triggerCount;

    /// <summary>
    /// 初始化 Shannon 熵减检测器
    /// </summary>
    /// <param name="windowSize">熵值历史窗口大小</param>
    /// <param name="declineThreshold">连续下降轮数阈值（连续 declineThreshold 轮熵递减则触发）</param>
    /// <param name="minEntropyDelta">最小熵差阈值（相邻轮熵差需超过此值才算"下降"）</param>
    public ShannonEntropyDetector(
        int windowSize = 10,
        int declineThreshold = 4,
        double minEntropyDelta = 0.05)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(windowSize, 3);
        ArgumentOutOfRangeException.ThrowIfLessThan(declineThreshold, 2);
        ArgumentOutOfRangeException.ThrowIfNegative(minEntropyDelta);

        _windowSize = windowSize;
        _declineThreshold = declineThreshold;
        _minEntropyDelta = minEntropyDelta;
        _entropyHistory = new RingBuffer<double>(RingBuffer<double>.RoundUpToPowerOfTwo(windowSize * 2));
        _triggerCount = 0;
    }

    /// <summary>
    /// 记录一轮文本，计算 Shannon 熵并检测下降趋势
    /// </summary>
    public ShannonEntropyResult Record(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.Length < 10)
            return ShannonEntropyResult.NoLoop;

        var entropy = ComputeShannonEntropy(text);

        _entropyHistory.Add(entropy);

        var declineStreak = CountConsecutiveDecline();
        var currentEntropy = entropy;

        if (declineStreak >= _declineThreshold)
        {
            _triggerCount++;
            return new ShannonEntropyResult(true, currentEntropy, declineStreak, _triggerCount);
        }

        return new ShannonEntropyResult(false, currentEntropy, declineStreak, 0);
    }

    /// <summary>
    /// 重置检测器状态
    /// </summary>
    public void Reset()
    {
        _entropyHistory.Clear();
        _triggerCount = 0;
    }

    public int TriggerCount => _triggerCount;

    /// <summary>
    /// 计算 Shannon 信息熵 H = -Σ(p_i * log2(p_i))
    /// </summary>
    private static double ComputeShannonEntropy(string text)
    {
        if (text.Length == 0)
            return 0.0;

        var freq = new Dictionary<char, int>();
        foreach (var c in text)
        {
            ref var count = ref CollectionsMarshal.GetValueRefOrAddDefault(freq, c, out _);
            count++;
        }

        var entropy = 0.0;
        var len = (double)text.Length;

        foreach (var kvp in freq)
        {
            var p = kvp.Value / len;
            entropy -= p * Math.Log2(p);
        }

        return entropy;
    }

    /// <summary>
    /// 计算连续下降轮数（从最新往回看，每轮熵差超过 minEntropyDelta 才算下降）
    /// </summary>
    private int CountConsecutiveDecline()
    {
        if (_entropyHistory.Count < 2)
            return 0;

        var streak = 0;
        for (var i = _entropyHistory.Count - 1; i >= 1; i--)
        {
            var delta = _entropyHistory[i - 1] - _entropyHistory[i];
            if (delta >= _minEntropyDelta)
            {
                streak++;
            }
            else
            {
                break;
            }
        }

        return streak;
    }
}

public sealed record ShannonEntropyResult(
    bool IsLoopDetected,
    double CurrentEntropy,
    int DeclineStreak,
    int TriggerCount)
{
    public static readonly ShannonEntropyResult NoLoop = new(false, 0, 0, 0);
}
