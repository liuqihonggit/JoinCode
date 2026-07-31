namespace Infra.Tests.Utils.Resilience;

public sealed class UnifiedCircuitBreakerTests
{
    [Fact]
    public void Constructor_ValidatesName()
    {
        Assert.Throws<ArgumentException>(() => new UnifiedCircuitBreaker(""));
        Assert.Throws<ArgumentNullException>(() => new UnifiedCircuitBreaker(null!));
    }

    [Fact]
    public void Constructor_ValidatesFailureThreshold()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new UnifiedCircuitBreaker("test", failureThreshold: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UnifiedCircuitBreaker("test", failureThreshold: -1));
    }

    [Fact]
    public void InitialState_IsClosed()
    {
        var cb = new UnifiedCircuitBreaker("test");
        cb.Phase.Should().Be(CircuitBreakerPhase.Closed);
        cb.ConsecutiveFailures.Should().Be(0);
        cb.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void RecordSuccess_ResetsConsecutiveFailures()
    {
        var cb = new UnifiedCircuitBreaker("test", failureThreshold: 3);
        cb.RecordFailure();
        cb.RecordFailure();
        cb.ConsecutiveFailures.Should().Be(2);

        cb.RecordSuccess();
        cb.ConsecutiveFailures.Should().Be(0);
        cb.Phase.Should().Be(CircuitBreakerPhase.Closed);
    }

    [Fact]
    public void RecordFailure_ReachesThreshold_OpensCircuit()
    {
        var cb = new UnifiedCircuitBreaker("test", failureThreshold: 3);

        cb.RecordFailure();
        cb.Phase.Should().Be(CircuitBreakerPhase.Closed);
        cb.RecordFailure();
        cb.Phase.Should().Be(CircuitBreakerPhase.Closed);
        cb.RecordFailure();
        cb.Phase.Should().Be(CircuitBreakerPhase.Open);
        cb.IsOpen.Should().BeTrue();
    }

    [Fact]
    public void TryProbe_WhenClosed_ReturnsTrue()
    {
        var cb = new UnifiedCircuitBreaker("test");
        cb.TryProbe().Should().BeTrue();
    }

    [Fact]
    public void TryProbe_WhenOpen_ReturnsFalse()
    {
        var cb = new UnifiedCircuitBreaker("test", failureThreshold: 1);
        cb.RecordFailure();
        cb.Phase.Should().Be(CircuitBreakerPhase.Open);

        cb.TryProbe().Should().BeFalse();
    }

    [Fact]
    public async Task TryProbe_WhenHalfOpen_AllowsOneProbe()
    {
        var cb = new UnifiedCircuitBreaker("test", failureThreshold: 1, openDuration: TimeSpan.FromMilliseconds(1));
        cb.RecordFailure();
        cb.Phase.Should().Be(CircuitBreakerPhase.Open);

        await Task.Delay(2);

        cb.Phase.Should().Be(CircuitBreakerPhase.HalfOpen);
        cb.TryProbe().Should().BeTrue();
        cb.TryProbe().Should().BeFalse();
    }

    [Fact]
    public async Task HalfOpen_RecordFailure_ReturnsToOpen()
    {
        var cb = new UnifiedCircuitBreaker("test", failureThreshold: 1, openDuration: TimeSpan.FromMilliseconds(1));
        cb.RecordFailure();

        await Task.Delay(2);
        cb.Phase.Should().Be(CircuitBreakerPhase.HalfOpen);

        cb.RecordFailure();
        cb.Phase.Should().Be(CircuitBreakerPhase.Open);
    }

    [Fact]
    public async Task HalfOpen_RecordSuccess_ReturnsToClosed()
    {
        var cb = new UnifiedCircuitBreaker("test", failureThreshold: 1, openDuration: TimeSpan.FromMilliseconds(1));
        cb.RecordFailure();

        await Task.Delay(2);
        cb.Phase.Should().Be(CircuitBreakerPhase.HalfOpen);

        cb.RecordSuccess();
        cb.Phase.Should().Be(CircuitBreakerPhase.Closed);
        cb.ConsecutiveFailures.Should().Be(0);
    }

    [Fact]
    public void Reset_ReturnsToClosed()
    {
        var cb = new UnifiedCircuitBreaker("test", failureThreshold: 1);
        cb.RecordFailure();
        cb.Phase.Should().Be(CircuitBreakerPhase.Open);

        cb.Reset();
        cb.Phase.Should().Be(CircuitBreakerPhase.Closed);
        cb.ConsecutiveFailures.Should().Be(0);
    }

    [Fact]
    public void TotalFailures_AccumulatesAcrossResets()
    {
        var cb = new UnifiedCircuitBreaker("test", failureThreshold: 2);

        cb.RecordFailure();
        cb.RecordFailure();
        cb.RecordSuccess();
        cb.RecordFailure();

        cb.TotalFailures.Should().Be(3);
        cb.TotalSuccesses.Should().Be(1);
    }

    [Fact]
    public void OpenedAt_SetWhenOpens()
    {
        var cb = new UnifiedCircuitBreaker("test", failureThreshold: 1);
        cb.OpenedAt.Should().BeNull();

        cb.RecordFailure();
        cb.OpenedAt.Should().NotBeNull();
    }

    [Fact]
    public void FromCircuitBreakerConfig()
    {
        var config = new CircuitBreakerConfig { FailureThreshold = 7, OpenDuration = TimeSpan.FromSeconds(60) };
        var cb = new UnifiedCircuitBreaker("test", config);
        cb.Name.Should().Be("test");

        for (var i = 0; i < 6; i++) cb.RecordFailure();
        cb.Phase.Should().Be(CircuitBreakerPhase.Closed);

        cb.RecordFailure();
        cb.Phase.Should().Be(CircuitBreakerPhase.Open);
    }
}
