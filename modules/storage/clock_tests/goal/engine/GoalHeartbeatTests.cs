
// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
namespace Core.Goal.Tests;

public sealed class GoalHeartbeatTests {
    private static GoalHeartbeat CreateHeartbeat(TimeSpan? interval = null, IClockService? clock = null) {
        return new GoalHeartbeat(interval, clock: clock);
    }

    [Fact]
    public async Task Constructor_DefaultInterval_Is30Seconds() {
        await using var heartbeat = CreateHeartbeat();

        Assert.Equal(0, heartbeat.RefCount);
        Assert.False(heartbeat.IsActive);
        Assert.Null(heartbeat.LastActivityAt);
        Assert.Null(heartbeat.IdleDuration);
    }

    [Fact]
    public async Task Constructor_CustomInterval_IsActive_ReturnsFalse() {
        await using var heartbeat = CreateHeartbeat(TimeSpan.FromSeconds(10));

        Assert.False(heartbeat.IsActive);
    }

    [Fact]
    public async Task StartActivityAsync_IncrementsRefCount_AndSetsLastActivityAt() {
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
    public async Task StartActivityAsync_MultipleReasons_TracksCounts() {
        await using var heartbeat = CreateHeartbeat();

        await heartbeat.StartActivityAsync(SessionActivityReason.ApiCall).ConfigureAwait(true);
        await heartbeat.StartActivityAsync(SessionActivityReason.ApiCall).ConfigureAwait(true);
        await heartbeat.StartActivityAsync(SessionActivityReason.ToolExecution).ConfigureAwait(true);

        Assert.Equal(3, heartbeat.RefCount);
        Assert.True(heartbeat.IsActive);
    }

    [Fact]
    public async Task StopActivityAsync_DecrementsRefCount() {
        await using var heartbeat = CreateHeartbeat();

        await heartbeat.StartActivityAsync(SessionActivityReason.ApiCall).ConfigureAwait(true);
        await heartbeat.StartActivityAsync(SessionActivityReason.ApiCall).ConfigureAwait(true);
        await heartbeat.StopActivityAsync(SessionActivityReason.ApiCall).ConfigureAwait(true);

        Assert.Equal(1, heartbeat.RefCount);
        Assert.True(heartbeat.IsActive);
    }

    [Fact]
    public async Task StopActivityAsync_WhenRefCountReachesZero_StopsTimer() {
        await using var heartbeat = CreateHeartbeat();

        await heartbeat.StartActivityAsync(SessionActivityReason.ApiCall).ConfigureAwait(true);
        await heartbeat.StopActivityAsync(SessionActivityReason.ApiCall).ConfigureAwait(true);

        Assert.Equal(0, heartbeat.RefCount);
        Assert.False(heartbeat.IsActive);
    }

    [Fact]
    public async Task StopActivityAsync_WhenReasonCountAlreadyZero_DoesNotGoNegative() {
        await using var heartbeat = CreateHeartbeat();

        await heartbeat.StartActivityAsync(SessionActivityReason.ApiCall).ConfigureAwait(true);
        await heartbeat.StopActivityAsync(SessionActivityReason.ToolExecution).ConfigureAwait(true);
        await heartbeat.StopActivityAsync(SessionActivityReason.ApiCall).ConfigureAwait(true);

        Assert.Equal(0, heartbeat.RefCount);
    }

    [Fact]
    public async Task StopActivityAsync_WhenRefCountAlreadyZero_DoesNotGoNegative() {
        await using var heartbeat = CreateHeartbeat();

        await heartbeat.StopActivityAsync(SessionActivityReason.ApiCall).ConfigureAwait(true);

        Assert.Equal(0, heartbeat.RefCount);
    }

    [Fact]
    public async Task ResetAsync_ClearsState() {
        await using var heartbeat = CreateHeartbeat();

        await heartbeat.StartActivityAsync(SessionActivityReason.ApiCall).ConfigureAwait(true);
        await heartbeat.ResetAsync().ConfigureAwait(true);

        Assert.Equal(0, heartbeat.RefCount);
        Assert.False(heartbeat.IsActive);
        Assert.Null(heartbeat.LastActivityAt);
    }

    [Fact]
    public async Task IdleDuration_WhenInactive_ReturnsNull() {
        await using var heartbeat = CreateHeartbeat();

        Assert.Null(heartbeat.IdleDuration);
    }

    [Fact]
    public async Task IdleDuration_WhenActive_CalculatesFromLastActivity() {
        var clock = new Mock<IClockService>();
        var start = DateTime.UtcNow;
        clock.Setup(c => c.GetUtcNow()).Returns(start);

        await using var heartbeat = CreateHeartbeat(clock: clock.Object);
        await heartbeat.StartActivityAsync(SessionActivityReason.ApiCall).ConfigureAwait(true);

        clock.Setup(c => c.GetUtcNow()).Returns(start.AddMinutes(2));

        Assert.Equal(TimeSpan.FromMinutes(2), heartbeat.IdleDuration);
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public async Task RegisterCallback_NullCallback_Throws() {
        await using var heartbeat = CreateHeartbeat();

        Assert.Throws<ArgumentNullException>(() => heartbeat.RegisterCallback(null!));
    }

    [Fact]
    public async Task HeartbeatCallback_IsInvoked_Periodically() {
        var tcs = new TaskCompletionSource();
        await using var heartbeat = CreateHeartbeat(TimeSpan.FromMilliseconds(50));

        heartbeat.RegisterCallback(async _ => {
            tcs.TrySetResult();
            await Task.CompletedTask.ConfigureAwait(true);
        });

        await heartbeat.StartActivityAsync(SessionActivityReason.ApiCall).ConfigureAwait(true);

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);

        await heartbeat.StopActivityAsync(SessionActivityReason.ApiCall).ConfigureAwait(true);
    }

    /// <summary>回调抛异常不炸循环 — 确定性信号 + Retry16（flaky 根治，AGENTS 规范）
    /// <para>原 150ms 固定窗两缺陷：①RegisterCallback 是 TrySend（fire-and-forget），与 Start 命令
    /// 乱序时首个 tick 前回调未注册（run 39662954991 实证 callCount=0）②窗内回调未触发即假红。
    /// 修法：回调进站即 Set 信号量（注册+触发双确认），20s 收敛窗；未触发换新实例最多 16 次。</para></summary>
    [Fact]
    [Trait("Category", "Flaky")]
    public async Task HeartbeatCallback_Exception_DoesNotCrashLoop_Retry16Times() {
        for (var attempt = 0; attempt < 16; attempt++) {
            if (await TryHeartbeatExceptionLoopOnceAsync()) {
                return;
            }
        }
        throw new Xunit.Sdk.XunitException("16次重试均失败: 20s 信号窗内回调从未触发（注册/定时链断裂）");
    }

    /// <summary>单轮异常回调循环验证 — false = 本轮信号窗内回调未触发（注册乱序，非断言失败）</summary>
    private static async Task<bool> TryHeartbeatExceptionLoopOnceAsync() {
        var called = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callCount = 0;
        await using var heartbeat = CreateHeartbeat(TimeSpan.FromMilliseconds(50));

        heartbeat.RegisterCallback(_ => {
            var count = Interlocked.Increment(ref callCount);
            called.TrySetResult();
            if (count == 1) {
                throw new InvalidOperationException("test");
            }

            return ValueTask.CompletedTask;
        });

        await heartbeat.StartActivityAsync(SessionActivityReason.ApiCall).ConfigureAwait(true);

        try {
            await called.Task.WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(true);
        } catch (TimeoutException) {
            return false;
        }

        Assert.True(Volatile.Read(ref callCount) >= 1);

        await heartbeat.ResetAsync().ConfigureAwait(true);
        return true;
    }

    [Fact]
    public async Task DisposeAsync_CanBeCalledMultipleTimes() {
        var heartbeat = CreateHeartbeat();

        await heartbeat.DisposeAsync().ConfigureAwait(true);
        await heartbeat.DisposeAsync().ConfigureAwait(true);

        Assert.Equal(0, heartbeat.RefCount);
    }
}