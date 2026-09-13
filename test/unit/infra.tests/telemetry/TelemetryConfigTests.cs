
namespace Core.Tests.Telemetry;

public sealed class TelemetryConfigTests
{
    [Fact]
    public void Constructor_SetsDefaults()
    {
        var config = new TelemetryConfig();

        Assert.Equal("JoinCode", config.ServiceName);
        Assert.Equal("1.0.0", config.ServiceVersion);
        Assert.True(config.TracingEnabled);
        Assert.True(config.MetricsEnabled);
        Assert.Equal(TelemetryExportFormat.None, config.ExportFormat);
        Assert.Null(config.OtlpEndpoint);
        Assert.Equal(15, config.MetricsIntervalSeconds);
        Assert.True(config.RecordExceptions);
        Assert.Empty(config.DefaultTags);
    }

    [Fact]
    public void Constructor_WithCustomValues()
    {
        var config = new TelemetryConfig
        {
            ServiceName = "MyService",
            ServiceVersion = "2.0.0",
            TracingEnabled = false,
            MetricsEnabled = false,
            ExportFormat = TelemetryExportFormat.Otlp,
            OtlpEndpoint = "http://localhost:4317",
            MetricsIntervalSeconds = 30,
            RecordExceptions = false,
            DefaultTags = new Dictionary<string, string> { ["env"] = "test" }
        };

        Assert.Equal("MyService", config.ServiceName);
        Assert.Equal("2.0.0", config.ServiceVersion);
        Assert.False(config.TracingEnabled);
        Assert.False(config.MetricsEnabled);
        Assert.Equal(TelemetryExportFormat.Otlp, config.ExportFormat);
        Assert.Equal("http://localhost:4317", config.OtlpEndpoint);
        Assert.Equal(30, config.MetricsIntervalSeconds);
        Assert.False(config.RecordExceptions);
        Assert.Single(config.DefaultTags);
    }

    [Fact]
    public void ExportFormat_AllValues()
    {
        var values = Enum.GetValues<TelemetryExportFormat>();
        Assert.Equal(4, values.Length);
        Assert.Contains(TelemetryExportFormat.None, values);
        Assert.Contains(TelemetryExportFormat.Otlp, values);
        Assert.Contains(TelemetryExportFormat.Prometheus, values);
        Assert.Contains(TelemetryExportFormat.Console, values);
    }

    [Fact]
    public void SpanKind_AllValues()
    {
        var values = Enum.GetValues<TelemetrySpanKind>();
        Assert.Equal(5, values.Length);
    }

    [Fact]
    public void StatusCode_AllValues()
    {
        var values = Enum.GetValues<TelemetryStatusCode>();
        Assert.Equal(3, values.Length);
    }
}
