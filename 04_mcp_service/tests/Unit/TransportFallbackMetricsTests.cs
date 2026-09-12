namespace Mcp.Tests;

public sealed class TransportFallbackMetricsTests
{
    [Fact]
    public void RecordConnection_IncrementsAttemptsAndSuccesses()
    {
        var metrics = new TransportFallbackMetrics(3);
        metrics.RecordConnection(0);
        var snapshot = metrics.GetSnapshot();
        snapshot.ConnectionAttempts[0].Should().Be(1);
        snapshot.ConnectionSuccesses[0].Should().Be(1);
    }

    [Fact]
    public void RecordFailure_IncrementsAttemptsAndFailures()
    {
        var metrics = new TransportFallbackMetrics(3);
        metrics.RecordFailure(1);
        var snapshot = metrics.GetSnapshot();
        snapshot.ConnectionAttempts[1].Should().Be(1);
        snapshot.ConnectionFailures[1].Should().Be(1);
    }

    [Fact]
    public void RecordFallback_IncrementsTotalFallbacks()
    {
        var metrics = new TransportFallbackMetrics(3);
        metrics.RecordFallback(0, 1, 500);
        metrics.RecordFallback(1, 2, 300);
        var snapshot = metrics.GetSnapshot();
        snapshot.TotalFallbacks.Should().Be(2);
        snapshot.AverageFallbackDurationMs.Should().Be(400);
    }

    [Fact]
    public void GetSnapshot_ContainsCorrectTransportCount()
    {
        var metrics = new TransportFallbackMetrics(4);
        var snapshot = metrics.GetSnapshot();
        snapshot.ConnectionAttempts.Length.Should().Be(4);
        snapshot.ConnectionSuccesses.Length.Should().Be(4);
        snapshot.ConnectionFailures.Length.Should().Be(4);
    }

    [Fact]
    public void InvalidIndex_Throws()
    {
        var metrics = new TransportFallbackMetrics(2);
        Assert.Throws<ArgumentOutOfRangeException>(() => metrics.RecordConnection(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => metrics.RecordFailure(-1));
    }

    [Fact]
    public void ZeroTransportCount_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TransportFallbackMetrics(0));
    }
}
