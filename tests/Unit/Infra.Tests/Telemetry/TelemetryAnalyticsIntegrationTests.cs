namespace Core.Tests.Telemetry;

public sealed class TelemetryAnalyticsIntegrationTests
{
    [Fact]
    public async Task RecordCount_WithAnalyticsSink_WritesEventToSink()
    {
        var fs = new TestInMemFs();
        await using var sink = new AnalyticsFileSink(fs, flushInterval: TimeSpan.FromSeconds(60));
        var config = new TelemetryConfig { ServiceName = "test", MetricsEnabled = true };
        await using var telemetry = new TelemetryService(config, analyticsSink: sink);

        telemetry.RecordCount("test.metric", new Dictionary<string, string> { ["key"] = "value" });

        Assert.Equal(1, sink.Killswitch.WrittenCount);
    }

    [Fact]
    public async Task RecordCount_WithoutAnalyticsSink_DoesNotCrash()
    {
        var config = new TelemetryConfig { ServiceName = "test", MetricsEnabled = true };
        await using var telemetry = new TelemetryService(config);

        telemetry.RecordCount("test.metric");

        Assert.True(true);
    }

    [Fact]
    public async Task RecordCount_MultipleCalls_AllWrittenToSink()
    {
        var fs = new TestInMemFs();
        await using var sink = new AnalyticsFileSink(fs, flushInterval: TimeSpan.FromSeconds(60));
        var config = new TelemetryConfig { ServiceName = "test", MetricsEnabled = true };
        await using var telemetry = new TelemetryService(config, analyticsSink: sink);

        for (var i = 0; i < 10; i++)
        {
            telemetry.RecordCount("test.metric");
        }

        Assert.Equal(10, sink.Killswitch.WrittenCount);
    }

    [Fact]
    public async Task RecordCount_WithKillswitchDisabled_NotWritten()
    {
        var fs = new TestInMemFs();
        await using var sink = new AnalyticsFileSink(fs, flushInterval: TimeSpan.FromSeconds(60));
        sink.Killswitch.SetEnabled(false);
        var config = new TelemetryConfig { ServiceName = "test", MetricsEnabled = true };
        await using var telemetry = new TelemetryService(config, analyticsSink: sink);

        telemetry.RecordCount("test.metric");

        Assert.Equal(0, sink.Killswitch.WrittenCount);
        Assert.Equal(1, sink.Killswitch.DroppedCount);
    }

    [Fact]
    public async Task RecordCount_FlushAsync_WritesJsonlFile()
    {
        var fs = new TestInMemFs();
        var outputDir = ".jcc/analytics-telemetry-test";
        await using var sink = new AnalyticsFileSink(fs, outputDirectory: outputDir, flushInterval: TimeSpan.FromSeconds(60));
        var config = new TelemetryConfig { ServiceName = "test", MetricsEnabled = true };
        await using var telemetry = new TelemetryService(config, analyticsSink: sink);

        telemetry.RecordCount("test.metric", new Dictionary<string, string> { ["tool"] = "read" });

        await sink.FlushAsync();

        var files = fs.EnumerateFiles(outputDir).ToList();
        Assert.NotEmpty(files);
    }
}
