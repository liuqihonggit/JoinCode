namespace Core.Context;

public sealed class OutputLoopDetectorTests
{
    private readonly OutputLoopDetector _sut = new(
        minPatternLength: 5,
        checkInterval: 1,
        requiredRepeats: 3);

    [Fact]
    public void Detect_ShortText_ReturnsNoLoop()
    {
        var result = _sut.Detect("Hello world");

        Assert.False(result.IsLoopDetected);
    }

    [Fact]
    public void Detect_ThreeRepeats_SamePattern_ReturnsLoop()
    {
        var pattern = "这是正确的代码实现。";
        var text = "前置内容" + string.Concat(Enumerable.Repeat(pattern, 3));

        var result = _sut.Detect(text);

        Assert.True(result.IsLoopDetected);
        Assert.Equal(3, result.RepeatCount);
        Assert.Equal(pattern, result.RepeatedPattern);
    }

    [Fact]
    public void Detect_TwoRepeats_ReturnsNoLoop()
    {
        var pattern = "这是正确的代码实现。";
        var text = "前置内容" + string.Concat(Enumerable.Repeat(pattern, 2));

        var result = _sut.Detect(text);

        Assert.False(result.IsLoopDetected);
    }

    [Fact]
    public void Detect_LongPattern_ThreeRepeats_ReturnsLoop()
    {
        var pattern = new string('A', 200);
        var text = "前置内容" + string.Concat(Enumerable.Repeat(pattern, 3));

        var result = _sut.Detect(text);

        Assert.True(result.IsLoopDetected);
        Assert.Equal(3, result.RepeatCount);
    }

    [Fact]
    public void Detect_MinLengthPattern_ThreeRepeats_ReturnsLoop()
    {
        var pattern = new string('B', 5);
        var text = string.Concat(Enumerable.Repeat(pattern, 4));

        var result = _sut.Detect(text);

        Assert.True(result.IsLoopDetected);
        Assert.True(result.RepeatCount >= 3);
    }

    [Fact]
    public void Detect_LoopStartIndex_PointsToBeforeRepetition()
    {
        var pattern = "重复内容XYZ";
        var prefix = "这是正常的前置文本。";
        var text = prefix + string.Concat(Enumerable.Repeat(pattern, 3));

        var result = _sut.Detect(text);

        Assert.True(result.IsLoopDetected);
        Assert.Equal(prefix.Length, result.LoopStartIndex);
    }

    [Fact]
    public void Detect_NormalText_NoFalsePositive()
    {
        var text = "第一段内容。第二段不同的内容。第三段又是新的内容。这些都不重复。";

        var result = _sut.Detect(text);

        Assert.False(result.IsLoopDetected);
    }

    [Fact]
    public void Detect_FourRepeats_ReturnsLoopWithCount4()
    {
        var pattern = "循环段落内容。";
        var text = string.Concat(Enumerable.Repeat(pattern, 4));

        var result = _sut.Detect(text);

        Assert.True(result.IsLoopDetected);
        Assert.Equal(4, result.RepeatCount);
    }

    [Fact]
    public void Reset_ClearsInternalState()
    {
        var pattern = "重复内容文本。";
        var text = string.Concat(Enumerable.Repeat(pattern, 3));

        var result1 = _sut.Detect(text);
        Assert.True(result1.IsLoopDetected);

        _sut.Reset();

        var shortText = "短文本";
        var result2 = _sut.Detect(shortText);
        Assert.False(result2.IsLoopDetected);
    }

    [Fact]
    public void Detect_MixedContentWithLoopAtTail_ReturnsLoop()
    {
        var prefix = "这是一段正常的分析文本，包含多个不同的句子。每个句子都有独特的含义。";
        var loopPattern = "重复的结论部分。";
        var text = prefix + string.Concat(Enumerable.Repeat(loopPattern, 3));

        var result = _sut.Detect(text);

        Assert.True(result.IsLoopDetected);
        Assert.True(result.LoopStartIndex >= prefix.Length - loopPattern.Length);
    }

    [Fact]
    public void Detect_EmptyString_ReturnsNoLoop()
    {
        var result = _sut.Detect(string.Empty);

        Assert.False(result.IsLoopDetected);
    }

    [Fact]
    public void Detect_CheckInterval_SkipsIntermediateCalls()
    {
        var detector = new OutputLoopDetector(minPatternLength: 5, checkInterval: 100, requiredRepeats: 3);
        var pattern = new string('X', 20);
        var shortText = "短文本";

        var result = detector.Detect(shortText);
        Assert.False(result.IsLoopDetected);

        var loopText = string.Concat(Enumerable.Repeat(pattern, 6));
        var result2 = detector.Detect(loopText);
        Assert.True(result2.IsLoopDetected);
    }

