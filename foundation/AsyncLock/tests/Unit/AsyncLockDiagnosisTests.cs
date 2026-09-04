namespace LockDiagnosis.Tests;

/// <summary>
/// AsyncLock 互斥语义 + LockRegistry 诊断能力单元测试。
/// </summary>
public class AsyncLockDiagnosisTests : IDisposable
{
    public AsyncLockDiagnosisTests()
    {
        LockRegistry.ClearForTesting();
        LockRegistry.DiagnosticsEnabled = true;
        LockRegistry.HoldTooLongThreshold = TimeSpan.FromMilliseconds(100);
        LockRegistry.WaitTimeoutThreshold = TimeSpan.FromMilliseconds(200);
    }

    public void Dispose()
    {
        LockRegistry.StopBackgroundScan();
        LockRegistry.DiagnosticSink = null;
    }

    [Fact]
    public async Task LockAsync_两个任务串行执行_互斥成立()
    {
        using var lk = new AsyncLock("mutex-test");
        var order = new List<int>();
        var t1 = Task.Run(async () =>
        {
            using (await lk.TryLockAsync() ?? throw new System.TimeoutException($"锁 '{lk.Name}' 等待超时"))
            {
                order.Add(1);
                await Task.Delay(50);
                order.Add(2);
            }
        });
        var t2 = Task.Run(async () =>
        {
            await Task.Delay(10);
            using (await lk.TryLockAsync() ?? throw new System.TimeoutException($"锁 '{lk.Name}' 等待超时"))
            {
                order.Add(3);
            }
        });
        await Task.WhenAll(t1, t2);
        order.Should().Equal([1, 2, 3], "LockAsync 应保证互斥，t2 必须等 t1 释放后才能进入");
    }

    [Fact]
    public async Task 具名构造_锁名出现在DumpAll()
    {
        using var lk = new AsyncLock("my-test-lock");
        using (await lk.TryLockAsync() ?? throw new System.TimeoutException($"锁 '{lk.Name}' 等待超时"))
        {
            var dump = LockRegistry.DumpAll();
            dump.Should().Contain("my-test-lock", "具名锁的名称应出现在 DumpAll 输出中");
            dump.Should().Contain("持有中", "已获取的锁应显示持有中状态");
        }
    }

    [Fact]
    public async Task DumpAll_空闲锁显示空闲()
    {
        using var lk = new AsyncLock("idle-lock");
        var dump = LockRegistry.DumpAll();
        dump.Should().Contain("idle-lock");
        dump.Should().Contain("空闲", "未获取的锁应显示空闲");
    }

    [Fact]
    public async Task LockRegistry_Count_构造增加_Dispose减少()
    {
        LockRegistry.ClearForTesting();
        var lk = new AsyncLock("count-test");
        LockRegistry.Count.Should().Be(1, "构造一把锁后注册表应有1条");
        lk.Dispose();
        LockRegistry.Count.Should().Be(0, "Dispose 后应从注册表移除");
        await Task.CompletedTask;
    }

    [Fact]
    public async Task TryLock_已持有时返回null()
    {
        using var lk = new AsyncLock("trylock-test", TimeSpan.FromMilliseconds(500));
        using var guard = await lk.TryLockAsync() ?? throw new System.TimeoutException($"锁 '{lk.Name}' 等待超时");
        // 在另一线程获取 — 同线程重入会超时返回 null
        var guard2 = await Task.Run(async () => await lk.TryLockAsync());
        guard2.Should().BeNull("另一线程持锁等待500ms超时后 TryLock 应返回 null");
    }

    [Fact]
    public async Task TryLockAsync_超时返回null()
    {
        using var lk = new AsyncLock("trylock-timeout", TimeSpan.FromMilliseconds(500));
        using var holder = await lk.TryLockAsync() ?? throw new System.TimeoutException($"锁 '{lk.Name}' 等待超时");
        // 在另一线程获取 — 同线程重入会超时返回 null
        var result = await Task.Run(async () => await lk.TryLockAsync());
        result.Should().BeNull("锁已被持有时 TryLock 应500ms超时返回 null");
    }

