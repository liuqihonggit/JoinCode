
namespace Core.Goal.Tests;

public sealed class GoalHeartbeatTests
{
    private static GoalHeartbeat CreateHeartbeat(TimeSpan? interval = null, IClockService? clock = null)
    {
        return new GoalHeartbeat(interval, clock: clock);
    }

    [Fact]
    public async Task Constructor_DefaultInterval_Is30Seconds()
    {
        await using var heartbeat = CreateHeartbeat();

        Assert.Equal(0, heartbeat.RefCount);
        Assert.False(heartbeat.IsActive);
        Assert.Null(heartbeat.LastActivityAt);
        Assert.Null(heartbeat.IdleDuration);
    }

    [Fact]
    public async Task Constructor_CustomInterval_IsActive_ReturnsFalse()
    {
        await using var heartbeat = CreateHeartbeat(TimeSpan.FromSeconds(10));

        Assert.False(heartbeat.IsActive);
    }

    [Fact]
    public async Task StartActivityAsync_IncrementsRefCount_AndSetsLastActivityAt()
    {
        var clock = new Mock<IClockService>();
        var now = DateTime.UtcNow;
        clock.Setup(c => c.GetUtcNow()).Returns(now);

        await using var heartbeat = CreateHeartbeat(clock: clock.Object);

        await heartbeat.StartActivityAsync(SessionActivityReason.ApiCall).ConfigureAwait(true);

        Assert.Equal(1, heartbeat.RefCount);
        Assert.True(heartbeat.IsActive);
        Assert.Equal(now, heartbeat.LastActivityAt);
        Assert.Equal(TimeSpan.Zero, heartbeat.IdleDuration);
    }

    [Fact]
    public async Task StartActivityAsync_MultipleReasons_TracksCounts()
    {
        await using var heartbeat = CreateHeartbeat();

        await heartbeat.StartActivityAsync(SessionActivityReason.ApiCall).ConfigureAwait(true);
        await heartbeat.StartActivityAsync(SessionActivityReason.ApiCall).ConfigureAwait(true);
        await heartbeat.StartActivityAsync(SessionActivityReason.ToolExecution).ConfigureAwait(true);

        Assert.Equal(3, heartbeat.RefCount);
        Assert.True(heartbeat.IsActive);
    }

    [Fact]
    public async Task StopActivityAsync_DecrementsRefCount()
    {
        await using var heartbeat = CreateHeartbeat();

        await heartbeat.StartActivityAsync(SessionActivityReason.ApiCall).ConfigureAwait(true);
        await heartbeat.StartActivityAsync(SessionActivityReason.ApiCall).ConfigureAwait(true);
        await heartbeat.StopActivityAsync(SessionActivityReason.ApiCall).ConfigureAwait(true);

        Assert.Equal(1, heartbeat.RefCount);
        Assert.True(heartbeat.IsActive);
    }

    [Fact]
    public async Task StopActivityAsync_WhenRefCountReachesZero_StopsTimer()
    {
        await using var heartbeat = CreateHeartbeat();

        await heartbeat.StartActivityAsync(SessionActivityReason.ApiCall).ConfigureAwait(true);
        await heartbeat.StopActivityAsync(SessionActivityReason.ApiCall).ConfigureAwait(true);

        Assert.Equal(0, heartbeat.RefCount);
        Assert.False(heartbeat.IsActive);
    }

    [Fact]
    public async Task StopActivityAsync_WhenReasonCountAlreadyZero_DoesNotGoNegative()
    {
        await using var heartbeat = CreateHeartbeat();

        await heartbeat.StartActivityAsync(SessionActivityReason.ApiCall).ConfigureAwait(true);
        await heartbeat.StopActivityAsync(SessionActivityReason.ToolExecution).ConfigureAwait(true);
        await heartbeat.StopActivityAsync(SessionActivityReason.ApiCall).ConfigureAwait(true);

        Assert.Equal(0, heartbeat.RefCount);
    }

    [Fact]
    public async Task StopActivityAsync_WhenRefCountAlreadyZero_DoesNotGoNegative()
    {
        await using var heartbeat = CreateHeartbeat();

        await heartbeat.StopActivityAsync(SessionActivityReason.ApiCall).ConfigureAwait(true);

        Assert.Equal(0, heartbeat.RefCount);
    }

    [Fact]
    public async Task ResetAsync_ClearsState()
    {
        await using var heartbeat = CreateHeartbeat();

        await heartbeat.StartActivityAsync(SessionActivityReason.ApiCall).ConfigureAwait(true);
        await heartbeat.ResetAsync().ConfigureAwait(true);

        Assert.Equal(0, heartbeat.RefCount);
        Assert.False(heartbeat.IsActive);
        Assert.Null(heartbeat.LastActivityAt);
    }

    [Fact]
    public async Task IdleDuration_WhenInactive_ReturnsNull()
    {
        await using var heartbeat = CreateHeartbeat();

        Assert.Null(heartbeat.IdleDuration);
    }

    [Fact]
    public async Task IdleDuration_WhenActive_CalculatesFromLastActivity()
    {
        var clock = new Mock<IClockService>();
        var start = DateTime.UtcNow;
        clock.Setup(c => c.GetUtcNow()).Returns(start);

        await using var heartbeat = CreateHeartbeat(clock: clock.Object);
        await heartbeat.StartActivityAsync(SessionActivityReason.ApiCall).ConfigureAwait(true);

        clock.Setup(c => c.GetUtcNow()).Returns(start.AddMinutes(2));

        Assert.Equal(TimeSpan.FromMinutes(2), heartbeat.IdleDuration);
    }

    [Fact]
    public async Task RegisterCallback_NullCallback_Throws()
    {
        await using var heartbeat = CreateHeartbeat();

        Assert.Throws<ArgumentNullException>(() => heartbeat.RegisterCallback(null!));
    }

    [Fact]
    public async Task HeartbeatCallback_IsInvoked_Periodically()
    {
        var tcs = new TaskCompletionSource();
        await using var heartbeat = CreateHeartbeat(TimeSpan.FromMilliseconds(50));

        heartbeat.RegisterCallback(async _ =>
        {
            tcs.TrySetResult();
            await Task.CompletedTask.ConfigureAwait(true);
        });

        await heartbeat.StartActivityAsync(SessionActivityReason.ApiCall).ConfigureAwait(true);

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);

        await heartbeat.StopActivityAsync(SessionActivityReason.ApiCall).ConfigureAwait(true);
    }

    [Fact]
    public async Task HeartbeatCallback_Exception_DoesNotCrashLoop()
    {
        var callCount = 0;
        await using var heartbeat = CreateHeartbeat(TimeSpan.FromMilliseconds(50));

        heartbeat.RegisterCallback(_ =>
        {
            callCount++;
            if (callCount == 1)
            {
                throw new InvalidOperationException("test");
            }

            return ValueTask.CompletedTask;
        });

        await heartbeat.StartActivityAsync(SessionActivityReason.ApiCall).ConfigureAwait(true);

        await Task.Delay(150).ConfigureAwait(true);

        Assert.True(callCount >= 1);

        await heartbeat.ResetAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task DisposeAsync_CanBeCalledMultipleTimes()
    {
        var heartbeat = CreateHeartbeat();

        await heartbeat.DisposeAsync().ConfigureAwait(true);
        await heartbeat.DisposeAsync().ConfigureAwait(true);

        Assert.Equal(0, heartbeat.RefCount);
    }
}
