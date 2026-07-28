namespace Core.Context;

/// <summary>
/// 逻辑指纹检测器 — 对每轮推理文本取结构指纹，滑动窗口内命中则判定逻辑循环
/// 检测"换词但同逻辑"的循环（如"让我检查A" → "我来查看A" → "我需要验证A"）
/// </summary>
public sealed class LogicFingerprintDetector
{
    private readonly int _fingerprintPrefixLen;
    private readonly int _fingerprintSuffixLen;
    private readonly int _windowSize;
    private readonly int _hitThreshold;
    private readonly List<int> _fingerprints;
    private int _triggerCount;

    public LogicFingerprintDetector(
        int fingerprintPrefixLen = 200,
        int fingerprintSuffixLen = 200,
        int windowSize = 5,
        int hitThreshold = 4)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(fingerprintPrefixLen, 10);
        ArgumentOutOfRangeException.ThrowIfLessThan(fingerprintSuffixLen, 10);
        ArgumentOutOfRangeException.ThrowIfLessThan(windowSize, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(hitThreshold, 2);

        _fingerprintPrefixLen = fingerprintPrefixLen;
        _fingerprintSuffixLen = fingerprintSuffixLen;
        _windowSize = windowSize;
        _hitThreshold = hitThreshold;
        _fingerprints = [];
        _triggerCount = 0;
    }

    /// <summary>
    /// 记录一轮推理文本，返回检测结果
    /// </summary>
    public LogicFingerprintResult Record(string roundText)
    {
        ArgumentNullException.ThrowIfNull(roundText);

        if (roundText.Length < _fingerprintPrefixLen + _fingerprintSuffixLen)
            return LogicFingerprintResult.NoLoop;

        var fingerprint = ComputeFingerprint(roundText);

        var hitsInWindow = 0;
        var startIdx = Math.Max(0, _fingerprints.Count - _windowSize);
        for (var i = startIdx; i < _fingerprints.Count; i++)
        {
            if (_fingerprints[i] == fingerprint)
                hitsInWindow++;
        }

        _fingerprints.Add(fingerprint);

        if (_fingerprints.Count > _windowSize * 2)
            _fingerprints.RemoveRange(0, _fingerprints.Count - _windowSize * 2);

        if (hitsInWindow >= _hitThreshold - 1)
        {
            _triggerCount++;
            return new LogicFingerprintResult(true, fingerprint, hitsInWindow + 1, _triggerCount);
        }

        return LogicFingerprintResult.NoLoop;
    }

    /// <summary>
    /// 重置检测器状态
    /// </summary>
    public void Reset()
    {
        _fingerprints.Clear();
        _triggerCount = 0;
    }

    public int TriggerCount => _triggerCount;

    private int ComputeFingerprint(string text)
    {
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

public sealed record LogicFingerprintResult(
    bool IsLoopDetected,
    int Fingerprint,
    int HitCount,
    int TriggerCount)
{
    public static readonly LogicFingerprintResult NoLoop = new(false, 0, 0, 0);
}
