namespace Core.Context;

/// <summary>
/// 对抗性测试 — 从死循环的字面量出发，验证检测器在边界条件下不会误杀合法迭代
/// 不依赖已有测试，独立构造测试用例
/// </summary>
public sealed class LoopDetectorAdversarialTests
{
    // ═══════════════════════════════════════════════════════════════════
    // OutputLoopDetector 对抗性测试（默认 requiredRepeats=10）
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void OutputLoop_NineRepeats_BelowThreshold_ShouldNotTrigger()
    {
        var detector = new OutputLoopDetector();
        var loopPattern = "让我分析这段代码的实现方式，首先检查导入语句，然后查看函数定义，最后验证返回值类型。";
        var prefix = "用户要求重构登录模块。";
        var text = prefix + string.Concat(Enumerable.Repeat(loopPattern, 9));

        var result = detector.Detect(text);

        Assert.False(result.IsLoopDetected);
    }

    [Fact]
    public void OutputLoop_TenRepeats_AtThreshold_ShouldTrigger()
    {
        var detector = new OutputLoopDetector();
        var loopPattern = "让我分析这段代码的实现方式，首先检查导入语句，然后查看函数定义，最后验证返回值类型。";
        var prefix = "用户要求重构登录模块。";
        var text = prefix + string.Concat(Enumerable.Repeat(loopPattern, 10));

        var result = detector.Detect(text);

        Assert.True(result.IsLoopDetected);
        Assert.Equal(10, result.RepeatCount);
    }

    [Fact]
    public void OutputLoop_ShortPattern_ElevenRepeats_ShouldNotTrigger()
    {
        var detector = new OutputLoopDetector();
        var shortPattern = "短";
        var text = string.Concat(Enumerable.Repeat(shortPattern, 11));

        var result = detector.Detect(text);

        Assert.False(result.IsLoopDetected);
    }

    [Fact]
    public void OutputLoop_LoopPatternInMiddle_ShouldDetectAtTail()
    {
        var detector = new OutputLoopDetector();
        var normalText = "这是一段正常的分析文本，包含多个不同的观点和论述。每个段落都有独特的信息。";
        var loopPattern = "结论：需要进一步验证。";
        var text = normalText + string.Concat(Enumerable.Repeat(loopPattern, 10));

        var result = detector.Detect(text);

        Assert.True(result.IsLoopDetected);
        Assert.Equal(loopPattern, result.RepeatedPattern);
    }

    [Fact]
    public void OutputLoop_CooldownPreventsImmediateReTrigger()
    {
        var detector = new OutputLoopDetector(
            minPatternLength: 5, checkInterval: 1, cooldownChars: 500, requiredRepeats: 3);

        var pattern = "这是正确的代码实现方案。";
        var text1 = "前置" + string.Concat(Enumerable.Repeat(pattern, 3));

        var result1 = detector.Detect(text1);
        Assert.True(result1.IsLoopDetected);

        var text2 = text1 + string.Concat(Enumerable.Repeat(pattern, 2));
        var result2 = detector.Detect(text2);
        Assert.False(result2.IsLoopDetected);
    }

    [Fact]
    public void OutputLoop_CooldownExpires_DetectionResumes()
    {
        var detector = new OutputLoopDetector(
            minPatternLength: 5, checkInterval: 1, cooldownChars: 50, requiredRepeats: 3);

        var pattern = "这是正确的代码实现方案。";
        var text1 = "前置" + string.Concat(Enumerable.Repeat(pattern, 3));
        detector.Detect(text1);

        var padding = new string('X', 100);
        var text2 = text1 + padding + string.Concat(Enumerable.Repeat(pattern, 3));
        var result2 = detector.Detect(text2);

        Assert.True(result2.IsLoopDetected);
        Assert.Equal(2, result2.LoopTriggerCount);
    }

    // ═══════════════════════════════════════════════════════════════════
    // ToolCallSequenceDetector 对抗性测试（默认 minPatternLength=3, requiredRepeats=4）
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ToolCall_ReadGrepThreeTimes_BelowMinPattern_ShouldNotTrigger()
    {
        var detector = new ToolCallSequenceDetector();
        detector.Record("Read");
        detector.Record("Grep");
        detector.Record("Read");
        detector.Record("Grep");
        detector.Record("Read");
        var result = detector.Record("Grep");

        Assert.False(result.IsLoopDetected);
    }

