namespace JoinCode.Transport.Impl.Tests;

/// <summary>
/// TASK071 #3 — BridgeTokenRefreshScheduler 行为锁（对齐 TS jwtUtils.ts:147-206）：
/// ① ScheduleFromExpiresIn 30s 下限钳制（buffer 超过 expires_in 防紧循环）；
/// ② null token 计数失败 → 60s 重试 → 达上限放弃（旧实现静默中断 / 计数被清零致上限永不触发）。
/// RecordingTimeProvider 捕获 CreateTimer dueTime；Actor 的 DisposeAsync 等待全部 in-flight 命令
/// （ADR 0125 RegisterInFlight 契约）— Fire → Dispose 后断言，零等待全确定。
/// </summary>
public sealed class BridgeTokenRefreshSchedulerBehaviorTests {
    private int _refreshCalls;
    private Func<string?> _tokenProvider = () => null;

    private BridgeTokenRefreshScheduler CreateScheduler(RecordingTimeProvider provider) => new(
        new TokenRefreshOptions {
            GetAccessToken = () => _tokenProvider(),
            OnRefresh = (_, _) => _refreshCalls++,
            Label = "behavior-test",
            Logger = ProviderLogger,
        },
        provider);

    /// <summary>等待第 index 个 timer 被调度（Actor 异步消费 — SpinUntil 条件等待 5s，非盲等）后触发。</summary>
    private void FireWhenAvailable(BridgeTokenRefreshScheduler scheduler, RecordingTimeProvider provider, int index) {
        var scheduled = System.Threading.SpinWait.SpinUntil(
            () => provider.Created.Count > index, TimeSpan.FromSeconds(5));
        if (!scheduled) {
            var timersField = typeof(BridgeTokenRefreshScheduler)
                .GetField("_timers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var timers = timersField?.GetValue(scheduler);
            throw new InvalidOperationException(
                $"timer #{index} 未在 5s 内调度: created=[{string.Join(",", provider.Created.Select(c => c.DueMs))}], _timers={timers?.GetType().GetProperty("Count")?.GetValue(timers) ?? "(?)"}, probe=[{string.Join(" | ", provider.ProbeLog)}]");
        }

        provider.Created.ElementAt(index).Timer.Fire();
    }

    /// <summary>控制台日志 — 探针（定位 DoRefresh 处理链断点后移除）。</summary>
    private static readonly Microsoft.Extensions.Logging.ILogger ProviderLogger =
        Microsoft.Extensions.Logging.LoggerFactory.Create(b => b.AddConsole()).CreateLogger("behavior-test");

    private static string MakeJwtWithExp(int offsetSeconds) {
        // exp 取当前时钟 + 偏移 — Schedule 按 now 对比 exp 计算 delay（过去时间戳会被 30s 下限钳制吞掉）
        var exp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + offsetSeconds;
        var payload = "{\"exp\":" + exp.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}";
        var header = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("{\"alg\":\"HS256\"}")).TrimEnd('=');
        var payloadB64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(payload)).TrimEnd('=');
        return $"{header}.{payloadB64}.sig";
    }

    [Fact]
    public async Task ScheduleFromExpiresIn_BufferExceedsExpires_ClampedTo30sFloor() {
        var provider = new RecordingTimeProvider();
        await using var scheduler = new BridgeTokenRefreshScheduler(
            new TokenRefreshOptions { GetAccessToken = () => null, OnRefresh = (_, _) => { }, Label = "t" },
            provider);

        // expires_in=10s, buffer 默认 60s → 裸 delay = -50s → 必须 clamp 到 30s（旧实现立即 DoRefresh 紧循环）
        scheduler.ScheduleFromExpiresIn("s1", expiresInSeconds: 10);
        // 命令消费异步 — Dispose 与消费竞态（CI 实证 Created 偶发 0），先条件等待 timer 落地
        System.Threading.SpinWait.SpinUntil(() => provider.Created.Count >= 1, TimeSpan.FromSeconds(5))
            .Should().BeTrue("ScheduleFromDelayCmd 应在 5s 内被消费并创建 timer");
        await scheduler.DisposeAsync().ConfigureAwait(true);

        provider.Created.Should().HaveCount(1);
        provider.Created.ElementAt(0).DueMs.Should().Be(30_000);
    }

    [Fact]
    public async Task ScheduleFromExpiresIn_NormalDelay_Unclamped() {
        var provider = new RecordingTimeProvider();
        await using var scheduler = new BridgeTokenRefreshScheduler(
            new TokenRefreshOptions { GetAccessToken = () => null, OnRefresh = (_, _) => { }, Label = "t" },
            provider);

        // expires_in=3600s - buffer 300s = 3300s > 30s → 不受钳制影响
        scheduler.ScheduleFromExpiresIn("s1", expiresInSeconds: 3600);
        System.Threading.SpinWait.SpinUntil(() => provider.Created.Count >= 1, TimeSpan.FromSeconds(5))
            .Should().BeTrue("ScheduleFromDelayCmd 应在 5s 内被消费并创建 timer");
        await scheduler.DisposeAsync().ConfigureAwait(true);

        provider.Created.Should().HaveCount(1);
        provider.Created.ElementAt(0).DueMs.Should().Be(3_300_000, "3600s - 默认 buffer 300s = 3300s");
    }

    [Fact]
    public async Task DoRefresh_NullToken_RetriesWith60sThenGivesUp() {
        _tokenProvider = () => null; // 永远取不到 token
        var provider = new RecordingTimeProvider();
        await using var scheduler = CreateScheduler(provider);

        scheduler.ScheduleFromExpiresIn("s1", expiresInSeconds: 10);
        FireWhenAvailable(scheduler, provider, 0); // 触发首次 DoRefresh → 失败 1
        FireWhenAvailable(scheduler, provider, 1); // 失败 2（60s 重试 timer）
        FireWhenAvailable(scheduler, provider, 2); // 失败 3 → 达上限放弃
        await scheduler.DisposeAsync().ConfigureAwait(true); // 等待全部 in-flight 完成（ADR 0125）

        _refreshCalls.Should().Be(0, "取不到 token 就不该回调 OnRefresh");
        provider.Created.Should().HaveCount(3,
            "初始 30s + 两次 60s 重试 = 3 个 timer；第 3 次失败达上限后不得再调度（防刷新风暴）");
        provider.Created.ElementAt(1).DueMs.Should().Be(60_000);
        provider.Created.ElementAt(2).DueMs.Should().Be(60_000);
    }

    [Fact]
    public async Task DoRefresh_Success_AfterFailures_ResetsFailureChain() {
        // token 序列按 GetAccessToken 调用序号驱动（Actor FIFO 消费保证与 DoRefresh 1:1 对应 —
        // 禁止测试线程覆盖 _tokenProvider 字段：与异步消费竞态，日志实证三次全 null）
        var calls = 0;
        var jwt = MakeJwtWithExp(offsetSeconds: 3600);
        _tokenProvider = () => ++calls switch {
            3 => jwt,   // 第 3 次 DoRefresh：成功
            _ => null,  // 第 1/2 次：失败；第 4 次（成功重排后的 DoRefresh）：再失败 → 重试链
        };
        var provider = new RecordingTimeProvider();
        await using var scheduler = CreateScheduler(provider);

        scheduler.ScheduleFromExpiresIn("s1", expiresInSeconds: 10);
        FireWhenAvailable(scheduler, provider, 0); // 失败 1
        FireWhenAvailable(scheduler, provider, 1); // 失败 2
        FireWhenAvailable(scheduler, provider, 2); // 成功 → OnRefresh + Schedule(jwt)
        FireWhenAvailable(scheduler, provider, 3); // 新 timer 的 DoRefresh → 失败（计数已随成功清零 → 从 1 开始 → 60s 重试而非放弃）
        await scheduler.DisposeAsync().ConfigureAwait(true);

        _refreshCalls.Should().Be(1, "第三次拿到 token 应回调 OnRefresh");
        provider.Created.Should().HaveCount(5,
            "30s 初始 + 60s + 60s + 成功后按新 exp 调度 + 失败 1 的 60s 重试");
        provider.Created.ElementAt(3).DueMs.Should().BeInRange(3_299_000, 3_300_000,
            "成功后按新 JWT exp 重新调度（3600-300=3300s 减测试流逝毫秒 — 远大于 30s 下限）");
        provider.Created.ElementAt(4).DueMs.Should().Be(60_000, "成功清零后再次失败应重新进入重试链");
    }

    /// <summary>捕获 CreateTimer dueTime 的 TimeProvider（timer 手动触发 — 确定性无真实等待）。</summary>
    private sealed class RecordingTimeProvider : TimeProvider {
        public System.Collections.Concurrent.ConcurrentQueue<(long DueMs, RecordingTimer Timer)> Created { get; } = new();

        public List<string> ProbeLog { get; } = [];

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) {
            ProbeLog.Add($"CreateTimer({(long)dueTime.TotalMilliseconds})");
            var timer = new RecordingTimer(callback, state);
            Created.Enqueue(((long)dueTime.TotalMilliseconds, timer));
            return timer;
        }

        /// <summary>手动触发第 index 个创建的 timer 回调。</summary>
        public void Fire(int index) => Created.ElementAt(index).Timer.Fire();

        public sealed class RecordingTimer(TimerCallback callback, object? state) : ITimer {
            public void Fire() => callback(state);

            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
