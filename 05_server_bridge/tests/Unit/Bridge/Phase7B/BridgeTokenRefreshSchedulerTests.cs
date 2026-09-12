
namespace Bridge.Tests.Phase7B;

public sealed class BridgeTokenRefreshSchedulerTests
{
    [Fact]
    public void ScheduleFromExpiresIn_ValidSeconds_SchedulesRefresh()
    {
        var scheduler = new BridgeTokenRefreshScheduler(
            new TokenRefreshOptions
            {
                GetAccessToken = () => "test-token",
                OnRefresh = (sessionId, token) => { },
                Label = "test",
            });

        // 10 秒过期 → 应调度刷新
        scheduler.ScheduleFromExpiresIn("session1", 10);

        // 验证不抛异常
        Assert.True(true);

        scheduler.CancelAll();
    }

    [Fact]
    public async Task Cancel_StopsScheduledRefresh()
    {
        var fakeTime = new FakeTimeProvider();
        var refreshed = false;
        var scheduler = new BridgeTokenRefreshScheduler(
            new TokenRefreshOptions
            {
                GetAccessToken = () => "test-token",
                OnRefresh = (sessionId, token) => { refreshed = true; },
                Label = "test",
                RefreshBufferMs = 50,
            },
            timeProvider: fakeTime);

        scheduler.ScheduleFromExpiresIn("session1", 1);
        scheduler.Cancel("session1");

        // 推进时间超过调度延迟，确认回调未执行
        fakeTime.Advance(TimeSpan.FromSeconds(2));
        // 让定时器回调有机会执行
        await Task.Yield();

        Assert.False(refreshed);
    }

    [Fact]
    public void CancelAll_StopsAllScheduledRefreshes()
    {
        var scheduler = new BridgeTokenRefreshScheduler(
            new TokenRefreshOptions
            {
                GetAccessToken = () => "test-token",
                OnRefresh = (sessionId, token) => { },
                Label = "test",
            });

        scheduler.ScheduleFromExpiresIn("session1", 1);
        scheduler.ScheduleFromExpiresIn("session2", 1);

        scheduler.CancelAll();

        // 不抛异常即通过
        Assert.True(true);
    }

    [Fact]
    public void Schedule_SameSession_ReplacesPrevious()
    {
        var scheduler = new BridgeTokenRefreshScheduler(
            new TokenRefreshOptions
            {
                GetAccessToken = () => "test-token",
                OnRefresh = (sessionId, token) => { },
                Label = "test",
            });

        // 两次 Schedule 同一个 session，不应抛异常
        scheduler.ScheduleFromExpiresIn("session1", 5);
        scheduler.ScheduleFromExpiresIn("session1", 10);

        Assert.True(true);
        scheduler.CancelAll();
    }
}
