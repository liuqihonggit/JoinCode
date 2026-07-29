using JoinCode.Abstractions.Attributes;

namespace Core.Context;

[Register]
public sealed partial class OutputLoopDetector : IOutputLoopDetector
{
    private readonly int _windowSize;
    private readonly int _minPatternLength;
    private readonly int _maxPatternLength;
    private readonly int _requiredRepeats;
    private readonly int _checkInterval;
    private readonly int _cooldownChars;
    private int _lastCheckedLength;
    private int _cooldownStartLength;
    private int _loopTriggerCount;
    private bool _inCooldown;

    /// <summary>
    /// 初始化输出循环检测器，指定检测窗口大小、模式长度范围、重复次数阈值、检查间隔和冷却期字符数。
    /// </summary>
    public OutputLoopDetector(
        int windowSize = 2000,
        int minPatternLength = 10,
        int maxPatternLength = 500,
        int requiredRepeats = 10,
        int checkInterval = 50,
        int cooldownChars = 500)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(windowSize, 100);
        ArgumentOutOfRangeException.ThrowIfLessThan(minPatternLength, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minPatternLength, maxPatternLength);
        ArgumentOutOfRangeException.ThrowIfLessThan(requiredRepeats, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(checkInterval, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(cooldownChars, 0);

        _windowSize = windowSize;
        _minPatternLength = minPatternLength;
        _maxPatternLength = maxPatternLength;
        _requiredRepeats = requiredRepeats;
        _checkInterval = checkInterval;
        _cooldownChars = cooldownChars;
    }

    public LoopDetectionResult Detect(string accumulatedText)
    {
        if (string.IsNullOrEmpty(accumulatedText))
            return LoopDetectionResult.NoLoop;

        var len = accumulatedText.Length;
        if (len < _minPatternLength * _requiredRepeats)
            return LoopDetectionResult.NoLoop;

        if (len - _lastCheckedLength < _checkInterval)
            return LoopDetectionResult.NoLoop;

        _lastCheckedLength = len;

        if (_inCooldown)
        {
            if (len - _cooldownStartLength >= _cooldownChars)
                _inCooldown = false;
            else
                return LoopDetectionResult.NoLoop;
        }

        var tailLen = Math.Min(len, _windowSize);
        var tailStart = len - tailLen;
        var maxCheckablePattern = Math.Min(_maxPatternLength, tailLen / _requiredRepeats);

        for (var patternLen = maxCheckablePattern; patternLen >= _minPatternLength; patternLen--)
        {
            var patternStart = len - patternLen;
            var pattern = accumulatedText[patternStart..];
            var repeatCount = 1;
            var pos = patternStart;

            while (pos >= patternLen)
            {
                var prevStart = pos - patternLen;
                if (accumulatedText[prevStart..pos] == pattern)
                {
                    repeatCount++;
                    pos = prevStart;
                }
                else
                {
                    break;
                }
            }

            if (repeatCount >= _requiredRepeats)
            {
                var loopStartIndex = pos;
                if (loopStartIndex < tailStart)
                    loopStartIndex = tailStart;

                _loopTriggerCount++;
                _cooldownStartLength = len;
                _inCooldown = true;

                return new LoopDetectionResult(
                    true, pattern, repeatCount, loopStartIndex,
                    LoopTriggerCount: _loopTriggerCount);
            }
        }

        return LoopDetectionResult.NoLoop;
    }

    /// <summary>
    /// 重置检测器内部状态，用于开始新一轮检测。
    /// </summary>
    public void Reset()
    {
        _lastCheckedLength = 0;
        _cooldownStartLength = 0;
        _loopTriggerCount = 0;
        _inCooldown = false;
    }

    /// <summary>
    /// 获取当前任务循环触发次数（供漏斗机制和TODO表联动使用）。
    /// </summary>
    public int LoopTriggerCount => _loopTriggerCount;
}
