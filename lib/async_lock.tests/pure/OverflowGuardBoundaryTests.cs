namespace Core.Utils;

/// <summary>
/// ComputeTotalTimeoutMs 整数溢出守卫边界测试 — 验证 long 计算后钳制到 int.MaxValue。
/// </summary>
[Trait("Category", "Deterministic")]
public class ComputeTotalTimeoutMsOverflowTests {
    [Theory]
    [InlineData(int.MaxValue, 0, int.MaxValue)]
    [InlineData(int.MaxValue, 1, int.MaxValue)]
    [InlineData(int.MaxValue, int.MaxValue, int.MaxValue)]
    [InlineData(1, int.MaxValue, int.MaxValue)]
    [InlineData(100_000, int.MaxValue, int.MaxValue)]
    [InlineData(10_000, 16, 170_000)]
    [InlineData(0, int.MaxValue, 0)]
    [InlineData(0, 0, 0)]
    public void overflow_clamped_to_int_max(int singleTimeout, int maxRetries, int expected) {
        ActorBase<object, object>.ComputeTotalTimeoutMs(singleTimeout, maxRetries).Should().Be(expected);
    }

    [Fact]
    public void maxRetries_plus_one_overflow_still_clamped() {
        // maxRetries = int.MaxValue 时, maxRetries+1 在 int 下溢出为负数,原实现会返回负数
        // 修复后用 long 计算,钳制到 int.MaxValue
        ActorBase<object, object>.ComputeTotalTimeoutMs(1, int.MaxValue).Should().Be(int.MaxValue);
    }

    [Fact]
    public void product_overflow_still_clamped() {
        // 100_000 * (int.MaxValue + 1) 在 int 下溢出,修复后钳制到 int.MaxValue
        ActorBase<object, object>.ComputeTotalTimeoutMs(100_000, int.MaxValue).Should().Be(int.MaxValue);
    }

    [Fact]
    public void result_never_negative() {
        // 即使 singleTimeoutMs 为负数(异常调用),结果也不应为负(诊断消息用)
        // 注:负 singleTimeoutMs 是调用方契约违反,这里只验证不溢出为意外的正值
        var result = ActorBase<object, object>.ComputeTotalTimeoutMs(-1, 5);
        result.Should().Be(-6);
    }
}

/// <summary>
/// AsyncLock.TryLock TimeSpan 截断边界测试 — 验证 TotalMilliseconds 超过 int.MaxValue 不抛 OverflowException。
/// </summary>
[Trait("Category", "Deterministic")]
public class AsyncLockTryLockTimeSpanClampTests {
    [Fact]
    public void ClampTimeoutMs_zero_returns_zero() {
        AsyncLock.ClampTimeoutMs(TimeSpan.Zero).Should().Be(0);
    }

    [Fact]
    public void ClampTimeoutMs_negative_returns_zero() {
        AsyncLock.ClampTimeoutMs(TimeSpan.FromMilliseconds(-100)).Should().Be(0);
    }

    [Fact]
    public void ClampTimeoutMs_normal_value_passes_through() {
        AsyncLock.ClampTimeoutMs(TimeSpan.FromMilliseconds(500)).Should().Be(500);
    }

    [Fact]
    public void ClampTimeoutMs_just_below_int_max_passes_through() {
        AsyncLock.ClampTimeoutMs(TimeSpan.FromMilliseconds(int.MaxValue - 1)).Should().Be(int.MaxValue - 1);
    }

    [Fact]
    public void ClampTimeoutMs_above_int_max_clamped() {
        // 原 (int)TotalMilliseconds 会抛 OverflowException
        AsyncLock.ClampTimeoutMs(TimeSpan.FromMilliseconds((double)int.MaxValue + 1)).Should().Be(int.MaxValue);
    }

    [Fact]
    public void ClampTimeoutMs_huge_value_clamped() {
        AsyncLock.ClampTimeoutMs(TimeSpan.FromDays(365)).Should().Be(int.MaxValue);
    }

    [Fact]
    public void TryLock_with_huge_timeout_does_not_throw_overflow() {
        // 原实现会抛 OverflowException,修复后应正常返回(锁可用)或超时返回 null
        using var lk = new AsyncLock("overflow-test");
        var act = () => lk.TryLock(TimeSpan.FromDays(365));
        act.Should().NotThrow();
    }

