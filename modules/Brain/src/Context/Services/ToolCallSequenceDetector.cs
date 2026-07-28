namespace Core.Context;

/// <summary>
/// 工具调用序列指纹检测器 — 渐进式严格检测工具调用模式重复
/// 宽松层: 工具名序列重复(如 Read→Grep→Read→Grep)
/// 严格层: 工具名+参数指纹都重复(如 Read(file.py)→Grep(pat)→Read(file.py)→Grep(pat))
/// 参数不匹配时需要更多重复才触发,参数匹配时正常触发
/// </summary>
public sealed class ToolCallSequenceDetector
{
    private readonly int _windowSize;
    private readonly int _minPatternLength;
    private readonly int _requiredRepeats;
    private readonly List<string> _nameSequence;
    private readonly List<string?> _fingerprintSequence;

    public ToolCallSequenceDetector(
        int windowSize = 6,
        int minPatternLength = 3,
        int requiredRepeats = 4)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(windowSize, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(minPatternLength, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(requiredRepeats, 2);

        _windowSize = windowSize;
        _minPatternLength = minPatternLength;
        _requiredRepeats = requiredRepeats;
        _nameSequence = [];
        _fingerprintSequence = [];
    }

    /// <summary>
    /// 记录一次工具调用（仅工具名，不含参数指纹）
    /// </summary>
    public ToolCallSequenceResult Record(string toolName)
    {
        return Record(toolName, null);
    }

    /// <summary>
    /// 记录一次工具调用（含参数指纹），返回检测结果
    /// 参数指纹格式: "toolName(arg1=val1,arg2=val2)" 或 "toolName(hash)"
    /// </summary>
    public ToolCallSequenceResult Record(string toolName, string? argsFingerprint)
    {
        ArgumentNullException.ThrowIfNull(toolName);

        _nameSequence.Add(toolName);
        _fingerprintSequence.Add(argsFingerprint);

        TrimSequences();

        if (_nameSequence.Count < _minPatternLength * _requiredRepeats)
            return ToolCallSequenceResult.NoLoop;

        for (var patternLen = Math.Min(_nameSequence.Count / _requiredRepeats, _windowSize / 2);
             patternLen >= _minPatternLength;
             patternLen--)
        {
            var result = AnalyzePattern(patternLen);
            if (result.IsLoopDetected)
                return result;
        }

        return ToolCallSequenceResult.NoLoop;
    }

    public void Reset()
    {
        _nameSequence.Clear();
        _fingerprintSequence.Clear();
    }

    private void TrimSequences()
    {
        var maxCount = _windowSize * _requiredRepeats + _windowSize;
        if (_nameSequence.Count > maxCount)
        {
            var removeCount = _nameSequence.Count - maxCount;
            _nameSequence.RemoveRange(0, removeCount);
            _fingerprintSequence.RemoveRange(0, removeCount);
        }
    }

    private ToolCallSequenceResult AnalyzePattern(int patternLen)
    {
        var repeatCount = 1;
        var argsMatchCount = 0;
        var pos = _nameSequence.Count;

        while (pos >= patternLen * 2)
        {
            var currentStart = pos - patternLen;
            var prevStart = currentStart - patternLen;

            var nameMatch = true;
            var thisArgsMatch = true;

            for (var i = 0; i < patternLen; i++)
            {
                if (_nameSequence[prevStart + i] != _nameSequence[currentStart + i])
                {
                    nameMatch = false;
                    break;
                }

                var prevFp = _fingerprintSequence[prevStart + i];
                var currFp = _fingerprintSequence[currentStart + i];

                if (prevFp is not null && currFp is not null && prevFp != currFp)
                    thisArgsMatch = false;
            }

            if (!nameMatch)
                break;

            repeatCount++;
            if (thisArgsMatch)
                argsMatchCount++;

            pos = prevStart + patternLen;
        }

        if (repeatCount < _requiredRepeats)
            return ToolCallSequenceResult.NoLoop;

        var pattern = string.Join("→", _nameSequence[^patternLen..]);
        var allArgsMatched = argsMatchCount >= repeatCount - 1;

        return new ToolCallSequenceResult(true, pattern, repeatCount, ArgsMatched: allArgsMatched);
    }
}

public sealed record ToolCallSequenceResult(
    bool IsLoopDetected,
    string? RepeatedPattern,
    int RepeatCount,
    bool ArgsMatched = false)
{
    public static readonly ToolCallSequenceResult NoLoop = new(false, null, 0);

    /// <summary>
    /// 有效触发次数 — 参数匹配时=RepeatCount, 参数不匹配时=RepeatCount-1(降级)
    /// 供 LoopInterventionMiddleware 判断漏斗级别
    /// </summary>
    public int TriggerCount => ArgsMatched ? RepeatCount : Math.Max(1, RepeatCount - 1);
}