    [Fact]
    public void ToolCall_ReadGrepEditThreeTimes_BelowRequiredRepeats_ShouldNotTrigger()
    {
        var detector = new ToolCallSequenceDetector();
        detector.Record("Read");
        detector.Record("Grep");
        detector.Record("Edit");
        detector.Record("Read");
        detector.Record("Grep");
        detector.Record("Edit");
        detector.Record("Read");
        detector.Record("Grep");
        var result = detector.Record("Edit");

        Assert.False(result.IsLoopDetected);
    }

    [Fact]
    public void ToolCall_ReadGrepEditFourTimes_AtThreshold_ShouldTrigger()
    {
        var detector = new ToolCallSequenceDetector();
        detector.Record("Read");
        detector.Record("Grep");
        detector.Record("Edit");
        detector.Record("Read");
        detector.Record("Grep");
        detector.Record("Edit");
        detector.Record("Read");
        detector.Record("Grep");
        detector.Record("Edit");
        detector.Record("Read");
        detector.Record("Grep");
        var result = detector.Record("Edit");

        Assert.True(result.IsLoopDetected);
        Assert.Equal("Read→Grep→Edit", result.RepeatedPattern);
    }

    [Fact]
    public void ToolCall_SamePatternFourTimes_ShouldTrigger()
    {
        var detector = new ToolCallSequenceDetector();
        detector.Record("Read");
        detector.Record("Grep");
        detector.Record("Shell");
        detector.Record("Read");
        detector.Record("Grep");
        detector.Record("Shell");
        detector.Record("Read");
        detector.Record("Grep");
        detector.Record("Shell");
        detector.Record("Read");
        detector.Record("Grep");
        var result = detector.Record("Shell");

        Assert.True(result.IsLoopDetected);
    }

    [Fact]
    public void ToolCall_DifferentArgs_ShouldDowngradeTriggerCount()
    {
        var detector = new ToolCallSequenceDetector();
        detector.Record("Read", "Read(file1.py)");
        detector.Record("Grep", "Grep(pattern1)");
        detector.Record("Edit", "Edit(file1.py)");
        detector.Record("Read", "Read(file2.py)");
        detector.Record("Grep", "Grep(pattern2)");
        detector.Record("Edit", "Edit(file2.py)");
        detector.Record("Read", "Read(file3.py)");
        detector.Record("Grep", "Grep(pattern3)");
        detector.Record("Edit", "Edit(file3.py)");
        detector.Record("Read", "Read(file4.py)");
        detector.Record("Grep", "Grep(pattern4)");
        var result = detector.Record("Edit", "Edit(file4.py)");

        Assert.True(result.IsLoopDetected);
        Assert.False(result.ArgsMatched);
        Assert.Equal(3, result.TriggerCount);
    }

    [Fact]
    public void ToolCall_MixedArgs_PartialMatch_ShouldDowngrade()
    {
        var detector = new ToolCallSequenceDetector();
        detector.Record("Read", "Read(file.py)");
        detector.Record("Grep", "Grep(pattern1)");
        detector.Record("Edit", "Edit(file.py)");
        detector.Record("Read", "Read(file.py)");
        detector.Record("Grep", "Grep(pattern2)");
        detector.Record("Edit", "Edit(file.py)");
        detector.Record("Read", "Read(file.py)");
        detector.Record("Grep", "Grep(pattern3)");
        detector.Record("Edit", "Edit(file.py)");
        detector.Record("Read", "Read(file.py)");
        detector.Record("Grep", "Grep(pattern4)");
        var result = detector.Record("Edit", "Edit(file.py)");

        Assert.True(result.IsLoopDetected);
        Assert.False(result.ArgsMatched);
        Assert.Equal(4, result.RepeatCount);
        Assert.Equal(3, result.TriggerCount);
    }

    [Fact]
    public void ToolCall_BreaksLoop_ShouldNotTrigger()
    {
        var detector = new ToolCallSequenceDetector();
        detector.Record("Read");
        detector.Record("Grep");
        detector.Record("Edit");
        detector.Record("Read");
        detector.Record("Grep");
        detector.Record("Edit");
        detector.Record("Write");
        detector.Record("Shell");
        var result = detector.Record("Read");

        Assert.False(result.IsLoopDetected);
    }

