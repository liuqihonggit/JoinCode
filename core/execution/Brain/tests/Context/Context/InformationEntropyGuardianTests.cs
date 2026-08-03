namespace Core.Context;

public sealed class InformationEntropyGuardianTests
{
    private readonly InformationEntropyGuardian _sut = new(
        outputLoopDetector: new OutputLoopDetector(
            minPatternLength: 5, checkInterval: 1, requiredRepeats: 3, cooldownChars: 0),
        logicFingerprintDetector: new LogicFingerprintDetector(
            fingerprintPrefixLen: 50, fingerprintSuffixLen: 50, windowSize: 5, hitThreshold: 3),
        toolCallSequenceDetector: new ToolCallSequenceDetector(
            windowSize: 6, minPatternLength: 2, requiredRepeats: 3),
        shannonEntropyDetector: new ShannonEntropyDetector(
            windowSize: 10, declineThreshold: 4, minEntropyDelta: 0.05));

    [Fact]
    public void Detect_OutputLoopTriggered_ReturnsLoopResult()
    {
        var pattern = "这是重复的输出内容。";
        var text = "前置内容" + string.Concat(Enumerable.Repeat(pattern, 3));

        var result = _sut.Detect(text);

        Assert.True(result.IsLoopDetected);
    }

    [Fact]
    public void Detect_NoLoop_ReturnsNoLoop()
    {
        var text = "这是一段正常的文本，没有重复模式。";

        var result = _sut.Detect(text);

        Assert.False(result.IsLoopDetected);
    }

    [Fact]
    public void Detect_EmptyText_ReturnsNoLoop()
    {
        var result = _sut.Detect(string.Empty);

        Assert.False(result.IsLoopDetected);
    }

    [Fact]
    public void CheckTextLoop_LogicFingerprintTriggered_ReturnsInterventionResult()
    {
        var guardian = new InformationEntropyGuardian(
            logicFingerprintDetector: new LogicFingerprintDetector(
                fingerprintPrefixLen: 50, fingerprintSuffixLen: 50, windowSize: 5, hitThreshold: 3));

        var longText = new string('A', 50) + "中间不同内容" + new string('B', 50);

        guardian.CheckTextLoop(longText);
        guardian.CheckTextLoop(longText);

        var result = guardian.CheckTextLoop(longText);

        Assert.NotNull(result);
    }

    [Fact]
    public void CheckTextLoop_NoLoop_ReturnsNull()
    {
        var text = "这是一段正常的文本。";

        var result = _sut.CheckTextLoop(text);

        Assert.Null(result);
    }

    [Fact]
    public void CheckToolCallLoop_RepeatedPattern_ReturnsInterventionResult()
    {
        var guardian = new InformationEntropyGuardian(
            toolCallSequenceDetector: new ToolCallSequenceDetector(
                windowSize: 6, minPatternLength: 2, requiredRepeats: 3));

        using var doc1 = JsonDocument.Parse("\"test.cs\"");
        using var doc2 = JsonDocument.Parse("\"TODO\"");

        for (var i = 0; i < 6; i++)
        {
            guardian.CheckToolCallLoop("Read", new Dictionary<string, JsonElement>
            {
                ["file_path"] = doc1.RootElement.Clone()
            });
            guardian.CheckToolCallLoop("Grep", new Dictionary<string, JsonElement>
            {
                ["pattern"] = doc2.RootElement.Clone()
            });
        }

        var result = guardian.CheckToolCallLoop("Read", new Dictionary<string, JsonElement>
        {
            ["file_path"] = doc1.RootElement.Clone()
        });

        Assert.NotNull(result);
    }

    [Fact]
    public void CheckToolCallLoop_NoLoop_ReturnsNull()
    {
        var result = _sut.CheckToolCallLoop("Read", null);

        Assert.Null(result);
    }

    [Fact]
    public void Reset_ClearsAllDetectorState()
    {
        var guardian = new InformationEntropyGuardian(
            outputLoopDetector: new OutputLoopDetector(
                minPatternLength: 5, checkInterval: 1, requiredRepeats: 3, cooldownChars: 0));

        var pattern = "重复内容文本。";
        var text = string.Concat(Enumerable.Repeat(pattern, 3));

        var result1 = guardian.Detect(text);
        Assert.True(result1.IsLoopDetected);

        guardian.Reset();

        var shortText = "短文本";
        var result2 = guardian.Detect(shortText);
        Assert.False(result2.IsLoopDetected);
    }

