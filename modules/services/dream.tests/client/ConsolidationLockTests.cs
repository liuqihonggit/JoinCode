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

    private static string LockPath(string projectDir = "/proj") =>
        Path.Combine(projectDir, ConsolidationLock.LockFileName);

    private static async Task WriteRawAsync(InMemoryFileSystem fs, string body) =>
        await fs.WriteAllTextAsync(LockPath(), body, CancellationToken.None).ConfigureAwait(true);

    // === TASK126（#40）原子占锁 ===

    [Fact]
    public async Task TryAcquire_NoLock_ReturnsZeroAndStampsProcessId() {
        var (lockFile, fs) = CreateLock();

        var prior = await lockFile.TryAcquireAsync(isProcessAlive: _ => true).ConfigureAwait(true);

        prior.Should().Be(0L, "从未合并时返回 0（失败回滚 = 删文件）");
        var body = await fs.ReadAllTextAsync(LockPath()).ConfigureAwait(true);
        ConsolidationLock.ParseBody(body).Pid.Should().Be(Environment.ProcessId);
    }

    [Fact]
    public async Task TryAcquire_HeldByLiveProcess_YieldsNull() {
        var (lockFile, fs) = CreateLock();
        await WriteRawAsync(fs, $"424242:{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}").ConfigureAwait(true);

        var prior = await lockFile.TryAcquireAsync(isProcessAlive: _ => true).ConfigureAwait(true);

        prior.Should().BeNull("持有者活着且在过期窗口内 —— 再抢一份就是双份 LLM 账单");
    }

    [Fact]
    public async Task TryAcquire_DeadHolder_ReclaimsAndReturnsPriorTimestamp() {
        var (lockFile, fs) = CreateLock();
        var priorTs = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeMilliseconds();
        await WriteRawAsync(fs, $"999999:{priorTs}").ConfigureAwait(true);

        var prior = await lockFile.TryAcquireAsync(isProcessAlive: _ => false).ConfigureAwait(true);

        prior.Should().Be(priorTs, "死进程（崩溃残留）必须可回收，否则时间门永久卡死");
    }

    [Fact]
    public async Task TryAcquire_StaleHolder_ReclaimsEvenIfPidAlive() {
        var (lockFile, fs) = CreateLock();
        var priorTs = DateTimeOffset.UtcNow.AddHours(-2).ToUnixTimeMilliseconds();
        await WriteRawAsync(fs, $"424242:{priorTs}").ConfigureAwait(true);

        var prior = await lockFile.TryAcquireAsync(isProcessAlive: _ => true).ConfigureAwait(true);

        prior.Should().Be(priorTs, $"持锁超过 {ConsolidationLock.HolderStaleMs / 3_600_000}h 视为过期（防 pid 复用）");
    }

    [Fact]
    public async Task TryAcquire_LegacyPureTimestamp_IsReclaimable() {
        var (lockFile, fs) = CreateLock();
        var priorTs = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeMilliseconds();
        await WriteRawAsync(fs, priorTs.ToString(System.Globalization.CultureInfo.InvariantCulture)).ConfigureAwait(true);

        var prior = await lockFile.TryAcquireAsync(isProcessAlive: _ => true).ConfigureAwait(true);

        prior.Should().Be(priorTs, "TASK071 旧格式（纯时间戳、无 pid）只是完成时刻，不是持有者");
    }

    [Fact]
    public void ParseBody_HandlesBothFormats() {
        ConsolidationLock.ParseBody("1234:5678").Should().Be((1234, 5678L));
        ConsolidationLock.ParseBody(" 5678 ").Should().Be((0, 5678L));
        ConsolidationLock.ParseBody(null).Should().Be((0, 0L));
        ConsolidationLock.ParseBody("garbage").Should().Be((0, 0L));
    }

    [Fact]
    public async Task WriteNow_StampsPidSoNextAcquireSeesLiveHolder() {
        var (lockFile, fs) = CreateLock();
        await lockFile.WriteNowAsync().ConfigureAwait(true);

        var prior = await lockFile.TryAcquireAsync(isProcessAlive: _ => true).ConfigureAwait(true);

        prior.Should().BeNull("刚合并完的锁由本进程持有，任何回合都不该再触发一次");
    }

    [Fact]
    public async Task Read_NeverConsolidated_ReturnsZero() {
        var (lockFile, fs) = CreateLock();

        (await lockFile.ReadLastConsolidatedAtAsync().ConfigureAwait(true)).Should().Be(0L);
    }

    [Fact]
    public async Task WriteNow_ThenRead_ReturnsRecentTimestamp() {
        var (lockFile, fs) = CreateLock();
        var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await lockFile.WriteNowAsync().ConfigureAwait(true);

        var read = await lockFile.ReadLastConsolidatedAtAsync().ConfigureAwait(true);
        read.Should().BeInRange(before, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 60_000,
            "落锁后读取应得写入时刻");
    }

    [Fact]
    public async Task Rollback_PriorMtime_TimeGateReopens() {
        var (lockFile, fs) = CreateLock();
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
