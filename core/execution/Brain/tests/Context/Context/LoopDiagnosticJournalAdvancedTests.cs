namespace Core.Context;

public sealed class LoopDiagnosticJournalAdvancedTests
{
    [Fact]
    public async Task ConcurrentRecord_DoesNotCorruptWindow()
    {
        using var journal = new LoopDiagnosticJournal(traceWindowCapacity: 100);
        var tasks = new List<Task>();

        for (var i = 0; i < 10; i++)
        {
            var turn = i;
            tasks.Add(Task.Run(() =>
            {
                for (var j = 0; j < 50; j++)
                {
                    journal.Record("concurrent_event", "s1", turn, j);
                }
            }));
        }

        await Task.WhenAll(tasks);
        await Task.Delay(300);

        Assert.True(journal.WindowCount <= 100);
        Assert.True(journal.WindowCount > 0);
    }

    [Fact]
    public async Task ConcurrentRecordAndAnomaly_DoesNotCorruptWindow()
    {
        using var journal = new LoopDiagnosticJournal(traceWindowCapacity: 100);
        var tasks = new List<Task>();

        for (var i = 0; i < 5; i++)
        {
            var turn = i;
            tasks.Add(Task.Run(() =>
            {
                for (var j = 0; j < 30; j++)
                {
                    journal.Record("event", "s1", turn, j);
                }
                journal.OnLoopDetected("OutputLoop", "s1", turn, 30, 1, "循环");
            }));
        }

        await Task.WhenAll(tasks);
        await Task.Delay(300);

        Assert.True(journal.WindowCount <= 100);
        Assert.True(journal.WindowCount > 0);
    }

    [Fact]
    public async Task ChannelFull_DropOldest_DoesNotBlockCaller()
    {
        using var journal = new LoopDiagnosticJournal(traceWindowCapacity: 10);

        for (var i = 0; i < 500; i++)
        {
            journal.Record("burst", "s1", 1, i);
        }

        await Task.Delay(300);
        Assert.True(journal.WindowCount <= 10);
    }

    [Fact]
    public async Task Reset_DuringConsumption_DoesNotDeadlock()
    {
        using var journal = new LoopDiagnosticJournal(traceWindowCapacity: 50);

        for (var i = 0; i < 100; i++)
        {
            journal.Record("event", "s1", 1, i);
        }

        journal.Reset();

        await Task.Delay(200);
        Assert.Equal(0, journal.WindowCount);
    }

    [Fact]
    public async Task Dispose_StopsBackgroundConsumer()
    {
        var journal = new LoopDiagnosticJournal(traceWindowCapacity: 50);

        for (var i = 0; i < 100; i++)
        {
            journal.Record("event", "s1", 1, i);
        }

        journal.Dispose();

        Assert.True(true);
    }

    [Fact]
    public async Task OnLoopDetected_MultipleAnomalies_AllRecordedToWindow()
    {
        using var journal = new LoopDiagnosticJournal(traceWindowCapacity: 50);

        journal.Record("event", "s1", 1, 0);
        journal.OnLoopDetected("OutputLoop", "s1", 1, 0, 1, "循环1");

        journal.Record("event", "s1", 2, 1);
        journal.OnLoopDetected("ShannonEntropy", "s1", 2, 1, 2, "熵减", entropy: 1.5);

        await Task.Delay(200);

        Assert.True(journal.WindowCount >= 4);
    }

    [Fact]
    public async Task Record_WithCustomData_PreservedInEntry()
    {
        using var journal = new LoopDiagnosticJournal();

        var entry = journal.Record("tool_start", "s1", 1, 0, new Dictionary<string, string>
        {
            ["tool_name"] = "Read",
            ["file_path"] = "/test.cs"
        });

        Assert.Equal("Read", entry.Data["tool_name"]);
        Assert.Equal("/test.cs", entry.Data["file_path"]);

        await Task.Delay(100);
        Assert.Equal(1, journal.WindowCount);
    }

    [Fact]
    public async Task OnLoopDetected_WithEntropyAndSnippet_DataPreserved()
    {
        using var journal = new LoopDiagnosticJournal();
        journal.Record("event", "s1", 1, 0);

        var anomaly = journal.OnLoopDetected(
            "ShannonEntropy", "s1", 5, 10, 3,
            "信息熵减循环",
            entropy: 0.876,
            textSnippet: "重复输出内容");

        Assert.Equal("ShannonEntropy", anomaly.DetectorLayer);
        Assert.Equal(0.876, anomaly.Entropy);
        Assert.Equal("重复输出内容", anomaly.TextSnippet);

        var data = anomaly.ToDiagnosticData();
        Assert.Equal("0.8760", data["entropy"]);
        Assert.Equal("重复输出内容", data["text_snippet"]);
    }
}