    [Fact]
    public void Detect_OutputLoopPriority_WhenBothTrigger_OutputLoopWins()
    {
        var guardian = new InformationEntropyGuardian(
            outputLoopDetector: new OutputLoopDetector(
                minPatternLength: 5, checkInterval: 1, requiredRepeats: 3, cooldownChars: 0),
            logicFingerprintDetector: new LogicFingerprintDetector(
                fingerprintPrefixLen: 50, fingerprintSuffixLen: 50, windowSize: 5, hitThreshold: 3));

        var pattern = "这是重复的输出段落内容。";
        var text = "前置内容" + string.Concat(Enumerable.Repeat(pattern, 3));

        var result = guardian.Detect(text);

        Assert.True(result.IsLoopDetected);
        Assert.NotNull(result.RepeatedPattern);
    }

    [Fact]
    public void CheckTextLoop_OutputLoopAlsoChecked_WhenTextHasRepetition()
    {
        var guardian = new InformationEntropyGuardian(
            outputLoopDetector: new OutputLoopDetector(
                minPatternLength: 5, checkInterval: 1, requiredRepeats: 3, cooldownChars: 0));

        var pattern = "重复的输出内容段落。";
        var text = "前置" + string.Concat(Enumerable.Repeat(pattern, 3));

        var result = guardian.CheckTextLoop(text);

        Assert.NotNull(result);
        Assert.Contains("输出文本循环", result.Reason);
    }

    [Fact]
    public void Implements_IOutputLoopDetector()
    {
        Assert.IsAssignableFrom<IOutputLoopDetector>(_sut);
    }

    [Fact]
    public void Implements_ILoopDetectionStrategy()
    {
        Assert.IsAssignableFrom<ILoopDetectionStrategy>(_sut);
    }

    [Fact]
    public void CheckToolCallLoop_WithArguments_ExtractsFingerprint()
    {
        var guardian = new InformationEntropyGuardian(
            toolCallSequenceDetector: new ToolCallSequenceDetector(
                windowSize: 6, minPatternLength: 2, requiredRepeats: 3));

        using var doc = JsonDocument.Parse("\"/path/to/file.cs\"");
        var args = new Dictionary<string, JsonElement>
        {
            ["file_path"] = doc.RootElement.Clone()
        };

        var result = guardian.CheckToolCallLoop("Read", args);

        Assert.Null(result);
    }

    [Fact]
    public void CheckToolCallLoop_NullArguments_DoesNotThrow()
    {
        var result = _sut.CheckToolCallLoop("Read", null);

        Assert.Null(result);
    }

    [Fact]
    public void CheckTextLoop_ShannonEntropyTriggered_ReturnsInterventionResult()
    {
        var guardian = new InformationEntropyGuardian(
            outputLoopDetector: new OutputLoopDetector(
                minPatternLength: 100, checkInterval: 100, requiredRepeats: 100, cooldownChars: 0),
            shannonEntropyDetector: new ShannonEntropyDetector(
                windowSize: 10, declineThreshold: 3, minEntropyDelta: 0.001));

        var high = string.Concat(Enumerable.Range(0, 26).SelectMany(i => new string((char)('a' + i), 4)));
        var medium = string.Concat(Enumerable.Range(0, 5).SelectMany(i => new string((char)('a' + i), 8)));
        var low = new string('a', 30) + new string('b', 10);
        var veryLow = new string('a', 90) + new string('b', 10);

        guardian.CheckTextLoop(high);
        guardian.CheckTextLoop(medium);
        guardian.CheckTextLoop(low);

        var result = guardian.CheckTextLoop(veryLow);

        Assert.NotNull(result);
        Assert.Contains("信息熵减", result.Reason);
    }

    [Fact]
    public void Reset_ClearsShannonEntropyState()
    {
        var guardian = new InformationEntropyGuardian(
            shannonEntropyDetector: new ShannonEntropyDetector(
                windowSize: 10, declineThreshold: 3, minEntropyDelta: 0.001));

        var high = string.Concat(Enumerable.Range(0, 26).SelectMany(i => new string((char)('a' + i), 4)));
        var medium = string.Concat(Enumerable.Range(0, 5).SelectMany(i => new string((char)('a' + i), 8)));
        var low = new string('a', 30) + new string('b', 10);

        guardian.CheckTextLoop(high);
        guardian.CheckTextLoop(medium);
        guardian.CheckTextLoop(low);

        guardian.Reset();

        var result = guardian.CheckTextLoop(low);
        Assert.Null(result);
    }
}
