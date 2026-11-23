namespace Core.Context;

public sealed class InformationEntropyGuardianTests {
    private readonly InformationEntropyGuardian _sut = new(
        outputLoopDetector: new OutputLoopDetector(
            minPatternLength: 5, checkInterval: 1, requiredRepeats: 3, cooldownChars: 0),
        logicFingerprintDetector: new LogicFingerprintDetector(
            fingerprintPrefixLen: 50, fingerprintSuffixLen: 50, windowSize: 5, hitThreshold: 3),
        toolCallSequenceDetector: new ToolCallSequenceDetector(
            windowSize: 6, minPatternLength: 2, requiredRepeats: 3),
        shannonEntropyDetector: new ShannonEntropyDetector(
            windowSize: 10, declineThreshold: 4, minEntropyDelta: 0.05,
            confirmationWindow: TimeSpan.FromSeconds(5)));

    [Fact]
    public void Detect_OutputLoopTriggered_ReturnsLoopResult() {
        var pattern = "这是重复的输出内容。";
        var text = "前置内容" + string.Concat(Enumerable.Repeat(pattern, 3));

        var result = _sut.Detect(text);

        Assert.True(result.IsLoopDetected);
    }

    [Fact]
    public void Detect_NoLoop_ReturnsNoLoop() {
        var text = "这是一段正常的文本，没有重复模式。";

        var result = _sut.Detect(text);

        Assert.False(result.IsLoopDetected);
    }

    [Fact]
    public void Detect_EmptyText_ReturnsNoLoop() {
        var result = _sut.Detect(string.Empty);

        Assert.False(result.IsLoopDetected);
    }

    [Fact]
    public async Task CheckTextLoop_LogicFingerprintTriggered_ReturnsInterventionResult() {
        await using var guardian = new InformationEntropyGuardian(
            logicFingerprintDetector: new LogicFingerprintDetector(
                fingerprintPrefixLen: 50, fingerprintSuffixLen: 50, windowSize: 5, hitThreshold: 3));

        var longText = new string('A', 50) + "中间不同内容" + new string('B', 50);

        guardian.TryGetTextLoop(longText, out _);
        guardian.TryGetTextLoop(longText, out _);

        var detected = guardian.TryGetTextLoop(longText, out var result);

        Assert.True(detected);
        Assert.NotNull(result);
    }

    [Fact]
    public void CheckTextLoop_NoLoop_ReturnsNull() {
        var text = "这是一段正常的文本。";

        var detected = _sut.TryGetTextLoop(text, out var result);

        Assert.False(detected);
        Assert.Null(result);
    }

    [Fact]
    public async Task CheckToolCallLoop_RepeatedPattern_ReturnsInterventionResult() {
        await using var guardian = new InformationEntropyGuardian(
            toolCallSequenceDetector: new ToolCallSequenceDetector(
                windowSize: 6, minPatternLength: 2, requiredRepeats: 3));

        using var doc1 = JsonDocument.Parse("\"test.cs\"");
        using var doc2 = JsonDocument.Parse("\"TODO\"");

        for (var i = 0; i < 6; i++) {
            guardian.TryGetToolCallLoop("Read", new Dictionary<string, JsonElement> {
                ["file_path"] = doc1.RootElement.Clone()
            }, out _);
            guardian.TryGetToolCallLoop("Grep", new Dictionary<string, JsonElement> {
                ["pattern"] = doc2.RootElement.Clone()
            }, out _);
        }

        var detected = guardian.TryGetToolCallLoop("Read", new Dictionary<string, JsonElement> {
            ["file_path"] = doc1.RootElement.Clone()
        }, out var result);

        Assert.True(detected);
        Assert.NotNull(result);
    }

    [Fact]
    public void CheckToolCallLoop_NoLoop_ReturnsNull() {
        var detected = _sut.TryGetToolCallLoop("Read", null, out var result);

        Assert.False(detected);
        Assert.Null(result);
    }