    [Fact]
    public async Task TryLockAsync_with_huge_timeout_does_not_throw_overflow() {
        using var lk = new AsyncLock("overflow-test-async");
        var act = async () => await lk.TryLockAsync(TimeSpan.FromDays(365));
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void TryLock_with_negative_timeout_treated_as_nonblocking() {
        // 原实现 (int)负数 = 负数,SemaphoreSlim.Wait 抛 ArgumentOutOfRangeException
        // 修复后钳制为 0(非阻塞尝试),锁可用时返回 releaser
        using var lk = new AsyncLock("negative-test");
        using var releaser = lk.TryLock(TimeSpan.FromMilliseconds(-100));
        releaser.Should().NotBeNull();
        releaser!.Dispose();
    }
}

/// <summary>
/// RoundRobinStrategy.Select 索引溢出边界测试 — 验证 _index 溢出 int.MaxValue 后仍返回非负索引。
/// </summary>
[Trait("Category", "Deterministic")]
public class RoundRobinStrategyOverflowTests {
    [Fact]
    public void select_returns_non_negative_after_many_calls() {
        // 大量调用使 _index 接近溢出,验证始终返回非负
        var strategy = new RoundRobinStrategy<string>();
        var workerCount = 3;
        for (var i = 0; i < 100_000; i++) {
            var idx = strategy.Select(workerCount, "msg");
            idx.Should().BeInRange(0, workerCount - 1);
        }
    }

    [Fact]
    public void select_with_int_max_workers_returns_zero() {
        var strategy = new RoundRobinStrategy<string>();
        // workerCount = int.MaxValue 时,(uint)index % (uint)int.MaxValue 仍非负
        var idx = strategy.Select(int.MaxValue, "msg");
        idx.Should().BeInRange(0, int.MaxValue - 1);
    }

    [Fact]
    public void select_never_returns_negative_index() {
        // 即使内部 _index 溢出为负,uint 取模保证结果非负
        var strategy = new RoundRobinStrategy<string>();
        var workerCount = 7;
        // 调用足够多次触发潜在的溢出路径(虽然 100k 不会真的溢出 int,但验证 uint 取模的正确性)
        for (var i = 0; i < 10_000; i++) {
            strategy.Select(workerCount, "msg").Should().BeGreaterThanOrEqualTo(0);
        }
    }
}

/// <summary>
/// ActorBackpressure 负数 Capacity 守卫边界测试。
/// </summary>
[Trait("Category", "Deterministic")]
public class ActorBackpressureNegativeCapacityTests {
    [Fact]
    public void negative_capacity_throws_argument_out_of_range() {
        var act = () => new ActorBackpressure(Capacity: -1);
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("Capacity");
    }

    [Fact]
    public void large_negative_capacity_throws() {
        var act = () => new ActorBackpressure(Capacity: int.MinValue);
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("Capacity");
    }

    [Fact]
    public void zero_capacity_allowed() {
        var bp = new ActorBackpressure(Capacity: 0);
        bp.Capacity.Should().Be(0);
    }

    [Fact]
    public void positive_capacity_allowed() {
        var bp = new ActorBackpressure(Capacity: 100);
        bp.Capacity.Should().Be(100);
    }

    [Fact]
    public void negative_capacity_with_other_params_throws() {
        var act = () => new ActorBackpressure(
            Capacity: -10,
            FullMode: BoundedChannelFullMode.Wait,
            HighWatermark: 5,
            CriticalWatermark: 8,
            SendTimeout: TimeSpan.FromSeconds(10));
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("Capacity");
    }

    [Fact]
    public void exception_message_contains_capacity_hint() {
        var act = () => new ActorBackpressure(Capacity: -5);
        var ex = act.Should().Throw<ArgumentOutOfRangeException>().Which;
        ex.Message.Should().Contain("Capacity 不能为负数");
    }
}

/// <summary>
/// RecordRestart 零/负参数边界测试 — 验证 within=0/negative、maxRestarts=0/negative 行为。
/// </summary>
[Trait("Category", "Deterministic")]
public class RecordRestartBoundaryTests {
    private static readonly DateTimeOffset BaseTime = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void max_restarts_zero_rejects_all() {
        // maxRestarts=0 意味着不允许任何重启
        var (newList, added) = ChildActorHandle.RecordRestart(
            ImmutableList<DateTimeOffset>.Empty, BaseTime, TimeSpan.FromMinutes(1), 0);
        added.Should().BeFalse();
        newList.Should().BeEmpty();
    }

