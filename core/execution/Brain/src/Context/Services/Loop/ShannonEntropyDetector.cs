namespace Core.Context;

/// <summary>
/// Shannon 信息熵减检测器状态机的三种状态
/// </summary>
public enum EntropyDetectionState
{
    /// <summary>监控中 — 未检测到熵减趋势或已复位</summary>
    Monitoring,

    /// <summary>疑似死循环 — 第一次触发，等待二次确认</summary>
    Suspected,

    /// <summary>确认死循环 — 确认窗口内二次触发，触发干预</summary>
    Confirmed
}

/// <summary>
/// Shannon 信息熵减检测器 — 时间窗口二次确认状态机
/// 原理：LLM 进入死循环时，输出越来越重复，字符分布趋于集中，熵值持续下降
/// 状态转换链：Monitoring →(decline>=threshold)→ Suspected →(窗口内再次触发)→ Confirmed
/// 误报消除：Suspected 状态超过确认窗口未再次触发 → 复位到 Monitoring
/// </summary>
public sealed class ShannonEntropyDetector
{
    private readonly int _windowSize;
    private readonly int _declineThreshold;
    private readonly double _minEntropyDelta;
    private readonly TimeSpan _confirmationWindow;
    private readonly Func<DateTimeOffset> _clock;
    private readonly RingBuffer<double> _entropyHistory;

    private EntropyDetectionState _state;
    private DateTimeOffset? _firstTriggerTime;
    private int _triggerCount;

    /// <summary>
    /// 初始化 Shannon 熵减检测器状态机
    /// </summary>
    /// <param name="windowSize">熵值历史窗口大小</param>
    /// <param name="declineThreshold">连续下降轮数阈值（连续 declineThreshold 轮熵递减则进入 Suspected 状态）</param>
    /// <param name="minEntropyDelta">最小熵差阈值（相邻轮熵差需超过此值才算"下降"）</param>
    /// <param name="confirmationWindow">二次确认时间窗口 — Suspected 状态下在此窗口内再次触发则确认死循环</param>
    /// <param name="clock">时钟注入点（仅测试用，生产环境用 DateTimeOffset.UtcNow）</param>
    public ShannonEntropyDetector(
        int windowSize,
        int declineThreshold,
        double minEntropyDelta,
        TimeSpan confirmationWindow,
        Func<DateTimeOffset>? clock = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(windowSize, 3);
        ArgumentOutOfRangeException.ThrowIfLessThan(declineThreshold, 2);
        ArgumentOutOfRangeException.ThrowIfNegative(minEntropyDelta);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(confirmationWindow, TimeSpan.Zero);

        _windowSize = windowSize;
        _declineThreshold = declineThreshold;
        _minEntropyDelta = minEntropyDelta;
        _confirmationWindow = confirmationWindow;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _entropyHistory = new RingBuffer<double>(RingBuffer<double>.RoundUpToPowerOfTwo(windowSize * 2));
        _state = EntropyDetectionState.Monitoring;
        _firstTriggerTime = null;
        _triggerCount = 0;
    }

    /// <summary>
    /// 记录一轮文本，计算 Shannon 熵并驱动状态机转换
    /// </summary>
    public ShannonEntropyResult Record(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.Length < 10)
            return new ShannonEntropyResult(_state, false, 0, 0, _triggerCount);

        var entropy = ComputeShannonEntropy(text);
        _entropyHistory.Add(entropy);

        var declineStreak = CountConsecutiveDecline();
        var isDeclining = declineStreak >= _declineThreshold;

        return _state switch
        {
            EntropyDetectionState.Monitoring => HandleMonitoring(entropy, declineStreak, isDeclining),
            EntropyDetectionState.Suspected => HandleSuspected(entropy, declineStreak, isDeclining),
            EntropyDetectionState.Confirmed => HandleConfirmed(entropy, declineStreak, isDeclining),
            _ => new ShannonEntropyResult(EntropyDetectionState.Monitoring, false, entropy, declineStreak, 0)
        };
    }

    /// <summary>
    /// Monitoring 状态处理 — 检测到熵减则进入 Suspected
    /// </summary>
    private ShannonEntropyResult HandleMonitoring(double entropy, int declineStreak, bool isDeclining)
    {
        if (!isDeclining)
            return new ShannonEntropyResult(EntropyDetectionState.Monitoring, false, entropy, declineStreak, 0);

        _state = EntropyDetectionState.Suspected;
        _firstTriggerTime = _clock();
        return new ShannonEntropyResult(EntropyDetectionState.Suspected, false, entropy, declineStreak, 0);
    }

    /// <summary>
    /// Suspected 状态处理 — 窗口内再次触发则确认，超时则复位
    /// </summary>
    private ShannonEntropyResult HandleSuspected(double entropy, int declineStreak, bool isDeclining)
    {
        var now = _clock();
        var firstTime = _firstTriggerTime ?? now;
        var elapsed = now - firstTime;

        if (elapsed > _confirmationWindow)
        {
            _state = EntropyDetectionState.Monitoring;
            _firstTriggerTime = null;

            if (isDeclining)
            {
                _state = EntropyDetectionState.Suspected;
                _firstTriggerTime = now;
                return new ShannonEntropyResult(EntropyDetectionState.Suspected, false, entropy, declineStreak, 0);
            }

            return new ShannonEntropyResult(EntropyDetectionState.Monitoring, false, entropy, declineStreak, 0);
        }

        if (isDeclining)
        {
            _state = EntropyDetectionState.Confirmed;
            _triggerCount++;
            return new ShannonEntropyResult(EntropyDetectionState.Confirmed, true, entropy, declineStreak, _triggerCount);
        }

        return new ShannonEntropyResult(EntropyDetectionState.Suspected, false, entropy, declineStreak, 0);
    }

    /// <summary>
    /// Confirmed 状态处理 — 持续熵减则保持确认，熵恢复则复位
    /// </summary>
    private ShannonEntropyResult HandleConfirmed(double entropy, int declineStreak, bool isDeclining)
    {
        if (isDeclining)
        {
            _triggerCount++;
            return new ShannonEntropyResult(EntropyDetectionState.Confirmed, true, entropy, declineStreak, _triggerCount);
        }

        _state = EntropyDetectionState.Monitoring;
        _firstTriggerTime = null;
        return new ShannonEntropyResult(EntropyDetectionState.Monitoring, false, entropy, declineStreak, _triggerCount);
    }

    /// <summary>
    /// 重置检测器状态机和所有历史
    /// </summary>
    public void Reset()
    {
        _entropyHistory.Clear();
        _triggerCount = 0;
        _state = EntropyDetectionState.Monitoring;
        _firstTriggerTime = null;
    }

    /// <summary>当前状态机状态</summary>
    public EntropyDetectionState State => _state;

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

/// <summary>
/// Shannon 熵减检测结果 — 携带状态机当前状态
/// </summary>
public sealed record ShannonEntropyResult(
    EntropyDetectionState State,
    bool IsLoopDetected,
    double CurrentEntropy,
    int DeclineStreak,
    int TriggerCount)
{
    /// <summary>
    /// 只有 Confirmed 状态才 IsLoopDetected=true，触发 LoopDetected 事件
    /// </summary>
    public static readonly ShannonEntropyResult NoLoop = new(EntropyDetectionState.Monitoring, false, 0, 0, 0);
}
