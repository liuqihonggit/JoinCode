namespace Infra.Tests.Utils.Throttle;

public class FixedWindowRateLimiterTests
{
    [Fact]
    public void TryAcquire_WithinLimit_ShouldReturnTrue()
    {
        var limiter = new FixedWindowRateLimiter(3, TimeSpan.FromSeconds(1));
        limiter.TryAcquire().Should().BeTrue();
        limiter.TryAcquire().Should().BeTrue();
        limiter.TryAcquire().Should().BeTrue();
    }

    [Fact]
    public void TryAcquire_ExceedsLimit_ShouldReturnFalse()
    {
        var limiter = new FixedWindowRateLimiter(2, TimeSpan.FromSeconds(1));
        limiter.TryAcquire().Should().BeTrue();
        limiter.TryAcquire().Should().BeTrue();
        limiter.TryAcquire().Should().BeFalse();
    }

    [Fact]
    public void Reset_ShouldAllowNewRequests()
    {
        var limiter = new FixedWindowRateLimiter(1, TimeSpan.FromSeconds(1));
        limiter.TryAcquire().Should().BeTrue();
        limiter.TryAcquire().Should().BeFalse();
        limiter.Reset();
        limiter.TryAcquire().Should().BeTrue();
    }
}

public class CircuitBreakerStateTests
{
    [Fact]
    public void ShouldTrip_AfterThresholdFailures_ShouldReturnTrue()
    {
        var breaker = new CircuitBreakerState(3, TimeSpan.FromSeconds(30));
        breaker.RecordFailure();
        breaker.RecordFailure();
        breaker.ShouldTrip().Should().BeFalse();
        breaker.RecordFailure();
        breaker.ShouldTrip().Should().BeTrue();
    }

    [Fact]
    public void IsOpen_AfterTripping_ShouldBeTrue()
    {
        var breaker = new CircuitBreakerState(2, TimeSpan.FromSeconds(30));
        breaker.RecordFailure();
        breaker.RecordFailure();
        breaker.ShouldTrip().Should().BeTrue();
        breaker.IsOpen.Should().BeTrue();
    }

    [Fact]
    public void RecordSuccess_ShouldResetConsecutiveFailures()
    {
        var breaker = new CircuitBreakerState(3, TimeSpan.FromSeconds(30));
        breaker.RecordFailure();
        breaker.RecordFailure();
        breaker.RecordSuccess();
        breaker.ConsecutiveFailures.Should().Be(0);
    }

    [Fact]
    public void Reset_ShouldClearState()
    {
        var breaker = new CircuitBreakerState(2, TimeSpan.FromSeconds(30));
        breaker.RecordFailure();
        breaker.RecordFailure();
        breaker.Reset();
        breaker.ConsecutiveFailures.Should().Be(0);
        breaker.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void IsOpen_BeforeThreshold_ShouldBeFalse()
    {
        var breaker = new CircuitBreakerState(5, TimeSpan.FromSeconds(30));
        breaker.IsOpen.Should().BeFalse();
    }
}
