using Api.LLM.Fallback;
using System.Runtime.CompilerServices;

namespace Llm.Tests.Adapters.Fallback;

public class StreamIdleWatchdogTests
{
    [Fact]
    public void Constructor_WithDefaultParameters_CreatesWatchdog()
    {
        using var watchdog = new StreamIdleWatchdog(90_000, CancellationToken.None);

        watchdog.WasIdleAborted.Should().BeFalse();
        watchdog.ReceivedAnyChunk.Should().BeFalse();
        watchdog.CombinedToken.Should().NotBe(CancellationToken.None);
    }

    [Fact]
    public void Constructor_WhenDisabled_CombinedTokenEqualsOriginal()
    {
        using var cts = new CancellationTokenSource();
        using var watchdog = new StreamIdleWatchdog(90_000, cts.Token, enabled: false);

        watchdog.CombinedToken.Should().Be(cts.Token);
    }

    [Fact]
    public void Reset_SetsReceivedAnyChunkToTrue()
    {
        using var watchdog = new StreamIdleWatchdog(90_000, CancellationToken.None);

        watchdog.ReceivedAnyChunk.Should().BeFalse();
        watchdog.Reset();
        watchdog.ReceivedAnyChunk.Should().BeTrue();
    }

    [Fact]
    public void Constructor_WhenOriginalTokenCancelled_CombinedTokenIsCancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        using var watchdog = new StreamIdleWatchdog(90_000, cts.Token);

        watchdog.CombinedToken.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task Watchdog_TriggersIdleAbort_WhenTimeAdvances()
    {
        using var watchdog = new StreamIdleWatchdog(1, CancellationToken.None);

        using var signal = new SemaphoreSlim(0, 1);
        using var registration = watchdog.CombinedToken.Register(() => signal.Release());

        await signal.WaitAsync(TimeSpan.FromSeconds(5));

        watchdog.WasIdleAborted.Should().BeTrue();
        watchdog.CombinedToken.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task Watchdog_DoesNotTrigger_WhenResetBeforeTimeout()
    {
        using var watchdog = new StreamIdleWatchdog(5000, CancellationToken.None);

        for (var i = 0; i < 3; i++)
        {
            watchdog.Reset();
            using var barrier = new SemaphoreSlim(0, 1);
            await barrier.WaitAsync(TimeSpan.FromMilliseconds(10));
        }

        watchdog.WasIdleAborted.Should().BeFalse();
    }

    [Fact]
    public void Dispose_PreventsFurtherAborts()
    {
        var watchdog = new StreamIdleWatchdog(1, CancellationToken.None);

        watchdog.Dispose();

        watchdog.WasIdleAborted.Should().BeFalse();
    }
}