    [Fact]
    public async Task Reset_ClearsAllDetectorState() {
        await using var guardian = new InformationEntropyGuardian(
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
    public async Task Detect_OutputLoopPriority_WhenBothTrigger_OutputLoopWins() {
        await using var guardian = new InformationEntropyGuardian(
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
    public async Task CheckTextLoop_OutputLoopAlsoChecked_WhenTextHasRepetition() {
        await using var guardian = new InformationEntropyGuardian(
            outputLoopDetector: new OutputLoopDetector(
                minPatternLength: 5, checkInterval: 1, requiredRepeats: 3, cooldownChars: 0));

        var pattern = "重复的输出内容段落。";
        var text = "前置" + string.Concat(Enumerable.Repeat(pattern, 3));

        var detected = guardian.TryGetTextLoop(text, out var result);

        Assert.True(detected);
        var actual = result ?? throw new InvalidOperationException("应检测到输出文本循环");
        Assert.Contains("输出文本循环", actual.Reason);
    }

    [Fact]
    public void Implements_IOutputLoopDetector() {
        Assert.IsAssignableFrom<IOutputLoopDetector>(_sut);
    }

    [Fact]
    public void Implements_ILoopDetectionStrategy() {
        Assert.IsAssignableFrom<ILoopDetectionStrategy>(_sut);
    }

    [Fact]
    public async Task CheckToolCallLoop_WithArguments_ExtractsFingerprint() {
        await using var guardian = new InformationEntropyGuardian(
            toolCallSequenceDetector: new ToolCallSequenceDetector(
                windowSize: 6, minPatternLength: 2, requiredRepeats: 3));

        using var doc = JsonDocument.Parse("\"/path/to/file.cs\"");
        var args = new Dictionary<string, JsonElement> {
            ["file_path"] = doc.RootElement.Clone()
        };

        var detected = guardian.TryGetToolCallLoop("Read", args, out var result);

        Assert.False(detected);
        Assert.Null(result);
    }

    [Fact]
    public void CheckToolCallLoop_NullArguments_DoesNotThrow() {
        var detected = _sut.TryGetToolCallLoop("Read", null, out var result);

        Assert.False(detected);
        Assert.Null(result);
    }

    [Fact]
    public async Task CheckTextLoop_ShannonEntropyTriggered_ReturnsInterventionResult() {
        await using var guardian = new InformationEntropyGuardian(
            outputLoopDetector: new OutputLoopDetector(
                minPatternLength: 100, checkInterval: 100, requiredRepeats: 100, cooldownChars: 0),
            shannonEntropyDetector: new ShannonEntropyDetector(
                windowSize: 10, declineThreshold: 3, minEntropyDelta: 0.001,
                confirmationWindow: TimeSpan.FromSeconds(5)));

        var high = string.Concat(Enumerable.Range(0, 26).SelectMany(i => new string((char)('a' + i), 4)));
        var medium = string.Concat(Enumerable.Range(0, 5).SelectMany(i => new string((char)('a' + i), 8)));
        var low = new string('a', 30) + new string('b', 10);
        var veryLow = new string('a', 90) + new string('b', 10);
        var evenLower = new string('a', 500) + new string('b', 10);

        guardian.TryGetTextLoop(high, out _);
        guardian.TryGetTextLoop(medium, out _);
        guardian.TryGetTextLoop(low, out _);
        guardian.TryGetTextLoop(veryLow, out _);

        var detected = guardian.TryGetTextLoop(evenLower, out var result);

        Assert.True(detected);
        var actual = result ?? throw new InvalidOperationException("应检测到信息熵减循环");
        Assert.Contains("信息熵减", actual.Reason);
    }

    [Fact]
    public async Task Reset_ClearsShannonEntropyState() {
        await using var guardian = new InformationEntropyGuardian(
            shannonEntropyDetector: new ShannonEntropyDetector(
                windowSize: 10, declineThreshold: 3, minEntropyDelta: 0.001,
                confirmationWindow: TimeSpan.FromSeconds(5)));

        var high = string.Concat(Enumerable.Range(0, 26).SelectMany(i => new string((char)('a' + i), 4)));
        var medium = string.Concat(Enumerable.Range(0, 5).SelectMany(i => new string((char)('a' + i), 8)));
        var low = new string('a', 30) + new string('b', 10);

        guardian.TryGetTextLoop(high, out _);
        guardian.TryGetTextLoop(medium, out _);
        guardian.TryGetTextLoop(low, out _);

        guardian.Reset();

        var detected = guardian.TryGetTextLoop(low, out var result);
        Assert.False(detected);
        Assert.Null(result);
    }
}