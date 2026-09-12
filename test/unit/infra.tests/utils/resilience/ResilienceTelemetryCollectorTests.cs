namespace Infra.Tests.Utils.Resilience;

public sealed class ResilienceTelemetryCollectorTests
{
    [Fact]
    public void Collect_WithNoProvider_ReturnsEmpty()
    {
        var collector = new ResilienceTelemetryCollector();
        var report = collector.Collect();

        report.HttpEndpoints.Should().BeEmpty();
        report.Subprocesses.Should().BeEmpty();
    }

    [Fact]
    public void Collect_WithResilientProvider_ReturnsCircuitBreakerStatus()
    {
        var mockInner = new Mock<IHttpClientProvider>();
        mockInner.Setup(x => x.GetClient()).Returns(new HttpClient());

        var provider = new ResilientHttpClientProvider(mockInner.Object, policy: new ResiliencePolicy
        {
            Name = "test-endpoint",
            OperationTimeout = TimeSpan.FromSeconds(5),
            CircuitBreaker = new CircuitBreakerConfig { FailureThreshold = 3, OpenDuration = TimeSpan.FromSeconds(30) },
        });

        var collector = new ResilienceTelemetryCollector(httpClientProvider: provider);
        var report = collector.Collect();

        report.HttpEndpoints.Should().ContainKey("test-endpoint");
        var status = report.HttpEndpoints["test-endpoint"];
        status.Name.Should().Be("test-endpoint");
        status.CircuitBreakerState.Should().Be(CircuitBreakerPhase.Closed);
        status.ConsecutiveFailures.Should().Be(0);
        status.TotalFailures.Should().Be(0);
        status.TotalSuccesses.Should().Be(0);
    }

    [Fact]
    public void Format_EmptyReport_ReturnsNoEndpointsMessage()
    {
        var report = ResilienceTelemetryReport.Empty;
        var text = ResilienceTelemetryCollector.Format(report);

        text.Should().Contain("无韧性端点注册");
    }

    [Fact]
    public void Format_WithHttpEndpoint_ContainsEndpointInfo()
    {
        var httpDict = new Dictionary<string, HttpResilienceStatus>
        {
            ["test"] = new HttpResilienceStatus
            {
                Name = "test",
                CircuitBreakerState = CircuitBreakerPhase.Closed,
                ConsecutiveFailures = 0,
                TotalFailures = 0,
                TotalSuccesses = 5,
                LastFailureTime = null,
                OpenedAt = null,
            }
        };
        var report = new ResilienceTelemetryReport
        {
            HttpEndpoints = httpDict,
            Subprocesses = new Dictionary<string, SubprocessResilienceStatus>(),
        };

        var text = ResilienceTelemetryCollector.Format(report);
        text.Should().Contain("test");
        text.Should().Contain("Closed");
    }

    [Fact]
    public void Collect_AfterFailures_RecordsFailureCount()
    {
        var mockInner = new Mock<IHttpClientProvider>();
        mockInner.Setup(x => x.GetClient()).Returns(new HttpClient());

        var provider = new ResilientHttpClientProvider(mockInner.Object, policy: new ResiliencePolicy
        {
            Name = "failing-endpoint",
            OperationTimeout = TimeSpan.FromMilliseconds(100),
            CircuitBreaker = new CircuitBreakerConfig { FailureThreshold = 5, OpenDuration = TimeSpan.FromSeconds(30) },
        });

        var cb = provider.Executor.CircuitBreaker!;
        cb.RecordFailure();
        cb.RecordFailure();

        var collector = new ResilienceTelemetryCollector(httpClientProvider: provider);
        var report = collector.Collect();

        report.HttpEndpoints["failing-endpoint"].ConsecutiveFailures.Should().Be(2);
        report.HttpEndpoints["failing-endpoint"].TotalFailures.Should().Be(2);
        report.HttpEndpoints["failing-endpoint"].LastFailureTime.Should().NotBeNull();
    }
}
