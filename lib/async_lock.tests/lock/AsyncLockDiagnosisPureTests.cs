namespace LockDiagnosis.Tests;

/// <summary>
/// AsyncLock 诊断能力 — 确定性部分(纯诊断输出/异常语义/取消语义,不涉时序竞争与 Task.Delay)。
/// <para>拆分自 AsyncLockDiagnosisTests,与 AsyncLockDiagnosisTests(时序部分)互补。</para>
/// <para>构造/Dispose 与原类一致:重置 LockRegistry 状态,隔离测试。</para>
/// </summary>
public class AsyncLockDiagnosisPureTests : IDisposable {
    public AsyncLockDiagnosisPureTests() {
        LockRegistry.ClearForTesting();
        LockRegistry.DiagnosticsEnabled = true;
        LockRegistry.HoldTooLongThreshold = TimeSpan.FromMilliseconds(100);
        LockRegistry.WaitTimeoutThreshold = TimeSpan.FromMilliseconds(200);
    }

    public void Dispose() {
        LockRegistry.StopBackgroundScan();
        LockRegistry.DiagnosticSink = null;
    }

    [Fact]
    [Trait("Category", "Deterministic")]
    public async Task 具名构造_锁名出现在DumpAll() {
        using var lk = new AsyncLock("my-test-lock");
        using (await lk.TryLockAsync() ?? throw new System.TimeoutException($"锁 '{lk.Name}' 等待超时")) {
            var dump = LockRegistry.DumpAll();
            dump.Should().Contain("my-test-lock", "具名锁的名称应出现在 DumpAll 输出中");
            dump.Should().Contain("持有中", "已获取的锁应显示持有中状态");
        }
    }

    [Fact]
    [Trait("Category", "Deterministic")]
    public Task DumpAll_空闲锁显示空闲() {
        using var lk = new AsyncLock("idle-lock");
        var dump = LockRegistry.DumpAll();
        dump.Should().Contain("idle-lock");
        dump.Should().Contain("空闲", "未获取的锁应显示空闲");
        return Task.CompletedTask;
    }

    [Fact]
    [Trait("Category", "Deterministic")]
    public async Task LockRegistry_Count_构造增加_Dispose减少() {
        LockRegistry.ClearForTesting();
        var lk = new AsyncLock("count-test");
        LockRegistry.Count.Should().Be(1, "构造一把锁后注册表应有1条");
        lk.Dispose();
        LockRegistry.Count.Should().Be(0, "Dispose 后应从注册表移除");
        await Task.CompletedTask;
    }

    [Fact]
    [Trait("Category", "Deterministic")]
    public async Task Dispose后_LockAsync抛ObjectDisposedException() {
        var lk = new AsyncLock("disposed-test");
        lk.Dispose();
        // Dispose 后 TryLock 同步抛 ObjectDisposedException
        Func<Task> act = async () => await lk.TryLockAsync();
        await act.Should().ThrowAsync<ObjectDisposedException>("Dispose 后再获取应抛 ObjectDisposedException");
        await Task.CompletedTask;
    }

    [Fact]
    [Trait("Category", "Deterministic")]
    public async Task DumpAll_包含获取调用栈() {
        LockRegistry.DiagnosticsEnabled = true;
        using var lk = new AsyncLock("stack-test");
        using (await lk.TryLockAsync() ?? throw new System.TimeoutException($"锁 '{lk.Name}' 等待超时")) {
            var dump = LockRegistry.DumpAll();
            dump.Should().Contain("获取调用栈", "诊断开启时 DumpAll 应包含获取调用栈");
            dump.Should().Contain("AsyncLockDiagnosisPureTests", "调用栈应包含测试类方法名(拆分后类名更新)");
        }
    }

    [Fact]
    [Trait("Category", "Deterministic")]
    public async Task Lock带CancellationToken_取消时抛OperationCanceledException() {
        using var lk = new AsyncLock("cancel-test");
        using var holder = await lk.TryLockAsync() ?? throw new System.TimeoutException($"锁 '{lk.Name}' 等待超时");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        // TryLock(已取消 token) 同步抛 OCE; 在另一线程调用以避免重入检测
        var act = () => Task.Run(async () => { _ = await lk.TryLockAsync(cts.Token) ?? throw new System.TimeoutException($"锁 '{lk.Name}' 等待超时"); });
        await act.Should().ThrowAsync<OperationCanceledException>("取消令牌触发时应抛 OCE");
    }

    [Fact]
    [Trait("Category", "Deterministic")]
    public async Task 死锁检测_无死锁时DeadlockDetected为false() {
        using var lockA = new AsyncLock("no-deadlock-A");
        using var lockB = new AsyncLock("no-deadlock-B");
        using (await lockA.TryLockAsync() ?? throw new System.TimeoutException($"锁 '{lockA.Name}' 等待超时")) {
            using (await lockB.TryLockAsync() ?? throw new System.TimeoutException($"锁 '{lockB.Name}' 等待超时")) {
                LockRegistry.DeadlockDetected.Should().BeFalse("顺序获取不形成死锁");
            }
        }
        await Task.CompletedTask;
    }
}