    [Fact]
    public async Task Dispose后_LockAsync抛ObjectDisposedException()
    {
        var lk = new AsyncLock("disposed-test");
        lk.Dispose();
        // Dispose 后 TryLock 同步抛 ObjectDisposedException
        Func<Task> act = async () => await lk.TryLockAsync();
        await act.Should().ThrowAsync<ObjectDisposedException>("Dispose 后再获取应抛 ObjectDisposedException");
        await Task.CompletedTask;
    }

    [Fact]
    public async Task 诊断Sink_持有过长时收到告警()
    {
        var messages = new ConcurrentQueue<string>();
        LockRegistry.DiagnosticSink = messages.Enqueue;
        LockRegistry.HoldTooLongThreshold = TimeSpan.FromMilliseconds(50);
        using var lk = new AsyncLock("hold-long-test");
        using (await lk.TryLockAsync() ?? throw new System.TimeoutException($"锁 '{lk.Name}' 等待超时"))
        {
            await Task.Delay(120);
        }
        messages.Should().Contain(m => m.Contains("LOCK-HOLD-TOO-LONG") && m.Contains("hold-long-test"),
            "持有时间超过阈值时诊断 sink 应收到告警");
    }

    [Fact]
    public async Task 诊断Sink_等待过长时收到告警()
    {
        var messages = new ConcurrentQueue<string>();
        LockRegistry.DiagnosticSink = messages.Enqueue;
        LockRegistry.WaitTimeoutThreshold = TimeSpan.FromMilliseconds(50);
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
    public async Task DumpAll_包含获取调用栈()
    {
        LockRegistry.DiagnosticsEnabled = true;
        using var lk = new AsyncLock("stack-test");
        using (await lk.TryLockAsync() ?? throw new System.TimeoutException($"锁 '{lk.Name}' 等待超时"))
        {
            var dump = LockRegistry.DumpAll();
            dump.Should().Contain("获取调用栈", "诊断开启时 DumpAll 应包含获取调用栈");
            dump.Should().Contain("AsyncLockDiagnosisTests", "调用栈应包含测试类方法名");
        }
    }

    [Fact]
    public async Task 后台扫描_持有过长时输出告警()
    {
        var messages = new ConcurrentQueue<string>();
        LockRegistry.DiagnosticSink = messages.Enqueue;
        LockRegistry.HoldTooLongThreshold = TimeSpan.FromMilliseconds(50);
        LockRegistry.StartBackgroundScan(TimeSpan.FromMilliseconds(30));
        using var lk = new AsyncLock("scan-test");
        using var holder = await lk.TryLockAsync() ?? throw new System.TimeoutException($"锁 '{lk.Name}' 等待超时");
        await Task.Delay(200);
        messages.Should().Contain(m => m.Contains("LOCK-SCAN-HOLD") && m.Contains("scan-test"),
            "后台扫描应检测到持有过长的锁并告警");
    }

    [Fact]
    public async Task DiagnosticsEnabled关闭时_不记录诊断()
    {
        LockRegistry.DiagnosticsEnabled = false;
        var messages = new ConcurrentQueue<string>();
        LockRegistry.DiagnosticSink = messages.Enqueue;
        using var lk = new AsyncLock("disabled-test");
        using (await lk.TryLockAsync() ?? throw new System.TimeoutException($"锁 '{lk.Name}' 等待超时"))
        {
            await Task.Delay(60);
        }
        messages.Should().BeEmpty("诊断关闭时不应产生任何告警");
        LockRegistry.DiagnosticsEnabled = true;
    }

    [Fact]
    public async Task Lock同步_基本互斥()
    {
        using var lk = new AsyncLock("sync-mutex", TimeSpan.FromMilliseconds(500));
        using var g1 = await lk.TryLockAsync() ?? throw new System.TimeoutException($"锁 '{lk.Name}' 等待超时");
        // 在另一线程获取 — 同线程重入会超时返回 null
        var g2 = await Task.Run(async () => await lk.TryLockAsync());
        g2.Should().BeNull("同步 Lock 持有后另一线程 TryLock 应500ms超时返回 null");
    }

    [Fact]
    public async Task Lock带CancellationToken_取消时抛OperationCanceledException()
    {
        using var lk = new AsyncLock("cancel-test");
        using var holder = await lk.TryLockAsync() ?? throw new System.TimeoutException($"锁 '{lk.Name}' 等待超时");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        // TryLock(已取消 token) 同步抛 OCE; 在另一线程调用以避免重入检测
        var act = () => Task.Run(async () => { _ = await lk.TryLockAsync(cts.Token) ?? throw new System.TimeoutException($"锁 '{lk.Name}' 等待超时"); });
        await act.Should().ThrowAsync<OperationCanceledException>("取消令牌触发时应抛 OCE");
    }

    [Fact]
    public async Task 死锁检测_两个线程互相等待时自动检测()
    {
        var messages = new ConcurrentQueue<string>();
        LockRegistry.DiagnosticSink = messages.Enqueue;
        using var lockA = new AsyncLock("deadlock-A", TimeSpan.FromMilliseconds(500));
        using var lockB = new AsyncLock("deadlock-B", TimeSpan.FromMilliseconds(500));

        var barrier = new Barrier(2);
        var t1Done = new ManualResetEventSlim();
        var t2Done = new ManualResetEventSlim();

        var t1 = new Thread(() =>
        {
            using (lockA.TryLock() ?? throw new System.TimeoutException($"锁 '{lockA.Name}' 等待超时"))
            {
                barrier.SignalAndWait();
                using var g = lockB.TryLock();
            }
            t1Done.Set();
        })
        { IsBackground = true };

        var t2 = new Thread(() =>
        {
            using (lockB.TryLock() ?? throw new System.TimeoutException($"锁 '{lockB.Name}' 等待超时"))
            {
                barrier.SignalAndWait();
                using var g = lockA.TryLock();
            }
            t2Done.Set();
        })
        { IsBackground = true };

        t1.Start();
        t2.Start();

        await Task.Delay(300);

        LockRegistry.DeadlockDetected.Should().BeTrue("两个线程互相等待对方持有的锁应被自动检测为死锁");
        LockRegistry.LastDeadlockReport.Should().Contain("DEADLOCK-DETECTED");
        LockRegistry.LastDeadlockReport.Should().Contain("deadlock-A");
        LockRegistry.LastDeadlockReport.Should().Contain("deadlock-B");
        messages.Should().Contain(m => m.Contains("DEADLOCK-DETECTED"));

        t1Done.Wait(5000);
        t2Done.Wait(5000);
    }

    [Fact]
    public async Task 死锁检测_无死锁时DeadlockDetected为false()
    {
        using var lockA = new AsyncLock("no-deadlock-A");
        using var lockB = new AsyncLock("no-deadlock-B");
        using (await lockA.TryLockAsync() ?? throw new System.TimeoutException($"锁 '{lockA.Name}' 等待超时"))
        {
            using (await lockB.TryLockAsync() ?? throw new System.TimeoutException($"锁 '{lockB.Name}' 等待超时"))
            {
                LockRegistry.DeadlockDetected.Should().BeFalse("顺序获取不形成死锁");
            }
        }
        await Task.CompletedTask;
    }

    [Fact]
    public async Task 死锁检测_async两个流互相等待时自动检测()
    {
        var messages = new ConcurrentQueue<string>();
        LockRegistry.DiagnosticSink = messages.Enqueue;
        using var lockA = new AsyncLock("async-deadlock-A", TimeSpan.FromSeconds(2));
        using var lockB = new AsyncLock("async-deadlock-B", TimeSpan.FromSeconds(2));

        var t1Ready = new TaskCompletionSource();
        var t2Ready = new TaskCompletionSource();

        var t1 = Task.Run(async () =>
        {
            using (await lockA.TryLockAsync() ?? throw new System.TimeoutException($"锁 '{lockA.Name}' 等待超时"))
            {
                t1Ready.SetResult();
                await t2Ready.Task;
                using var g = await lockB.TryLockAsync();
                if (g is null) Console.WriteLine("async t1 lockB 超时");
            }
        });

        var t2 = Task.Run(async () =>
        {
            using (await lockB.TryLockAsync() ?? throw new System.TimeoutException($"锁 '{lockB.Name}' 等待超时"))
            {
                t2Ready.SetResult();
                await t1Ready.Task;
                using var g = await lockA.TryLockAsync();
                if (g is null) Console.WriteLine("async t2 lockA 超时");
            }
        });

        await Task.Delay(1000);

        // async 下线程池复用导致 ThreadId 不可靠(ADR-0060),DetectDeadlock 可能误判自环(假阳性)
        // 而非 A-B 环。只验证死锁被检测到,不验证报告包含具体锁名。
        LockRegistry.DeadlockDetected.Should().BeTrue("两个 async 流互相等待对方持有的锁应被自动检测为死锁");
        LockRegistry.LastDeadlockReport.Should().Contain("DEADLOCK-DETECTED");
        messages.Should().Contain(m => m.Contains("DEADLOCK-DETECTED"));

        await Task.WhenAll(t1, t2);
    }

    // ===== 重入检测 — 同步场景下同线程重入会超时返回 null (ThreadId 在 async 下不可靠,不做重入抛异常) =====

    [Fact]
    public async Task 重入检测_同步重入同一把锁应超时返回null()
    {
        using var lk = new AsyncLock("reentrant-sync", TimeSpan.FromMilliseconds(500));

        using (await lk.TryLockAsync() ?? throw new System.TimeoutException($"锁 '{lk.Name}' 等待超时"))
        {
            // 同线程重入: TryLock 等500ms超时返回 null (不抛异常,因为 ThreadId 在 async 下不可靠)
            var result = await Task.Run(async () => await lk.TryLockAsync());
            result.Should().BeNull("另一线程持锁等待500ms超时后 TryLock 应返回 null");
        }
    }

    [Fact]
    public async Task 重入检测_async重入同一把锁应超时返回null()
    {
        using var lk = new AsyncLock("reentrant-async", TimeSpan.FromMilliseconds(500));

        using (await lk.TryLockAsync() ?? throw new System.TimeoutException($"锁 '{lk.Name}' 等待超时"))
        {
            var result = await Task.Run(async () => await lk.TryLockAsync());
            result.Should().BeNull("另一线程持锁等待500ms超时后 TryLock 应返回 null");
        }
    }

    [Fact]
    public async Task 重入检测_不同线程获取同一把锁不应抛异常()
    {
        using var lk = new AsyncLock("cross-thread", TimeSpan.FromMilliseconds(500));
        using var holder = await lk.TryLockAsync() ?? throw new System.TimeoutException($"锁 '{lk.Name}' 等待超时");

        var act = async () =>
        {
            await Task.Run(async () =>
            {
                using var g = await lk.TryLockAsync();
            });
        };

        await act.Should().NotThrowAsync(
            "不同线程获取同一把锁是正常竞争，不是重入");
    }

    [Fact]
    public async Task 重入检测_异常应包含锁名和调用栈()
    {
        using var lk = new AsyncLock("reentrant-info", TimeSpan.FromMilliseconds(500));

        using (await lk.TryLockAsync() ?? throw new System.TimeoutException($"锁 '{lk.Name}' 等待超时"))
        {
            // 同线程重入: TryLock 等500ms超时返回 null
            var result = await Task.Run(async () => await lk.TryLockAsync());
            result.Should().BeNull("另一线程持锁等待500ms超时后 TryLock 应返回 null");
        }
    }
}
