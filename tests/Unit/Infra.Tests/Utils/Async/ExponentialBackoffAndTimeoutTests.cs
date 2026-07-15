namespace Infra.Tests.Utils.Async;

using System.Collections.Frozen;

public class ExponentialBackoffTests
{
    [Fact]
    public void CalculateDelay_Attempt0_ShouldReturnBaseDelay()
    {
        var backoff = new ExponentialBackoff(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(10));
        var delay = backoff.CalculateDelay(0);
        delay.Should().Be(TimeSpan.FromMilliseconds(100));
    }

    [Fact]
    public void CalculateDelay_Attempt1_ShouldDouble()
    {
        var backoff = new ExponentialBackoff(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(10));
        var delay = backoff.CalculateDelay(1);
        delay.Should().Be(TimeSpan.FromMilliseconds(200));
    }

    [Fact]
    public void CalculateDelay_Attempt2_ShouldQuadruple()
    {
        var backoff = new ExponentialBackoff(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(10));
        var delay = backoff.CalculateDelay(2);
        delay.Should().Be(TimeSpan.FromMilliseconds(400));
    }

    [Fact]
    public void CalculateDelay_ShouldNotExceedMaxDelay()
    {
        var backoff = new ExponentialBackoff(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(300));
        backoff.CalculateDelay(0).Should().Be(TimeSpan.FromMilliseconds(100));
        backoff.CalculateDelay(1).Should().Be(TimeSpan.FromMilliseconds(200));
        backoff.CalculateDelay(2).Should().Be(TimeSpan.FromMilliseconds(300));
        backoff.CalculateDelay(10).Should().Be(TimeSpan.FromMilliseconds(300));
    }

    [Fact]
    public void Default_ShouldHaveReasonableValues()
    {
        var d = ExponentialBackoff.Default;
        d.BaseDelay.Should().Be(TimeSpan.FromMilliseconds(200));
        d.MaxDelay.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void MaxShiftBits_ShouldLimitExponent()
    {
        var backoff = new ExponentialBackoff(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(60), maxShiftBits: 3);
        backoff.CalculateDelay(0).Should().Be(TimeSpan.FromMilliseconds(100));
        backoff.CalculateDelay(3).Should().Be(TimeSpan.FromMilliseconds(800));
        backoff.CalculateDelay(4).Should().Be(TimeSpan.FromMilliseconds(800));
    }
}

public class TimeoutHelperTests
{
    [Fact]
    public async Task WithTimeoutAsync_ShouldCompleteWithinTimeout()
    {
        var result = await TimeoutHelper.WithTimeoutAsync(
            ct => Task.FromResult(42),
            TimeSpan.FromSeconds(5));
        result.Should().Be(42);
    }

    [Fact]
    public async Task WithTimeoutAsync_ShouldThrowOnTimeout()
    {
        var act = () => TimeoutHelper.WithTimeoutAsync(
            ct => Task.Delay(TimeSpan.FromSeconds(10), ct),
            TimeSpan.FromMilliseconds(50));
        await act.Should().ThrowAsync<TimeoutException>();
    }

    [Fact]
    public async Task WithTimeoutAsync_ShouldPropagateCancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var act = () => TimeoutHelper.WithTimeoutAsync(
            ct => Task.Delay(TimeSpan.FromSeconds(10), ct),
            TimeSpan.FromSeconds(5),
            cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void CreateLinkedTimeout_ShouldCreateLinkedCts()
    {
        using var parentCts = new CancellationTokenSource();
        using var linked = TimeoutHelper.CreateLinkedTimeout(parentCts.Token, TimeSpan.FromSeconds(5));
        linked.Should().NotBeNull();
        linked.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public async Task CreateLinkedTimeout_ShouldCancelAfterTimeout()
    {
        using var linked = TimeoutHelper.CreateLinkedTimeout(CancellationToken.None, TimeSpan.FromMilliseconds(50));
        await Task.Delay(100);
        linked.IsCancellationRequested.Should().BeTrue();
    }
}