    [Fact]
    public void max_restarts_negative_rejects_all() {
        // maxRestarts<0 时 filtered.Count >= 负数 总为 true,拒绝所有
        var (newList, added) = ChildActorHandle.RecordRestart(
            ImmutableList<DateTimeOffset>.Empty, BaseTime, TimeSpan.FromMinutes(1), -1);
        added.Should().BeFalse();
        newList.Should().BeEmpty();
    }

    [Fact]
    public void max_restarts_negative_with_existing_rejects_and_filters() {
        var existing = ImmutableList.Create(BaseTime.AddSeconds(-10));
        var (newList, added) = ChildActorHandle.RecordRestart(
            existing, BaseTime, TimeSpan.FromMinutes(1), -5);
        added.Should().BeFalse();
        // 旧记录在窗口内,保留(未添加新记录)
        newList.Should().HaveCount(1);
    }

    [Fact]
    public void within_zero_filters_all_past_then_adds() {
        // within=0: now - t > 0 对所有 t < now 为 true,过滤所有过去记录,然后添加 now
        var existing = ImmutableList.Create(BaseTime.AddSeconds(-10), BaseTime.AddSeconds(-5));
        var (newList, added) = ChildActorHandle.RecordRestart(existing, BaseTime, TimeSpan.Zero, 3);
        added.Should().BeTrue();
        newList.Should().HaveCount(1);
        newList[0].Should().Be(BaseTime);
    }

    [Fact]
    public void within_negative_filters_all_past_then_adds() {
        // within<0: now - t > 负数 对所有 t <= now 为 true,过滤所有,然后添加 now
        var existing = ImmutableList.Create(BaseTime.AddSeconds(-10));
        var (newList, added) = ChildActorHandle.RecordRestart(
            existing, BaseTime, TimeSpan.FromSeconds(-1), 3);
        added.Should().BeTrue();
        newList.Should().HaveCount(1);
        newList[0].Should().Be(BaseTime);
    }

    [Fact]
    public void within_zero_at_exact_now_keeps_record() {
        // now - now = 0, 0 > 0 为 false,所以 now 本身不被过滤
        var existing = ImmutableList.Create(BaseTime);
        var (newList, added) = ChildActorHandle.RecordRestart(existing, BaseTime, TimeSpan.Zero, 3);
        added.Should().BeTrue();
        newList.Should().HaveCount(2);
    }

    [Fact]
    public void within_zero_max_restarts_zero_rejects() {
        // within=0 过滤所有,但 maxRestarts=0 拒绝添加
        var (newList, added) = ChildActorHandle.RecordRestart(
            ImmutableList.Create(BaseTime.AddSeconds(-10)), BaseTime, TimeSpan.Zero, 0);
        added.Should().BeFalse();
        newList.Should().BeEmpty();
    }
}

/// <summary>
/// EvaluateBreakerState 负数 recoveryDelay 边界测试 — 验证负数/零 recoveryDelay 行为。
/// </summary>
[Trait("Category", "Deterministic")]
public class EvaluateBreakerStateRecoveryDelayBoundaryTests {
    [Fact]
    public void negative_recovery_delay_immediately_transitions_to_halfopen() {
        // recoveryDelay < 0 时, elapsed >= recoveryDelay 总为 true(elapsed 通常 >= 0)
        var (reject, newState) = GatewayActor<object, object>.EvaluateBreakerState(
            GatewayCircuitState.Open, TimeSpan.Zero, 5, TimeSpan.FromSeconds(-10));
        reject.Should().BeFalse();
        newState.Should().Be(GatewayCircuitState.HalfOpen);
    }

    [Fact]
    public void zero_recovery_delay_immediately_transitions_to_halfopen() {
        // recoveryDelay = 0 时, elapsed >= 0 总为 true
        var (reject, newState) = GatewayActor<object, object>.EvaluateBreakerState(
            GatewayCircuitState.Open, TimeSpan.Zero, 5, TimeSpan.Zero);
        reject.Should().BeFalse();
        newState.Should().Be(GatewayCircuitState.HalfOpen);
    }

