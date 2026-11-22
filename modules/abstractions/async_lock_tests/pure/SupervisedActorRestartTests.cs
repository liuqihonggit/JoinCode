namespace Core.Utils;

[Trait("Category", "Deterministic")]
public class SupervisedActorRecordRestartTests {
    private static readonly DateTimeOffset BaseTime = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void empty_list_adds_restart() {
        var (newList, added) = ChildActorHandle.RecordRestart(
            ImmutableList<DateTimeOffset>.Empty, BaseTime, TimeSpan.FromMinutes(1), 3);
        added.Should().BeTrue();
        newList.Should().HaveCount(1);
        newList[0].Should().Be(BaseTime);
    }

    [Fact]
    public void below_max_adds_restart() {
        var existing = ImmutableList.Create(BaseTime.AddSeconds(-10), BaseTime.AddSeconds(-5));
        var (newList, added) = ChildActorHandle.RecordRestart(
            existing, BaseTime, TimeSpan.FromMinutes(1), 3);
        added.Should().BeTrue();
        newList.Should().HaveCount(3);
        newList[2].Should().Be(BaseTime);
    }

    [Fact]
    public void at_max_rejects_restart() {
        var existing = ImmutableList.Create(
            BaseTime.AddSeconds(-10), BaseTime.AddSeconds(-5), BaseTime.AddSeconds(-1));
        var (newList, added) = ChildActorHandle.RecordRestart(
            existing, BaseTime, TimeSpan.FromMinutes(1), 3);
        added.Should().BeFalse();
        newList.Should().HaveCount(3);
    }

    [Fact]
    public void filters_out_window_expired_then_adds() {
        var within = TimeSpan.FromMinutes(1);
        var existing = ImmutableList.Create(
            BaseTime - TimeSpan.FromMinutes(2),
            BaseTime - TimeSpan.FromMinutes(3),
            BaseTime - TimeSpan.FromMinutes(4));
        var (newList, added) = ChildActorHandle.RecordRestart(existing, BaseTime, within, 3);
        added.Should().BeTrue();
        newList.Should().HaveCount(1);
        newList[0].Should().Be(BaseTime);
    }

    [Fact]
    public void mixed_in_and_out_window_filters_correctly() {
        var within = TimeSpan.FromMinutes(1);
        var existing = ImmutableList.Create(
            BaseTime - TimeSpan.FromMinutes(2),
            BaseTime.AddSeconds(-30),
            BaseTime.AddSeconds(-10));
        var (newList, added) = ChildActorHandle.RecordRestart(existing, BaseTime, within, 3);
        added.Should().BeTrue();
        newList.Should().HaveCount(3);
        newList[2].Should().Be(BaseTime);
    }

    [Fact]
    public void max_restarts_one_allows_single_add() {
        var existing = ImmutableList<DateTimeOffset>.Empty;
        var (newList, added) = ChildActorHandle.RecordRestart(
            existing, BaseTime, TimeSpan.FromMinutes(1), 1);
        added.Should().BeTrue();
        newList.Should().HaveCount(1);
    }

    [Fact]
    public void max_restarts_one_rejects_second_in_window() {
        var existing = ImmutableList.Create(BaseTime.AddSeconds(-10));
        var (newList, added) = ChildActorHandle.RecordRestart(
            existing, BaseTime, TimeSpan.FromMinutes(1), 1);
        added.Should().BeFalse();
        newList.Should().HaveCount(1);
    }

    [Fact]
    public void exactly_at_window_boundary_is_filtered() {
        var within = TimeSpan.FromMinutes(1);
        var edgeTime = BaseTime - within - TimeSpan.FromSeconds(1);
        var existing = ImmutableList.Create(edgeTime);
        var (newList, added) = ChildActorHandle.RecordRestart(existing, BaseTime, within, 1);
        added.Should().BeTrue();
        newList.Should().HaveCount(1);
        newList[0].Should().Be(BaseTime);
    }
}

/// <summary>
/// BackoffStrategy 纯函数测试 — DSG033 S5 指数退避重启。
/// </summary>
public class BackoffStrategyTests {
    [Fact]
    public void delay_at_restart_0_equals_min_backoff() {
        var strategy = new ActorBackoffStrategy(TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(1), 0);
        strategy.ComputeDelay(0).TotalMilliseconds.Should().BeApproximately(10, 0.1);
    }

    [Fact]
    public void delay_increases_exponentially() {
        var strategy = new ActorBackoffStrategy(TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(1), 0);
        strategy.ComputeDelay(1).TotalMilliseconds.Should().BeApproximately(20, 0.1);
        strategy.ComputeDelay(2).TotalMilliseconds.Should().BeApproximately(40, 0.1);
        strategy.ComputeDelay(3).TotalMilliseconds.Should().BeApproximately(80, 0.1);
    }

    [Fact]
    public void delay_capped_at_max_backoff() {
        var strategy = new ActorBackoffStrategy(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(50), 0);
        strategy.ComputeDelay(10).TotalMilliseconds.Should().BeLessThanOrEqualTo(50);
    }

    [Fact]
    public void jitter_within_random_factor_range() {
        var strategy = new ActorBackoffStrategy(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(10), 0.2);
        for (var i = 0; i < 100; i++) {
            var delay = strategy.ComputeDelay(0).TotalMilliseconds;
            delay.Should().BeInRange(80, 120, "抖动应在 ±20% 范围(100±20)");
        }
    }
}
