
namespace Core.Goal.Tests;

public sealed class ClusterTelemetryTests
{
    [Fact]
    public void RecordPhase_ShouldStoreMetric()
    {
        var sut = new ClusterTelemetry();
        var metric = new ClusterPhaseMetric
        {
            SessionId = "s1",
            Phase = "analyze",
            Duration = TimeSpan.FromMilliseconds(100),
            IsSuccess = true,
        };

        sut.RecordPhase(metric);

        var summary = sut.GetSummary();
        Assert.Single(summary.Phases);
        Assert.Equal("analyze", summary.Phases[0].Phase);
    }

    [Fact]
    public void GetSummary_MultiplePhases_ShouldAggregate()
    {
        var sut = new ClusterTelemetry();
        sut.RecordPhase(new ClusterPhaseMetric { SessionId = "s1", Phase = "analyze", Duration = TimeSpan.FromMilliseconds(100), IsSuccess = true });
        sut.RecordPhase(new ClusterPhaseMetric { SessionId = "s1", Phase = "worker", Duration = TimeSpan.FromMilliseconds(500), IsSuccess = true });
        sut.RecordPhase(new ClusterPhaseMetric { SessionId = "s1", Phase = "worker", Duration = TimeSpan.FromMilliseconds(600), IsSuccess = false });

        var summary = sut.GetSummary();
        Assert.Equal(3, summary.Phases.Count);
        Assert.Equal(2, summary.SuccessCount);
        Assert.Equal(1, summary.FailureCount);
        Assert.Equal(2, summary.WorkerCount);
    }

    [Fact]
    public void GetSummary_NoPhases_ShouldReturnEmpty()
    {
        var sut = new ClusterTelemetry();

        var summary = sut.GetSummary();
        Assert.Empty(summary.Phases);
        Assert.Equal(0, summary.SuccessCount);
    }

    [Fact]
    public void RecordPhase_NullMetric_ShouldThrow()
    {
        var sut = new ClusterTelemetry();
        Assert.Throws<ArgumentNullException>(() => sut.RecordPhase(null!));
    }
}
