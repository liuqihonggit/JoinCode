namespace Dream.Tests.Client;

/// <summary>
/// TASK071 服务域-②③ — ConsolidationLock 行为锁：
/// 内容承载时间戳（跨重启持久）、kill 回滚到 PriorMtime（时间门重新放行）、当前会话排除由 gate 消费。
/// </summary>
public sealed class ConsolidationLockBehaviorTests {
    private static (ConsolidationLock Lock, InMemoryFileSystem Fs) CreateLock(string projectDir = "/proj") {
        var fs = new InMemoryFileSystem();
        var config = new AutoDreamConfig { ProjectDir = projectDir };
        return (new ConsolidationLock(config, fs), fs);
    }

    [Fact]
    public async Task Read_NeverConsolidated_ReturnsZero() {
        var (lockFile, _) = CreateLock();

        (await lockFile.ReadLastConsolidatedAtAsync().ConfigureAwait(true)).Should().Be(0L);
    }

    [Fact]
    public async Task WriteNow_ThenRead_ReturnsRecentTimestamp() {
        var (lockFile, _) = CreateLock();
        var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await lockFile.WriteNowAsync().ConfigureAwait(true);

        var read = await lockFile.ReadLastConsolidatedAtAsync().ConfigureAwait(true);
        read.Should().BeInRange(before, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 60_000,
            "落锁后读取应得写入时刻");
    }

    [Fact]
    public async Task Rollback_PriorMtime_TimeGateReopens() {
        var (lockFile, _) = CreateLock();
        await lockFile.WriteNowAsync().ConfigureAwait(true);

        var prior = DateTimeOffset.UtcNow.AddHours(-25).ToUnixTimeMilliseconds();
        await lockFile.RollbackAsync(prior).ConfigureAwait(true);

        (await lockFile.ReadLastConsolidatedAtAsync().ConfigureAwait(true)).Should().Be(prior,
            "kill 回滚后时间门应重新放行（读回 prior 值）");
    }

    [Fact]
    public async Task Rollback_ZeroPrior_DeletesLockFile() {
        var (lockFile, fs) = CreateLock();
        await lockFile.WriteNowAsync().ConfigureAwait(true);

        await lockFile.RollbackAsync(0).ConfigureAwait(true);

        (await lockFile.ReadLastConsolidatedAtAsync().ConfigureAwait(true)).Should().Be(0L,
            "prior=0（从未合并）回滚 = 删除锁文件");
        fs.FileExists(Path.Combine("/proj", ConsolidationLock.LockFileName)).Should().BeFalse();
    }

    /// <summary>kill 注册表链 — 注册带回滚回调的任务，Kill 触发锁回滚（旧实现空操作）。</summary>
    [Fact]
    public async Task KillDreamTask_InvokesLockRollback() {
        var fs = new InMemoryFileSystem();
        var config = new AutoDreamConfig { ProjectDir = "/proj" };
        var consolidationLock = new ConsolidationLock(config, fs);
        await consolidationLock.WriteNowAsync().ConfigureAwait(true);
        var prior = DateTimeOffset.UtcNow.AddHours(-30).ToUnixTimeMilliseconds();
        await using var registry = new InMemoryDreamTaskRegistry();

        var taskId = await registry.RegisterDreamTaskAsync(new DreamTaskRegistrationRequest(
            SessionsReviewing: 3,
            PriorMtime: prior,
            AbortController: new CancellationTokenSource(),
            RollbackLockAsync: (p, _) => consolidationLock.RollbackAsync(p))).ConfigureAwait(true);

        await registry.KillDreamTaskAsync(taskId).ConfigureAwait(true);

        (await consolidationLock.ReadLastConsolidatedAtAsync().ConfigureAwait(true)).Should().Be(prior,
            "kill 应回滚锁到注册时的 PriorMtime（时间门重新放行，可安全重试）");
        var task = await registry.GetTaskStateAsync(taskId).ConfigureAwait(true);
        Assert.NotNull(task);
        task.Status.Should().Be(DreamTaskStatus.Killed);
    }
}
