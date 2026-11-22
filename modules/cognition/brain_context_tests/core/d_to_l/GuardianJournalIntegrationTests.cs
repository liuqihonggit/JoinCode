namespace Core.Context;

public sealed class GuardianJournalIntegrationTests {
    [Fact]
    public async Task Detect_OutputLoopTriggered_JournalReceivesAnomalyCommand() {
        await using var journal = new LoopDiagnosticJournal(logger: null);
        await using var guardian = new InformationEntropyGuardian(
            outputLoopDetector: new OutputLoopDetector(
                minPatternLength: 5, checkInterval: 1, requiredRepeats: 3, cooldownChars: 0),
            journal: journal);

        guardian.SetContext("test-session", 1, 0);

        var pattern = "这是重复的输出内容。";
        var text = "前置内容" + string.Concat(Enumerable.Repeat(pattern, 3));

        var result = guardian.Detect(text);

        Assert.True(result.IsLoopDetected);

        await Task.Delay(200);

        Assert.True(journal.WindowCount >= 2);
    }

    [Fact]
    public async Task CheckTextLoop_ShannonEntropyTriggered_JournalReceivesAnomalyCommand() {
        await using var journal = new LoopDiagnosticJournal(logger: null);
        await using var guardian = new InformationEntropyGuardian(
            outputLoopDetector: new OutputLoopDetector(
                minPatternLength: 100, checkInterval: 100, requiredRepeats: 100, cooldownChars: 0),
            shannonEntropyDetector: new ShannonEntropyDetector(
                windowSize: 10, declineThreshold: 3, minEntropyDelta: 0.001,
                confirmationWindow: TimeSpan.FromSeconds(5)),
            journal: journal);

        guardian.SetContext("test-session", 1, 0);

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

        await Task.Delay(200);
        Assert.True(journal.WindowCount >= 4);
    }

    [Fact]
    public async Task CheckToolCallLoop_Triggered_JournalReceivesAnomalyCommand() {
        await using var journal = new LoopDiagnosticJournal(logger: null);
        await using var guardian = new InformationEntropyGuardian(
            toolCallSequenceDetector: new ToolCallSequenceDetector(
                windowSize: 6, minPatternLength: 2, requiredRepeats: 3),
            journal: journal);

        guardian.SetContext("test-session", 1, 0);

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

        await Task.Delay(200);
        Assert.True(journal.WindowCount >= 2);
    }

    [Fact]
    public async Task Reset_ClearsGuardianAndJournalState() {
        await using var journal = new LoopDiagnosticJournal(logger: null);
        await using var guardian = new InformationEntropyGuardian(
            outputLoopDetector: new OutputLoopDetector(
                minPatternLength: 5, checkInterval: 1, requiredRepeats: 3, cooldownChars: 0),
            journal: journal);

        guardian.SetContext("test-session", 1, 0);

        var pattern = "重复内容文本。";
        var text = string.Concat(Enumerable.Repeat(pattern, 3));

        var result1 = guardian.Detect(text);
        Assert.True(result1.IsLoopDetected);

        guardian.Reset();

        await Task.Delay(100);
        Assert.Equal(0, journal.WindowCount);

        var shortText = "短文本";
        var result2 = guardian.Detect(shortText);
        Assert.False(result2.IsLoopDetected);

    }

    [Fact]
    public async Task SetContext_UpdatesSessionAndTurnInfo() {
        await using var journal = new LoopDiagnosticJournal(logger: null);
        await using var guardian = new InformationEntropyGuardian(
            outputLoopDetector: new OutputLoopDetector(
                minPatternLength: 5, checkInterval: 1, requiredRepeats: 3, cooldownChars: 0),
            journal: journal);

        guardian.SetContext("session-A", 5, 10);

        var pattern = "重复内容文本。";
        var text = string.Concat(Enumerable.Repeat(pattern, 3));
        guardian.Detect(text);

        for (var i = 0; i < 16; i++) {
            var deadline = DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(200);
            while (DateTimeOffset.UtcNow < deadline) {
                if (journal.WindowCount >= 1) break;
                await Task.Delay(10);
            }
            if (journal.WindowCount >= 1) break;
        }
        Assert.True(journal.WindowCount >= 1);

    }

    [Fact]
    public async Task MultipleTriggers_EachRecordedToJournal() {
        await using var journal = new LoopDiagnosticJournal(traceWindowCapacity: 50, logger: null);
        await using var guardian = new InformationEntropyGuardian(
            outputLoopDetector: new OutputLoopDetector(
                minPatternLength: 5, checkInterval: 1, requiredRepeats: 3, cooldownChars: 0),
            journal: journal);

        guardian.SetContext("test-session", 1, 0);

        var pattern = "重复内容文本。";

        var text1 = string.Concat(Enumerable.Repeat(pattern, 3));
        guardian.Detect(text1);

        var text2 = text1 + string.Concat(Enumerable.Repeat(pattern, 3));
        guardian.Detect(text2);

        await Task.Delay(300);

        Assert.True(journal.WindowCount >= 4);
    }
}