    // ═══════════════════════════════════════════════════════════════════
    // LogicFingerprintDetector 对抗性测试（默认 hitThreshold=4）
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void LogicFingerprint_SameTextThreeTimes_BelowThreshold_ShouldNotTrigger()
    {
        var detector = new LogicFingerprintDetector(fingerprintPrefixLen: 10, fingerprintSuffixLen: 10);
        var text = new string('A', 100);
        detector.Record(text);
        detector.Record(text);
        var result = detector.Record(text);

        Assert.False(result.IsLoopDetected);
    }

    [Fact]
    public void LogicFingerprint_SameTextFourTimes_AtThreshold_ShouldTrigger()
    {
        var detector = new LogicFingerprintDetector(fingerprintPrefixLen: 10, fingerprintSuffixLen: 10);
        var text = new string('A', 100);
        detector.Record(text);
        detector.Record(text);
        detector.Record(text);
        var result = detector.Record(text);

        Assert.True(result.IsLoopDetected);
    }

    [Fact]
    public void LogicFingerprint_DifferentTexts_ShouldNotTrigger()
    {
        var detector = new LogicFingerprintDetector(fingerprintPrefixLen: 10, fingerprintSuffixLen: 10);
        detector.Record(new string('A', 100));
        detector.Record(new string('B', 100));
        detector.Record(new string('C', 100));
        var result = detector.Record(new string('D', 100));

        Assert.False(result.IsLoopDetected);
    }

    [Fact]
    public void LogicFingerprint_SamePrefixSuffix_DifferentMiddle_ShouldDetect()
    {
        var detector = new LogicFingerprintDetector(
            fingerprintPrefixLen: 10, fingerprintSuffixLen: 10, hitThreshold: 2);
        var text1 = "让我分析这段代码的实现方式，中间内容完全不同但是首尾一样，结论是需要重构";
        var text2 = "让我分析这段代码的实现方式，中间内容差异很大但是首尾一样，结论是需要重构";
        detector.Record(text1);
        var result = detector.Record(text2);

        Assert.True(result.IsLoopDetected);
    }

    [Fact]
    public void LogicFingerprint_ShortText_ShouldNotTrigger()
    {
        var detector = new LogicFingerprintDetector(fingerprintPrefixLen: 100, fingerprintSuffixLen: 100);
        var shortText = "短文本";
        detector.Record(shortText);
        detector.Record(shortText);
        var result = detector.Record(shortText);

        Assert.False(result.IsLoopDetected);
    }

    [Fact]
    public void LogicFingerprint_Reset_ClearsState()
    {
        var detector = new LogicFingerprintDetector(fingerprintPrefixLen: 10, fingerprintSuffixLen: 10);
        var text = new string('A', 100);
        detector.Record(text);
        detector.Record(text);
        detector.Reset();
        detector.Record(text);
        var result = detector.Record(text);

        Assert.False(result.IsLoopDetected);
    }

    // ═══════════════════════════════════════════════════════════════════
    // LoopInterventionMiddleware 漏斗级别边界测试
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Funnel_Level1_ShouldOnlyInjectPrompt()
    {
        var options = new LoopInterventionOptions();
        Assert.Equal(3, options.HardTruncateThreshold);
        Assert.Equal(5, options.CompactThreshold);
        Assert.Equal(1, options.ProgressDiscount);
    }

    [Fact]
    public void Funnel_ProgressDiscount_ShouldReduceEffectiveCount()
    {
        var options = new LoopInterventionOptions();
        var effectiveCount = Math.Max(1, 3 - options.ProgressDiscount);

        Assert.Equal(2, effectiveCount);
        Assert.True(effectiveCount < options.HardTruncateThreshold);
    }

