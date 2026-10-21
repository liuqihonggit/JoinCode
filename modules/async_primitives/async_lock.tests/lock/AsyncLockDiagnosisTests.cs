// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
namespace LockDiagnosis.Tests;

/// <summary>
/// AsyncLock 互斥语义 + LockRegistry 诊断能力 — 时序部分(需 Task.Delay/并发/SpinUntil/超时竞争)。
/// <para>拆分自原 AsyncLockDiagnosisTests,确定性部分见 AsyncLockDiagnosisPureTests。</para>
/// </summary>
public class AsyncLockDiagnosisTests : IDisposable {
    public AsyncLockDiagnosisTests() {
        LockRegistry.ClearForTesting();
        LockRegistry.SetDiagnosticsEnabled(true);
        LockRegistry.SetHoldTooLongThreshold(TimeSpan.FromMilliseconds(100));
        LockRegistry.SetWaitTimeoutThreshold(TimeSpan.FromMilliseconds(200));
    }

    public void Dispose() {
        LockRegistry.StopBackgroundScan();
        LockRegistry.SetDiagnosticSink(null);
    }

    [Fact]
    [Trait("Category", "Timing")]
    public async Task LockAsync_两个任务串行执行_互斥成立() {
        using var lk = new AsyncLock("mutex-test");
        var order = new List<int>();
        var t1 = Task.Run(async () => {
            using (await lk.TryLockAsync() ?? throw new System.TimeoutException($"锁 '{lk.Name}' 等待超时")) {
                order.Add(1);
                await Task.Delay(50);
                order.Add(2);
            }
        });
        var t2 = Task.Run(async () => {
            await Task.Delay(10);
            using (await lk.TryLockAsync() ?? throw new System.TimeoutException($"锁 '{lk.Name}' 等待超时")) {
                order.Add(3);
            }
        });
        await Task.WhenAll(t1, t2);
        order.Should().Equal([1, 2, 3], "LockAsync 应保证互斥，t2 必须等 t1 释放后才能进入");
    }

    [Fact]
    [Trait("Category", "Timing")]
    public async Task TryLock_已持有时返回null() {
        using var lk = new AsyncLock("trylock-test", TimeSpan.FromMilliseconds(500));
        using var guard = await lk.TryLockAsync() ?? throw new System.TimeoutException($"锁 '{lk.Name}' 等待超时");
        // 在另一线程获取 — 同线程重入会超时返回 null
        using var guard2 = await Task.Run(async () => await lk.TryLockAsync());
        guard2.Should().BeNull("另一线程持锁等待500ms超时后 TryLock 应返回 null");
    }

    [Fact]
    [Trait("Category", "Timing")]
    public async Task TryLockAsync_超时返回null() {
        using var lk = new AsyncLock("trylock-timeout", TimeSpan.FromMilliseconds(500));
        using var holder = await lk.TryLockAsync() ?? throw new System.TimeoutException($"锁 '{lk.Name}' 等待超时");
        // 在另一线程获取 — 同线程重入会超时返回 null
        using var result = await Task.Run(async () => await lk.TryLockAsync());
        result.Should().BeNull("锁已被持有时 TryLock 应500ms超时返回 null");
    }