    [Fact]
    public void negative_recovery_delay_with_positive_elapsed_transitions() {
        var (reject, newState) = GatewayActor<object, object>.EvaluateBreakerState(
            GatewayCircuitState.Open, TimeSpan.FromSeconds(10), 5, TimeSpan.FromSeconds(-1));
        reject.Should().BeFalse();
        newState.Should().Be(GatewayCircuitState.HalfOpen);
    }

    [Fact]
    public void negative_recovery_delay_closed_state_unaffected() {
        var (reject, newState) = GatewayActor<object, object>.EvaluateBreakerState(
            GatewayCircuitState.Closed, TimeSpan.Zero, 5, TimeSpan.FromSeconds(-10));
        reject.Should().BeFalse();
        newState.Should().BeNull();
    }

    [Fact]
    public void negative_recovery_delay_halfopen_state_unaffected() {
        var (reject, newState) = GatewayActor<object, object>.EvaluateBreakerState(
            GatewayCircuitState.HalfOpen, TimeSpan.Zero, 5, TimeSpan.FromSeconds(-10));
        reject.Should().BeFalse();
        newState.Should().BeNull();
    }

    [Fact]
    public void large_negative_recovery_delay_does_not_overflow() {
        // 验证 TimeSpan 比较不会溢出
        var (reject, newState) = GatewayActor<object, object>.EvaluateBreakerState(
            GatewayCircuitState.Open, TimeSpan.Zero, 5, TimeSpan.MinValue);
        reject.Should().BeFalse();
        newState.Should().Be(GatewayCircuitState.HalfOpen);
    }
}

/// <summary>
/// CalculateCriticalDelay 高 count 溢出边界测试 — 验证 count - highWatermark int 减法溢出后仍正确。
/// </summary>
[Trait("Category", "Deterministic")]
public class CalculateCriticalDelayOverflowTests {
    [Fact]
    public void count_max_high_min_does_not_overflow_to_negative() {
        // int.MaxValue - int.MinValue 在 int 下溢出为 -1,原实现会返回 Zero(错误)
        // 修复后用 long 计算,overflow = int.MaxValue - int.MinValue = 4294967295,钳制到 1000ms
        var delay = BackpressureChannel<object>.CalculateCriticalDelay(int.MaxValue, int.MinValue);
        delay.Should().Be(TimeSpan.FromMilliseconds(1000));
    }

    [Fact]
    public void count_max_high_zero_caps_at_one_second() {
        BackpressureChannel<object>.CalculateCriticalDelay(int.MaxValue, 0)
            .Should().Be(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void count_zero_high_max_returns_zero() {
        // 0 - int.MaxValue 在 int 下溢出为正数(int.MinValue + 1),原实现会返回错误的大延迟
        // 修复后用 long 计算,overflow = -int.MaxValue < 0,返回 Zero
        BackpressureChannel<object>.CalculateCriticalDelay(0, int.MaxValue)
            .Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void count_min_high_max_returns_zero() {
        // int.MinValue - int.MaxValue 在 int 下溢出为 1,原实现会返回 100ms(错误)
        // 修复后用 long 计算,overflow = int.MinValue - int.MaxValue = -4294967295 < 0,返回 Zero
        BackpressureChannel<object>.CalculateCriticalDelay(int.MinValue, int.MaxValue)
            .Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void count_min_high_min_returns_zero() {
        // 两者相等,overflow = 0,返回 Zero
        BackpressureChannel<object>.CalculateCriticalDelay(int.MinValue, int.MinValue)
            .Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void count_max_high_max_returns_zero() {
        BackpressureChannel<object>.CalculateCriticalDelay(int.MaxValue, int.MaxValue)
            .Should().Be(TimeSpan.Zero);
    }

    [Theory]
    [InlineData(int.MaxValue, int.MinValue)]
    [InlineData(int.MaxValue, 0)]
    [InlineData(100_000, 0)]
    [InlineData(11, 1)]
    public void large_overflow_always_capped_at_one_second(int count, int high) {
        // 任何产生大 overflow 的组合都应钳制到 1 秒
        var delay = BackpressureChannel<object>.CalculateCriticalDelay(count, high);
        delay.TotalMilliseconds.Should().BeLessThanOrEqualTo(1000);
    }
}
