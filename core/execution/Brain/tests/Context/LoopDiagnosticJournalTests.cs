namespace Core.Context;

public sealed class LoopDiagnosticJournalTests
{
    [Fact]
    public void Record_ReturnsEntryWithTraceId()
    {
        using var journal = new LoopDiagnosticJournal();
        var entry = journal.Record("tool_start", "session1", 1, 0);

        Assert.NotNull(entry.TraceId);
        Assert.Equal(12, entry.TraceId.Length);
        Assert.Equal("tool_start", entry.EventType);
        Assert.Equal("session1", entry.SessionId);
    }

    [Fact]
    public async Task Record_WindowCountUpdated_AfterBackgroundConsumes()
    {
        using var journal = new LoopDiagnosticJournal();

        journal.Record("tool_start", "s1", 1, 0);
        journal.Record("tool_end", "s1", 1, 1);

        await WaitForWindowCountAsync(journal, 2);

        Assert.Equal(2, journal.WindowCount);
    }

    [Fact]
    public async Task Record_WindowSliding_CapacityExceeded()
    {
        using var journal = new LoopDiagnosticJournal(traceWindowCapacity: 5);

        for (var i = 0; i < 10; i++)
            journal.Record("event", "s1", 1, i);

        await WaitForWindowCountAsync(journal, 5);

        Assert.Equal(5, journal.WindowCount);
    }

    [Fact]
    public void OnLoopDetected_ReturnsAnomalyRecord()
    {
        using var journal = new LoopDiagnosticJournal();

        journal.Record("tool_start", "s1", 3, 5);
        journal.Record("tool_end", "s1", 3, 6);

        var anomaly = journal.OnLoopDetected(
            "ShannonEntropy", "s1", 3, 6, 2,
            "信息熵减循环(熵=1.234,连续下降4轮)",
            entropy: 1.234,
            textSnippet: "重复的输出内容...");

        Assert.NotNull(anomaly);
        Assert.Equal("ShannonEntropy", anomaly.DetectorLayer);
        Assert.Equal("s1", anomaly.SessionId);
        Assert.Equal(3, anomaly.ConversationTurn);
        Assert.Equal(6, anomaly.ToolCallCount);
        Assert.Equal(2, anomaly.TriggerCount);
        Assert.Equal(1.234, anomaly.Entropy);
        Assert.Equal(12, anomaly.TraceId.Length);
    }

    [Fact]
    public async Task OnLoopDetected_TraceChainPopulated_AfterBackgroundConsumes()
    {
        using var journal = new LoopDiagnosticJournal(traceWindowCapacity: 10);

        journal.Record("tool_start", "s1", 1, 0);
        journal.Record("tool_end", "s1", 1, 1);
        journal.Record("tool_start", "s1", 2, 1);

        await WaitForWindowCountAsync(journal, 3);

        journal.OnLoopDetected("OutputLoop", "s1", 2, 1, 1, "输出文本循环");

        await WaitForWindowCountAsync(journal, 4);

        Assert.True(journal.WindowCount >= 4);
    }

    [Fact]
    public void OnLoopDetected_ToDiagnosticData_ContainsAllFields()
    {
        using var journal = new LoopDiagnosticJournal();
        journal.Record("event", "s1", 1, 0);

        var anomaly = journal.OnLoopDetected(
            "LogicFingerprint", "s1", 5, 10, 3,
            "逻辑指纹循环",
            entropy: 2.5,
            textSnippet: "一些文本内容");

        var data = anomaly.ToDiagnosticData();

        Assert.Equal("LogicFingerprint", data["detector_layer"]);
        Assert.Equal("5", data["conversation_turn"]);
        Assert.Equal("10", data["tool_call_count"]);
        Assert.Equal("3", data["trigger_count"]);
        Assert.Equal("逻辑指纹循环", data["reason"]);
        Assert.Equal("2.5000", data["entropy"]);
        Assert.Equal("一些文本内容", data["text_snippet"]);
        Assert.True(data.ContainsKey("trace_chain"));
    }

    [Fact]
    public void OnLoopDetected_TextSnippet_TruncatedWhenTooLong()
    {
        using var journal = new LoopDiagnosticJournal();
        journal.Record("event", "s1", 1, 0);

        var longText = new string('A', 300);
        var anomaly = journal.OnLoopDetected(
            "OutputLoop", "s1", 1, 0, 1, "循环", textSnippet: longText);

        var data = anomaly.ToDiagnosticData();
        Assert.True(data["text_snippet"].Length <= 203);
        Assert.EndsWith("...", data["text_snippet"]);
    }

    [Fact]
    public void OnLoopDetected_NullEntropy_NotIncludedInData()
    {
        using var journal = new LoopDiagnosticJournal();
        journal.Record("event", "s1", 1, 0);

        var anomaly = journal.OnLoopDetected(
            "ToolCallSequence", "s1", 1, 0, 1, "工具循环");

        var data = anomaly.ToDiagnosticData();
        Assert.False(data.ContainsKey("entropy"));
    }

    [Fact]
    public async Task Reset_ClearsWindow()
    {
        using var journal = new LoopDiagnosticJournal();

        journal.Record("event", "s1", 1, 0);
        journal.Record("event", "s1", 2, 1);
        await WaitForWindowCountAsync(journal, 2);
        Assert.Equal(2, journal.WindowCount);

        journal.Reset();
        await WaitForWindowCountAsync(journal, 0);
        Assert.Equal(0, journal.WindowCount);
    }

    [Fact]
    public async Task OnLoopDetected_AddsAnomalyToWindow()
    {
        using var journal = new LoopDiagnosticJournal();

        journal.Record("event", "s1", 1, 0);
        journal.OnLoopDetected("OutputLoop", "s1", 1, 0, 1, "循环");

        await WaitForWindowCountAsync(journal, 2);

        Assert.Equal(2, journal.WindowCount);
    }

    private static async Task WaitForWindowCountAsync(LoopDiagnosticJournal journal, int expectedCount, int timeoutMs = 2000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (journal.WindowCount != expectedCount && sw.ElapsedMilliseconds < timeoutMs)
            await Task.Delay(10);
    }
}