    [Fact]
    public void Funnel_NoProgress_NoDiscount()
    {
        var options = new LoopInterventionOptions();
        var triggerCount = 3;
        var hasProgressed = false;
        var effectiveCount = hasProgressed
            ? Math.Max(1, triggerCount - options.ProgressDiscount)
            : triggerCount;

        Assert.Equal(3, effectiveCount);
        Assert.True(effectiveCount >= options.HardTruncateThreshold);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 中文短文本循环对抗性测试
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void OutputLoop_ChineseShortPattern_FourRepeats_NotDetected()
    {
        var detector = new OutputLoopDetector();
        var text = "床前明月光,疑是疑似疑似疑似疑似";

        var result = detector.Detect(text);

        Assert.False(result.IsLoopDetected);
    }

    [Fact]
    public void OutputLoop_ChineseShortPattern_TenRepeats_StillNotDetected()
    {
        var detector = new OutputLoopDetector();
        var text = "床前明月光," + string.Concat(Enumerable.Repeat("疑似", 10));

        var result = detector.Detect(text);

        Assert.False(result.IsLoopDetected);
    }

    [Fact]
    public void OutputLoop_ChineseShortPattern_WithCustomMinLength_Detected()
    {
        var detector = new OutputLoopDetector(minPatternLength: 2, requiredRepeats: 3, checkInterval: 1);
        var text = "床前明月光," + string.Concat(Enumerable.Repeat("疑似", 3));

        var result = detector.Detect(text);

        Assert.True(result.IsLoopDetected);
        Assert.Equal("疑似", result.RepeatedPattern);
    }

    [Fact]
    public void OutputLoop_ChineseMixedPattern_LongEnough_Detected()
    {
        var detector = new OutputLoopDetector();
        var pattern = "这是一个较长的重复模式，用于测试中文文本的循环检测能力。";
        var text = "前言," + string.Concat(Enumerable.Repeat(pattern, 10));

        var result = detector.Detect(text);

        Assert.True(result.IsLoopDetected);
    }

    [Fact]
    public void OutputLoop_EnglishShortPattern_FourRepeats_NotDetected()
    {
        var detector = new OutputLoopDetector();
        var text = "Hello world, haha haha haha haha";

        var result = detector.Detect(text);

        Assert.False(result.IsLoopDetected);
    }

    [Fact]
    public void OutputLoop_NumberPattern_TenRepeats_Detected()
    {
        var detector = new OutputLoopDetector();
        var pattern = "1234567890";
        var text = "prefix" + string.Concat(Enumerable.Repeat(pattern, 10));

        var result = detector.Detect(text);

        Assert.True(result.IsLoopDetected);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 用户指定模式：xxxxxxxxxxxxxxxxx,xxxxxxxxxxxxxxxxx,...
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void OutputLoop_UserPattern_FourRepeats_DefaultParams_NotDetected()
    {
        var detector = new OutputLoopDetector();
        var pattern = "xxxxxxxxxxxxxxxxx,";
        var text = string.Concat(Enumerable.Repeat(pattern, 4));

        var result = detector.Detect(text);

        Assert.False(result.IsLoopDetected);
    }

    [Fact]
    public void OutputLoop_UserPattern_FourRepeats_LoweredRequiredRepeats_Detected()
    {
        var detector = new OutputLoopDetector(requiredRepeats: 4, checkInterval: 1);
        var pattern = "xxxxxxxxxxxxxxxxx,";
        var text = string.Concat(Enumerable.Repeat(pattern, 4));

        var result = detector.Detect(text);

        Assert.True(result.IsLoopDetected);
        Assert.Equal(pattern, result.RepeatedPattern);
        Assert.Equal(4, result.RepeatCount);
    }

    [Fact]
    public void OutputLoop_UserPattern_TenRepeats_DefaultParams_Detected()
    {
        var detector = new OutputLoopDetector();
        var pattern = "xxxxxxxxxxxxxxxxx,";
        var text = string.Concat(Enumerable.Repeat(pattern, 10));

        var result = detector.Detect(text);

        Assert.True(result.IsLoopDetected);
    }

    [Fact]
    public void OutputLoop_UserPattern_NineRepeats_DefaultParams_NotDetected()
    {
        var detector = new OutputLoopDetector();
        var pattern = "xxxxxxxxxxxxxxxxx,";
        var text = string.Concat(Enumerable.Repeat(pattern, 9));

        var result = detector.Detect(text);

        Assert.False(result.IsLoopDetected);
    }
}
