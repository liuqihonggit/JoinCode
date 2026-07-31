namespace Mcp.Tests;

public sealed class TransportCircuitBreakerTests
{
    [Fact]
    public void InitialState_IsClosed()
    {
        var cb = new TransportCircuitBreaker();
        cb.State.Should().Be(CircuitBreakerState.Closed);
        cb.IsOpen.Should().BeFalse();
        cb.ConsecutiveFailures.Should().Be(0);
    }

    [Fact]
    public void RecordSuccess_ResetsFailures()
    {
        var cb = new TransportCircuitBreaker();
        cb.RecordFailure();
        cb.RecordFailure();
        cb.RecordSuccess();
        cb.ConsecutiveFailures.Should().Be(0);
        cb.State.Should().Be(CircuitBreakerState.Closed);
    }

    [Fact]
    public void RecordFailure_ReachesThreshold_OpensCircuit()
    {
        var cb = new TransportCircuitBreaker(failureThreshold: 3, coolDownMs: 60000);
        cb.RecordFailure();
        cb.State.Should().Be(CircuitBreakerState.Closed);
        cb.RecordFailure();
        cb.State.Should().Be(CircuitBreakerState.Closed);
        cb.RecordFailure();
        cb.State.Should().Be(CircuitBreakerState.Open);
        cb.IsOpen.Should().BeTrue();
    }

    [Fact]
    public async Task TryProbe_BeforeCoolDown_ReturnsFalse()
    {
        var cb = new TransportCircuitBreaker(failureThreshold: 1, coolDownMs: 60000);
        cb.RecordFailure();
        cb.IsOpen.Should().BeTrue();
        cb.TryProbe().Should().BeFalse();
        await Task.CompletedTask;
    }

    [Fact]
    public async Task TryProbe_AfterCoolDown_ReturnsTrue_AndTransitionsToHalfOpen()
    {
        var cb = new TransportCircuitBreaker(failureThreshold: 1, coolDownMs: 1);
        cb.RecordFailure();
        await Task.Delay(10);
        cb.TryProbe().Should().BeTrue();
        cb.State.Should().Be(CircuitBreakerState.HalfOpen);
    }

    [Fact]
    public async Task RecordSuccess_InHalfOpen_ClosesCircuit()
    {
        var cb = new TransportCircuitBreaker(failureThreshold: 1, coolDownMs: 1);
        cb.RecordFailure();
        await Task.Delay(10);
        cb.TryProbe();
        cb.RecordSuccess();
        cb.State.Should().Be(CircuitBreakerState.Closed);
        cb.ConsecutiveFailures.Should().Be(0);
    }

    [Fact]
    public async Task RecordFailure_InHalfOpen_OpensCircuitAgain()
    {
        var cb = new TransportCircuitBreaker(failureThreshold: 2, coolDownMs: 1);
        cb.RecordFailure();
        cb.RecordFailure();
        cb.State.Should().Be(CircuitBreakerState.Open);
        await Task.Delay(10);
        cb.TryProbe();
        cb.State.Should().Be(CircuitBreakerState.HalfOpen);
        cb.RecordFailure();
        cb.State.Should().Be(CircuitBreakerState.Open);
    }

    [Fact]
    public void OpenedAt_IsSet_WhenCircuitOpens()
    {
        var cb = new TransportCircuitBreaker(failureThreshold: 1, coolDownMs: 60000);
        cb.OpenedAt.Should().BeNull();
        cb.RecordFailure();
        cb.OpenedAt.Should().NotBeNull();
    }

    [Fact]
    public void InvalidThreshold_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TransportCircuitBreaker(failureThreshold: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TransportCircuitBreaker(coolDownMs: 0));
    }
}