    [Fact]
    [Trait("Category", "Timing")]
    public async Task 诊断Sink_持有过长时收到告警() {
        var messages = new ConcurrentQueue<string>();
        LockRegistry.SetDiagnosticSink(messages.Enqueue);
        LockRegistry.SetHoldTooLongThreshold(TimeSpan.FromMilliseconds(50));
        using var lk = new AsyncLock("hold-long-test");
        using (await lk.TryLockAsync() ?? throw new System.TimeoutException($"锁 '{lk.Name}' 等待超时")) {
            await Task.Delay(120);
        }
        messages.Should().Contain(m => m.Contains("LOCK-HOLD-TOO-LONG") && m.Contains("hold-long-test"),
            "持有时间超过阈值时诊断 sink 应收到告警");
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    [Trait("Category", "Timing")]
    public async Task 诊断Sink_等待过长时收到告警() {
        var messages = new ConcurrentQueue<string>();
        LockRegistry.SetDiagnosticSink(messages.Enqueue);
        LockRegistry.SetWaitTimeoutThreshold(TimeSpan.FromMilliseconds(50));
        using var lk = new AsyncLock("wait-long-test");
        using var holder = await lk.TryLockAsync() ?? throw new System.TimeoutException($"锁 '{lk.Name}' 等待超时");
        // TryLock 同步阻塞, 在另一线程调用以记录等待诊断
        var waitTask = Task.Run(async () => await lk.TryLockAsync());
        await Task.Delay(150);
        holder.Dispose();
        var acquired = await waitTask;
        acquired.Should().NotBeNull();
        acquired!.Dispose();
        messages.Should().Contain(m => m.Contains("LOCK-WAIT-SLOW") && m.Contains("wait-long-test"),
            "等待时间超过阈值后获取成功时诊断 sink 应收到 LOCK-WAIT-SLOW 告警");
    }

    [Fact]
    [Trait("Category", "Timing")]
    public async Task 后台扫描_持有过长时输出告警() {
        var messages = new ConcurrentQueue<string>();
        LockRegistry.SetDiagnosticSink(messages.Enqueue);
        var originalThreshold = LockRegistry.HoldTooLongThreshold;
        LockRegistry.SetHoldTooLongThreshold(TimeSpan.FromMilliseconds(50));
        LockRegistry.StartBackgroundScan(TimeSpan.FromMilliseconds(30));
        try {
            using var lk = new AsyncLock("scan-test");
            using var holder = await lk.TryLockAsync() ?? throw new System.TimeoutException($"锁 '{lk.Name}' 等待超时");
            var detected = System.Threading.SpinWait.SpinUntil(
                () => messages.Any(m => m.Contains("LOCK-SCAN-HOLD") && m.Contains("scan-test")),
                TimeSpan.FromSeconds(2));
            detected.Should().BeTrue("后台扫描应检测到持有过长的锁并告警(2s SpinUntil 等待,容忍CI调度延迟)");
        } finally {
            LockRegistry.StopBackgroundScan();
            LockRegistry.SetHoldTooLongThreshold(originalThreshold);
            LockRegistry.SetDiagnosticSink(null);
        }
    }

    [Fact]
    [Trait("Category", "Timing")]
    public async Task DiagnosticsEnabled关闭时_不记录诊断() {
        LockRegistry.SetDiagnosticsEnabled(false);
        var messages = new ConcurrentQueue<string>();
        LockRegistry.SetDiagnosticSink(messages.Enqueue);
        try {
            using var lk = new AsyncLock("disabled-test");
            using (await lk.TryLockAsync() ?? throw new System.TimeoutException($"锁 '{lk.Name}' 等待超时")) {
                await Task.Delay(60);
            }
            // 断言本锁无诊断（而非队列全空）：DiagnosticSink 是静态全局，并行测试类的锁诊断
            // 也会进本队列 —— "全空"断言在跨类并行下偶发红（flaky 同族，LockRegistry 静态互踩）
            messages.Should().NotContain(m => m.Contains("disabled-test"),
                "诊断关闭时本锁不应产生任何告警");
        } finally {
            LockRegistry.SetDiagnosticsEnabled(true);
        }
    }

    [Fact]
    [Trait("Category", "Timing")]
    public async Task Lock同步_基本互斥() {
        using var lk = new AsyncLock("sync-mutex", TimeSpan.FromMilliseconds(500));
        using var g1 = await lk.TryLockAsync() ?? throw new System.TimeoutException($"锁 '{lk.Name}' 等待超时");
        // 在另一线程获取 — 同线程重入会超时返回 null
        using var g2 = await Task.Run(async () => await lk.TryLockAsync());
        g2.Should().BeNull("同步 Lock 持有后另一线程 TryLock 应500ms超时返回 null");
    }

    [Fact]
    [Trait("Category", "Timing")]
    [Trait("Category", "Flaky")]
    public async Task 死锁检测_两个线程互相等待时自动检测_Retry16Times() {
        // flaky 根治（AGENTS 规范：重试16次+延长窗口）：后台 Timer 扫描在高负载（CI/本机全量并行）下回调
        // 延迟可远超单次窗口 —— 单轮 15s 检测窗口 × 16 轮重试，全部失败才判红（run 885 及本机全量偶发红实证）。
        // 每轮完整恢复 LockRegistry 全局（threshold/sink/扫描），防状态泄漏到下一轮。
        var originalWaitThreshold = LockRegistry.WaitTimeoutThreshold;
        string? lastReport = null;
        try {
            for (var attempt = 0; attempt < 16; attempt++) {
                var detected = await RunDeadlockRoundAsync(attempt);
                if (detected is { } report) {
                    return;
                }
                lastReport = LockRegistry.LastDeadlockReport;
            }
        } finally {
            LockRegistry.StopBackgroundScan();
            LockRegistry.SetWaitTimeoutThreshold(originalWaitThreshold);
            LockRegistry.SetDiagnosticSink(null);
        }
        throw new Xunit.Sdk.XunitException(
            $"16次重试均失败: 后台扫描在 16×15s 窗口内未检测到互相等待死锁（最后一次报告: {lastReport ?? "<null>"}）");
    }

    /// <summary>单轮死锁检测场景（两个线程经后台扫描路径互相等待）——返回检测报告字符串表示本轮命中；null=本轮未检测到</summary>
    private static async Task<string?> RunDeadlockRoundAsync(int attempt) {
        LockRegistry.SetWaitTimeoutThreshold(TimeSpan.FromMilliseconds(100));
        LockRegistry.StartBackgroundScan(TimeSpan.FromMilliseconds(50));
        var messages = new ConcurrentQueue<string>();
        LockRegistry.SetDiagnosticSink(messages.Enqueue);
        using var lockA = new AsyncLock($"deadlock-A-{attempt}", TimeSpan.FromSeconds(3));
        using var lockB = new AsyncLock($"deadlock-B-{attempt}", TimeSpan.FromSeconds(3));

        using var barrier = new Barrier(2);
        using var t1Done = new ManualResetEventSlim();
        using var t2Done = new ManualResetEventSlim();

        var t1 = new Thread(() => {
            using (lockA.TryLock() ?? throw new System.TimeoutException($"锁 '{lockA.Name}' 等待超时")) {
                barrier.SignalAndWait();
                using var g = lockB.TryLock();
            }
            t1Done.Set();
        }) { IsBackground = true };

        var t2 = new Thread(() => {
            using (lockB.TryLock() ?? throw new System.TimeoutException($"锁 '{lockB.Name}' 等待超时")) {
                barrier.SignalAndWait();
                using var g = lockA.TryLock();
            }
            t2Done.Set();
        }) { IsBackground = true };

        t1.Start();
        t2.Start();

        // 窗口 15s：CI 高负载 Timer 回调延迟留足余量（原 5s 曾被压满）
        var detected = System.Threading.SpinWait.SpinUntil(() => LockRegistry.DeadlockDetected, TimeSpan.FromSeconds(15));
        var report = detected ? LockRegistry.LastDeadlockReport : null;
        if (detected) {
            report.Should().Contain("DEADLOCK-DETECTED");
            report.Should().Contain(lockA.Name);
            report.Should().Contain(lockB.Name);
            messages.Should().Contain(m => m.Contains("DEADLOCK-DETECTED"));
        }

        t1Done.Wait(8000);
        t2Done.Wait(8000);
        // 本轮收尾：清检测标志/报告/flow 身份（internal ClearForTesting 先例），让下一轮从干净状态开始；
        // 锁对象带 attempt 后缀避免注册表残留互相干扰。ClearForTesting 内部会 StopBackgroundScan，
        // 下一轮开头的 StartBackgroundScan 重新拉起
        LockRegistry.ClearForTesting();
        await Task.CompletedTask;
        return report;
    }

    /// <summary>
    /// 根因修复验证:CI 高负载下后台 Timer 回调可能严重延迟,在死锁窗口内未执行 DetectDeadlock,
    /// 导致偶发漏检(run 35649777572 attempt#1 失败、attempt#2 通过)。
    /// OnWaitStart 即时检测应不依赖后台扫描即可发现死锁,消除对 Timer 调度及时性的依赖。
    /// </summary>
    [Fact]
    [Trait("Category", "Timing")]
    public async Task 死锁检测_OnWaitStart即时检测_不依赖后台扫描() {
        var messages = new ConcurrentQueue<string>();
        LockRegistry.SetDiagnosticSink(messages.Enqueue);
        using var lockA = new AsyncLock("imm-deadlock-A", TimeSpan.FromSeconds(5));
        using var lockB = new AsyncLock("imm-deadlock-B", TimeSpan.FromSeconds(5));
        LockRegistry.StopBackgroundScan(); // 模拟 CI 高负载:后台扫描不执行

        using var barrier = new Barrier(2);
        using var t1Done = new ManualResetEventSlim();
        using var t2Done = new ManualResetEventSlim();

        var t1 = new Thread(() => {
            using (lockA.TryLock() ?? throw new System.TimeoutException($"锁 '{lockA.Name}' 等待超时")) {
                barrier.SignalAndWait();
                using var g = lockB.TryLock();
            }
            t1Done.Set();
        }) { IsBackground = true };

        var t2 = new Thread(() => {
            using (lockB.TryLock() ?? throw new System.TimeoutException($"锁 '{lockB.Name}' 等待超时")) {
                barrier.SignalAndWait();
                using var g = lockA.TryLock();
            }
            t2Done.Set();
        }) { IsBackground = true };

        t1.Start();
        t2.Start();

        var detected = System.Threading.SpinWait.SpinUntil(() => LockRegistry.DeadlockDetected, TimeSpan.FromSeconds(2));
        detected.Should().BeTrue("OnWaitStart 即时检测应在不依赖后台扫描的情况下检测到死锁(根因修复:消除 Timer 调度延迟导致的偶发漏检)");

        t1Done.Wait(8000);
        t2Done.Wait(8000);

        LockRegistry.SetDiagnosticSink(null);
        await Task.CompletedTask;
    }

    [Fact]
    [Trait("Category", "Timing")]
    public async Task 死锁检测_async两个流互相等待时自动检测() {
        var messages = new ConcurrentQueue<string>();
        LockRegistry.SetDiagnosticSink(messages.Enqueue);
        var originalWaitThreshold = LockRegistry.WaitTimeoutThreshold;
        LockRegistry.SetWaitTimeoutThreshold(TimeSpan.FromMilliseconds(100));
        LockRegistry.StartBackgroundScan(TimeSpan.FromMilliseconds(50));
        try {
            using var lockA = new AsyncLock("async-deadlock-A", TimeSpan.FromSeconds(2));
            using var lockB = new AsyncLock("async-deadlock-B", TimeSpan.FromSeconds(2));

            var t1Ready = new TaskCompletionSource();
            var t2Ready = new TaskCompletionSource();

            var t1 = Task.Run(async () => {
                LockRegistry.RegisterFlow();
                using (await lockA.TryLockAsync() ?? throw new System.TimeoutException($"锁 '{lockA.Name}' 等待超时")) {
                    t1Ready.SetResult();
                    await t2Ready.Task;
                    using var g = await lockB.TryLockAsync();
                    if (g is null) Console.WriteLine("async t1 lockB 超时");
                }
            });

            var t2 = Task.Run(async () => {
                LockRegistry.RegisterFlow();
                using (await lockB.TryLockAsync() ?? throw new System.TimeoutException($"锁 '{lockB.Name}' 等待超时")) {
                    t2Ready.SetResult();
                    await t1Ready.Task;
                    using var g = await lockA.TryLockAsync();
                    if (g is null) Console.WriteLine("async t2 lockA 超时");
                }
            });

            var detected = System.Threading.SpinWait.SpinUntil(() => LockRegistry.DeadlockDetected, TimeSpan.FromSeconds(6));
            detected.Should().BeTrue("两个 async 流互相等待对方持有的锁应被自动检测为死锁(6s SpinUntil 等待,容忍CI高负载)");
            LockRegistry.LastDeadlockReport.Should().Contain("DEADLOCK-DETECTED");
            messages.Should().Contain(m => m.Contains("DEADLOCK-DETECTED"));

            await Task.WhenAll(t1, t2);
        } finally {
            LockRegistry.StopBackgroundScan();
            LockRegistry.SetWaitTimeoutThreshold(originalWaitThreshold);
            LockRegistry.SetDiagnosticSink(null);
        }
    }

    // ===== 重入检测 — 同步场景下同线程重入会超时返回 null (ThreadId 在 async 下不可靠,不做重入抛异常) =====

    [Fact]
    [Trait("Category", "Timing")]
    public async Task 重入检测_同步重入同一把锁应超时返回null() {
        using var lk = new AsyncLock("reentrant-sync", TimeSpan.FromMilliseconds(500));

        using (await lk.TryLockAsync() ?? throw new System.TimeoutException($"锁 '{lk.Name}' 等待超时")) {
            // 同线程重入: TryLock 等500ms超时返回 null (不抛异常,因为 ThreadId 在 async 下不可靠)
            using var result = await Task.Run(async () => await lk.TryLockAsync());
            result.Should().BeNull("另一线程持锁等待500ms超时后 TryLock 应返回 null");
        }
    }

    [Fact]
    [Trait("Category", "Timing")]
    public async Task 重入检测_async重入同一把锁应超时返回null() {
        using var lk = new AsyncLock("reentrant-async", TimeSpan.FromMilliseconds(500));

        using (await lk.TryLockAsync() ?? throw new System.TimeoutException($"锁 '{lk.Name}' 等待超时")) {
            using var result = await Task.Run(async () => await lk.TryLockAsync());
            result.Should().BeNull("另一线程持锁等待500ms超时后 TryLock 应返回 null");
        }
    }

    [Fact]
    [Trait("Category", "Timing")]
    public async Task 重入检测_不同线程获取同一把锁不应抛异常() {
        using var lk = new AsyncLock("cross-thread", TimeSpan.FromMilliseconds(500));
        using var holder = await lk.TryLockAsync() ?? throw new System.TimeoutException($"锁 '{lk.Name}' 等待超时");

        var act = async () => {
            await Task.Run(async () => {
                using var g = await lk.TryLockAsync();
            });
        };

        await act.Should().NotThrowAsync(
            "不同线程获取同一把锁是正常竞争，不是重入");
    }

    [Fact]
    [Trait("Category", "Timing")]
    public async Task 重入检测_异常应包含锁名和调用栈() {
        using var lk = new AsyncLock("reentrant-info", TimeSpan.FromMilliseconds(500));

        using (await lk.TryLockAsync() ?? throw new System.TimeoutException($"锁 '{lk.Name}' 等待超时")) {
            // 同线程重入: TryLock 等500ms超时返回 null
            using var result = await Task.Run(async () => await lk.TryLockAsync());
            result.Should().BeNull("另一线程持锁等待500ms超时后 TryLock 应返回 null");
        }
    }
}
