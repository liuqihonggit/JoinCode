namespace Core.Tests.Telemetry;

public sealed class AnalyticsFileSinkTests
{
    [Fact]
    public async Task LogEvent_WithNullFileSystem_DoesNothing()
    {
        await using var sink = new AnalyticsFileSink(fileSystem: null);

        sink.LogEvent("test.event");

        Assert.Equal(0, sink.Killswitch.WrittenCount);
    }

    [Fact]
    public async Task LogEvent_WithKillswitchDisabled_DropsEvent()
    {
        var fs = new TestInMemFs();
        var killswitch = new AnalyticsSinkKillswitch();
        killswitch.SetEnabled(false);
        await using var sink = new AnalyticsFileSink(fs, killswitch);

        sink.LogEvent("test.event");

        Assert.Equal(1, killswitch.DroppedCount);
        Assert.Equal(0, killswitch.WrittenCount);
    }

    [Fact]
    public async Task LogEvent_WithEnabled_QueuesEvent()
    {
        var fs = new TestInMemFs();
        await using var sink = new AnalyticsFileSink(fs, flushInterval: TimeSpan.FromSeconds(60));

        sink.LogEvent("test.event", new Dictionary<string, string> { ["key"] = "value" }, 42.0, "session-1");

        Assert.Equal(1, sink.Killswitch.WrittenCount);
    }

    [Fact]
    public async Task FlushAsync_WritesEventsToJsonlFile()
    {
        var fs = new TestInMemFs();
        var outputDir = ".jcc/analytics-test";
        await using var sink = new AnalyticsFileSink(fs, outputDirectory: outputDir, flushInterval: TimeSpan.FromSeconds(60));

        sink.LogEvent("tool.invoked", new Dictionary<string, string> { ["tool"] = "read" }, 1.0, "sess-abc");
        sink.LogEvent("cache.break", new Dictionary<string, string> { ["reason"] = "system_prompt" });

        await sink.FlushAsync();

        var files = fs.EnumerateFiles(outputDir).ToList();
        Assert.NotEmpty(files);
    }

    [Fact]
    public async Task FlushAsync_WithNoEvents_DoesNotCreateFile()
    {
        var fs = new TestInMemFs();
        var outputDir = ".jcc/analytics-empty";
        await using var sink = new AnalyticsFileSink(fs, outputDirectory: outputDir, flushInterval: TimeSpan.FromSeconds(60));

        await sink.FlushAsync();

        Assert.False(fs.DirectoryExists(outputDir));
    }

    [Fact]
    public async Task Killswitch_SetSampleRate_Zero_DropsAll()
    {
        var fs = new TestInMemFs();
        var killswitch = new AnalyticsSinkKillswitch();
        killswitch.SetSampleRate(0.0);
        await using var sink = new AnalyticsFileSink(fs, killswitch);

        for (var i = 0; i < 100; i++)
        {
            sink.LogEvent("test.event");
        }

        Assert.Equal(100, killswitch.DroppedCount);
        Assert.Equal(0, killswitch.WrittenCount);
    }

    [Fact]
    public async Task Killswitch_SetSampleRate_One_WritesAll()
    {
        var fs = new TestInMemFs();
        var killswitch = new AnalyticsSinkKillswitch();
        killswitch.SetSampleRate(1.0);
        await using var sink = new AnalyticsFileSink(fs, killswitch, flushInterval: TimeSpan.FromSeconds(60));

        for (var i = 0; i < 50; i++)
        {
            sink.LogEvent("test.event");
        }

        Assert.Equal(50, killswitch.WrittenCount);
        Assert.Equal(0, killswitch.DroppedCount);
    }

    [Fact]
    public async Task Killswitch_SetSampleRate_ClampsToValidRange()
    {
        var killswitch = new AnalyticsSinkKillswitch();

        killswitch.SetSampleRate(-5.0);
        Assert.Equal(0.0, killswitch.SampleRate);

        killswitch.SetSampleRate(10.0);
        Assert.Equal(1.0, killswitch.SampleRate);
    }

    [Fact]
    public async Task Killswitch_SetEnabled_False_Then_True_Resumes()
    {
        var fs = new TestInMemFs();
        var killswitch = new AnalyticsSinkKillswitch();
        await using var sink = new AnalyticsFileSink(fs, killswitch, flushInterval: TimeSpan.FromSeconds(60));

        killswitch.SetEnabled(false);
        sink.LogEvent("disabled.event");
        Assert.Equal(1, killswitch.DroppedCount);

        killswitch.SetEnabled(true);
        sink.LogEvent("enabled.event");
        Assert.Equal(1, killswitch.WrittenCount);
    }

    [Fact]
    public async Task DisposeAsync_StopsFlushLoopCleanly()
    {
        var fs = new TestInMemFs();
        var sink = new AnalyticsFileSink(fs, flushInterval: TimeSpan.FromMilliseconds(100));

        sink.LogEvent("test.event");

        await sink.DisposeAsync();
    }

    [Fact]
    public async Task LogEvent_ConvenienceOverload_CreatesEventWithTimestamp()
    {
        var fs = new TestInMemFs();
        await using var sink = new AnalyticsFileSink(fs, flushInterval: TimeSpan.FromSeconds(60));

        var before = DateTimeOffset.UtcNow;
        sink.LogEvent("my.event", new Dictionary<string, string> { ["k"] = "v" }, 3.14, "s1");
        var after = DateTimeOffset.UtcNow;

        Assert.Equal(1, sink.Killswitch.WrittenCount);
    }

    [Fact]
    public async Task LogEvent_ManyEvents_DoesNotCrashWhenChannelFull()
    {
        var fs = new TestInMemFs();
        await using var sink = new AnalyticsFileSink(fs, flushInterval: TimeSpan.FromSeconds(60), batchSize: 10);

        for (var i = 0; i < 2000; i++)
        {
            sink.LogEvent($"event.{i}");
        }

        Assert.Equal(2000, sink.Killswitch.WrittenCount);
    }
}