    [Fact]
    public void Detect_LoopStartIndex_TruncationPreservesPrefix()
    {
        var prefix = "这是正常回复的前半部分内容。";
        var pattern = "重复段落内容。";
        var text = prefix + string.Concat(Enumerable.Repeat(pattern, 3));

        var result = _sut.Detect(text);

        Assert.True(result.IsLoopDetected);
        var truncated = text[..result.LoopStartIndex];
        Assert.StartsWith(prefix[..^5], truncated);
    }

    [Fact]
    public void Detect_DefaultParameters_DetectsRealisticLoop()
    {
        var defaultDetector = new OutputLoopDetector();
        var pattern = "这是LLM重复输出的段落，包含了完整的句子和标点符号。";
        var text = "正常的前置回复内容。" + string.Concat(Enumerable.Repeat(pattern, 10));

        var result = defaultDetector.Detect(text);

        Assert.True(result.IsLoopDetected);
    }

    [Fact]
    public void Detect_DefaultParameters_NineRepeats_NotDetected()
    {
        var defaultDetector = new OutputLoopDetector();
        var pattern = "这是LLM重复输出的段落，包含了完整的句子和标点符号。";
        var text = "正常的前置回复内容。" + string.Concat(Enumerable.Repeat(pattern, 9));

        var result = defaultDetector.Detect(text);

        Assert.False(result.IsLoopDetected);
    }

    [Fact]
    public void Detect_FirstTrigger_LoopTriggerCountIs1()
    {
        var pattern = "这是正确的代码实现。";
        var text = "前置内容" + string.Concat(Enumerable.Repeat(pattern, 3));

        var result = _sut.Detect(text);

        Assert.True(result.IsLoopDetected);
        Assert.Equal(1, result.LoopTriggerCount);
    }

    [Fact]
    public void Detect_CooldownPeriod_SecondDetectionReturnsNoLoop()
    {
        var detector = new OutputLoopDetector(minPatternLength: 5, checkInterval: 1, cooldownChars: 500, requiredRepeats: 3);
        var pattern = "这是正确的代码实现。";
        var text1 = "前置内容" + string.Concat(Enumerable.Repeat(pattern, 3));

        var result1 = detector.Detect(text1);
        Assert.True(result1.IsLoopDetected);
        Assert.Equal(1, result1.LoopTriggerCount);

        var text2 = text1 + string.Concat(Enumerable.Repeat(pattern, 2));
        var result2 = detector.Detect(text2);
        Assert.False(result2.IsLoopDetected);
    }

    [Fact]
    public void Detect_CooldownExpires_DetectionResumes()
    {
        var detector = new OutputLoopDetector(minPatternLength: 5, checkInterval: 1, cooldownChars: 50, requiredRepeats: 3);
        var pattern = "这是正确的代码实现。";
        var text1 = "前置内容" + string.Concat(Enumerable.Repeat(pattern, 3));

        var result1 = detector.Detect(text1);
        Assert.True(result1.IsLoopDetected);
        Assert.Equal(1, result1.LoopTriggerCount);

        var padding = new string('X', 100);
        var text2 = text1 + padding + string.Concat(Enumerable.Repeat(pattern, 3));
        var result2 = detector.Detect(text2);
        Assert.True(result2.IsLoopDetected);
        Assert.Equal(2, result2.LoopTriggerCount);
    }

    [Fact]
    public void Detect_MultipleTriggers_TriggerCountIncrements()
    {
        var detector = new OutputLoopDetector(minPatternLength: 5, checkInterval: 1, cooldownChars: 0, requiredRepeats: 3);
        var pattern = "这是正确的代码实现。";
        var text1 = "前置内容" + string.Concat(Enumerable.Repeat(pattern, 3));

        var result1 = detector.Detect(text1);
        Assert.Equal(1, result1.LoopTriggerCount);

        var text2 = text1 + string.Concat(Enumerable.Repeat(pattern, 3));
        var result2 = detector.Detect(text2);
        Assert.Equal(2, result2.LoopTriggerCount);

        var text3 = text2 + string.Concat(Enumerable.Repeat(pattern, 3));
        var result3 = detector.Detect(text3);
        Assert.Equal(3, result3.LoopTriggerCount);
    }

    [Fact]
    public void Reset_ClearsTriggerCountAndCooldown()
    {
        var detector = new OutputLoopDetector(minPatternLength: 5, checkInterval: 1, cooldownChars: 500, requiredRepeats: 3);
        var pattern = "这是正确的代码实现。";
        var text = "前置内容" + string.Concat(Enumerable.Repeat(pattern, 3));

        var result1 = detector.Detect(text);
        Assert.True(result1.IsLoopDetected);

        detector.Reset();

        var result2 = detector.Detect(text);
        Assert.True(result2.IsLoopDetected);
        Assert.Equal(1, result2.LoopTriggerCount);
    }

    [Fact]
    public void NoLoop_StaticProperty_HasDefaultTriggerCount()
    {
        Assert.Equal(0, LoopDetectionResult.NoLoop.LoopTriggerCount);
    }
}
