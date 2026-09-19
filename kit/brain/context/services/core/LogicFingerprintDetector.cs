namespace Core.Context;

/// <summary>
/// 逻辑指纹检测器 — 对每轮推理文本取结构指纹，滑动窗口内命中则判定逻辑循环
/// 检测"换词但同逻辑"的循环（如"让我检查A" → "我来查看A" → "我需要验证A"）
/// </summary>
public sealed class LogicFingerprintDetector {
    private readonly int _fingerprintPrefixLen;
    private readonly int _fingerprintSuffixLen;
    private readonly int _windowSize;
    private readonly int _hitThreshold;
    private readonly RingBuffer<int> _fingerprints;
    private int _triggerCount;

    /// <summary>
    /// 初始化逻辑指纹检测器
    /// </summary>
    /// <param name="fingerprintPrefixLen">指纹前缀长度</param>
    /// <param name="fingerprintSuffixLen">指纹后缀长度</param>
    /// <param name="windowSize">滑动窗口大小</param>
    /// <param name="hitThreshold">命中阈值</param>
    public LogicFingerprintDetector(
        int fingerprintPrefixLen = 200,
        int fingerprintSuffixLen = 200,
        int windowSize = 5,
        int hitThreshold = 4) {
        ArgumentOutOfRangeException.ThrowIfLessThan(fingerprintPrefixLen, 10);
        ArgumentOutOfRangeException.ThrowIfLessThan(fingerprintSuffixLen, 10);
        ArgumentOutOfRangeException.ThrowIfLessThan(windowSize, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(hitThreshold, 2);

        _fingerprintPrefixLen = fingerprintPrefixLen;
        _fingerprintSuffixLen = fingerprintSuffixLen;
        _windowSize = windowSize;
        _hitThreshold = hitThreshold;
        _fingerprints = new RingBuffer<int>(RingBuffer<int>.RoundUpToPowerOfTwo(windowSize * 2));
        _triggerCount = 0;
    }

    /// <summary>
    /// 记录一轮推理文本，返回检测结果
    /// </summary>
    public LogicFingerprintResult Record(string roundText) {
        ArgumentNullException.ThrowIfNull(roundText);

        if (roundText.Length < _fingerprintPrefixLen + _fingerprintSuffixLen)
            return LogicFingerprintResult.NoLoop;

        var fingerprint = ComputeFingerprint(roundText);

        var hitsInWindow = 0;
        var startIdx = Math.Max(0, _fingerprints.Count - _windowSize);
        for (var i = startIdx; i < _fingerprints.Count; i++) {
            if (_fingerprints[i] == fingerprint)
                hitsInWindow++;
        }

        _fingerprints.Add(fingerprint);

        if (hitsInWindow >= _hitThreshold - 1) {
            _triggerCount++;
            return new LogicFingerprintResult(true, fingerprint, hitsInWindow + 1, _triggerCount);
        }

        return LogicFingerprintResult.NoLoop;
    }

    /// <summary>
    /// 重置检测器状态
    /// </summary>
    public void Reset() {
        _fingerprints.Clear();
        _triggerCount = 0;
    }

    /// <summary>累计触发次数</summary>
    public int TriggerCount => _triggerCount;

    private int ComputeFingerprint(string text) {
        var len = text.Length;
        var prefixEnd = Math.Min(_fingerprintPrefixLen, len / 2);
        var suffixStart = Math.Max(len - _fingerprintSuffixLen, len / 2);

        var hash = new HashCode();
        for (var i = 0; i < prefixEnd; i++)
            hash.Add(text[i]);
        for (var i = suffixStart; i < len; i++)
            hash.Add(text[i]);

        return hash.ToHashCode();
    }
}

/// <summary>
/// 逻辑指纹检测结果
/// </summary>
/// <param name="IsLoopDetected">是否检测到循环</param>
/// <param name="Fingerprint">命中的逻辑指纹值</param>
/// <param name="HitCount">窗口内命中次数</param>
/// <param name="TriggerCount">累计触发次数</param>
public sealed record LogicFingerprintResult(
    bool IsLoopDetected,
    int Fingerprint,
    int HitCount,
    int TriggerCount) {
    /// <summary>未检测到循环的空结果</summary>
    public static readonly LogicFingerprintResult NoLoop = new(false, 0, 0, 0);
}