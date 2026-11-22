namespace Dream.Tests.Services;

/// <summary>
/// TASK126（TASK103 #40）— 自动触发六门：配置门 / 时间门 / 扫描节流 / 会话门 / 占锁 / 执行。
/// <para>
/// 这些用例存在的理由不是「覆盖率」，而是 #40 最贵的那个坑：把触发点接上却不修时间门，
/// 等于给每次对话末尾加一次 LLM 调用。所以每条断言都对着「本回合该不该花钱」。
/// </para>
/// </summary>
public sealed class AutoDreamTriggerTests {
    private readonly Mock<IDreamFeature> _dream;
    private readonly Mock<ISessionScanner> _scanner;
    private readonly InMemoryFileSystem _fs;
    private readonly ConsolidationLock _lock;

    public AutoDreamTriggerTests() {
        _dream = new Mock<IDreamFeature>();
        _scanner = new Mock<ISessionScanner>();
        _fs = new InMemoryFileSystem();
        _lock = new ConsolidationLock(new AutoDreamConfig { ProjectDir = "/proj" }, _fs);
        _scanner.Setup(s => s.ListSessionsTouchedSinceAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(["s1"]);
    }

    private AutoDreamTrigger CreateTrigger(AutoDreamConfig config, Func<int, bool>? alive = null) =>
        new(_dream.Object, _scanner.Object, config, _lock, NullLogger<AutoDreamTrigger>.Instance, alive);

    private void SetupSuccess() =>
        _dream.Setup(d => d.ExecuteAsync(It.IsAny<DreamRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DreamResult.Success("ok", "task-1", 1, 5));

    private async Task<long> ReadLockAsync() => await _lock.ReadLastConsolidatedAtAsync().ConfigureAwait(true);

    [Fact]
    public async Task Fire_Disabled_ReturnsDisabled() {
        var trigger = CreateTrigger(new AutoDreamConfig { Enabled = false });

        (await trigger.FireAsync(null).ConfigureAwait(true)).Should().Be(AutoDreamTriggerOutcome.Disabled);
        _dream.Verify(d => d.ExecuteAsync(It.IsAny<DreamRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Fire_AutoMemoryDisabled_ReturnsMemoryDisabled() {
        var trigger = CreateTrigger(new AutoDreamConfig { AutoMemoryEnabled = false });

        (await trigger.FireAsync(null).ConfigureAwait(true)).Should().Be(AutoDreamTriggerOutcome.MemoryDisabled);
        _dream.Verify(d => d.ExecuteAsync(It.IsAny<DreamRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>时间门读真锁值：刚合并完（1 小时前）不得再触发 —— 这正是「假时间门」会放行的一格。</summary>
    [Fact]
    public async Task Fire_WithinMinHours_ReturnsTimeGate() {
        await _fs.WriteAllTextAsync(
            Path.Combine("/proj", ConsolidationLock.LockFileName),
            DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture), CancellationToken.None).ConfigureAwait(true);
        var trigger = CreateTrigger(new AutoDreamConfig { MinHours = 24, MinSessions = 1, ProjectDir = "/proj" });

        (await trigger.FireAsync(null).ConfigureAwait(true)).Should().Be(AutoDreamTriggerOutcome.TimeGate);
        _dream.Verify(d => d.ExecuteAsync(It.IsAny<DreamRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>时间门过了也不每回合扫目录：第二次调用落在节流窗口内。</summary>
    [Fact]
    public async Task Fire_SecondCallImmediately_ReturnsScanThrottle() {
        var trigger = CreateTrigger(new AutoDreamConfig { MinHours = 0, MinSessions = 5 });

        (await trigger.FireAsync(null).ConfigureAwait(true)).Should().Be(AutoDreamTriggerOutcome.SessionGate);
        (await trigger.FireAsync(null).ConfigureAwait(true)).Should().Be(AutoDreamTriggerOutcome.ScanThrottle);

        _scanner.Verify(s => s.ListSessionsTouchedSinceAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Fire_SessionsBelowMin_ReturnsSessionGate() {
        var trigger = CreateTrigger(new AutoDreamConfig { MinHours = 0, MinSessions = 5 });

        (await trigger.FireAsync(null).ConfigureAwait(true)).Should().Be(AutoDreamTriggerOutcome.SessionGate);
        _dream.Verify(d => d.ExecuteAsync(It.IsAny<DreamRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>当前会话的 transcript 恒为「刚写过」，不排除就会自己把自己凑成达标（基准 autoDream.ts:163）。</summary>
    [Fact]
    public async Task Fire_ExcludesCurrentSession() {
        _scanner.Setup(s => s.ListSessionsTouchedSinceAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(["cur", "s1"]);
        var trigger = CreateTrigger(new AutoDreamConfig { MinHours = 0, MinSessions = 2 });

        (await trigger.FireAsync("cur").ConfigureAwait(true)).Should().Be(AutoDreamTriggerOutcome.SessionGate);
    }

    [Fact]
    public async Task Fire_LockHeldByLiveProcess_ReturnsLockHeld() {
        await _fs.WriteAllTextAsync(
            Path.Combine("/proj", ConsolidationLock.LockFileName),
            $"424242:{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}", CancellationToken.None).ConfigureAwait(true);
        var trigger = CreateTrigger(
            new AutoDreamConfig { MinHours = 0, MinSessions = 1, ProjectDir = "/proj" },
            alive: _ => true);

        (await trigger.FireAsync(null).ConfigureAwait(true)).Should().Be(AutoDreamTriggerOutcome.LockHeld);
        _dream.Verify(d => d.ExecuteAsync(It.IsAny<DreamRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Fire_Success_WritesLockAndPassesSessionIds() {
        SetupSuccess();
        DreamRequest? captured = null;
        _dream.Setup(d => d.ExecuteAsync(It.IsAny<DreamRequest>(), It.IsAny<CancellationToken>()))
            .Callback((DreamRequest r, CancellationToken _) => captured = r)
            .ReturnsAsync(DreamResult.Success("ok", "task-1", 1, 5));
        var trigger = CreateTrigger(new AutoDreamConfig { MinHours = 0, MinSessions = 1 });

        (await trigger.FireAsync("other").ConfigureAwait(true)).Should().Be(AutoDreamTriggerOutcome.Fired);

        Assert.NotNull(captured);
        captured.Force.Should().BeTrue("自动触发已过门，执行体不应再自行判门");
        captured.SessionIds.Should().Equal("s1");
        (await ReadLockAsync().ConfigureAwait(true)).Should().BeGreaterThan(0, "成功后必须落锁，否则下回合又来一次");
    }

    /// <summary>失败必须回滚锁 —— 否则时间门把接下来 24 小时全挡掉，而记忆一条都没整合。</summary>
    [Fact]
    public async Task Fire_DreamFails_RollsBackLock() {
        var prior = DateTimeOffset.UtcNow.AddHours(-25).ToUnixTimeMilliseconds();
        await _fs.WriteAllTextAsync(
            Path.Combine("/proj", ConsolidationLock.LockFileName),
            prior.ToString(System.Globalization.CultureInfo.InvariantCulture), CancellationToken.None).ConfigureAwait(true);
        _dream.Setup(d => d.ExecuteAsync(It.IsAny<DreamRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DreamResult.Failure("boom"));
        var trigger = CreateTrigger(new AutoDreamConfig { MinHours = 24, MinSessions = 1, ProjectDir = "/proj" });

        (await trigger.FireAsync(null).ConfigureAwait(true)).Should().Be(AutoDreamTriggerOutcome.Failed);

        (await ReadLockAsync().ConfigureAwait(true)).Should().Be(prior, "失败要回到占锁前的时刻，使下回合可重试");
    }

    [Fact]
    public async Task Fire_DreamThrows_ReturnsFailedAndDoesNotThrow() {
        _dream.Setup(d => d.ExecuteAsync(It.IsAny<DreamRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("llm down"));
        var trigger = CreateTrigger(new AutoDreamConfig { MinHours = 0, MinSessions = 1 });

        (await trigger.FireAsync(null).ConfigureAwait(true)).Should().Be(AutoDreamTriggerOutcome.Failed);
    }

    [Fact]
    public async Task Fire_Cancelled_ReturnsFailedAndRollsBack() {
        _dream.Setup(d => d.ExecuteAsync(It.IsAny<DreamRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        var trigger = CreateTrigger(new AutoDreamConfig { MinHours = 0, MinSessions = 1 });
        using var cts = new CancellationTokenSource();

        (await trigger.FireAsync(null, cts.Token).ConfigureAwait(true)).Should().Be(AutoDreamTriggerOutcome.Failed);
        (await ReadLockAsync().ConfigureAwait(true)).Should().Be(0L, "取消也要回滚（删锁），否则时间门永久关闭");
    }

    /// <summary>进程内互斥：一个在跑时后续回合立即让出，不再走 IO。</summary>
    [Fact]
    public async Task Fire_WhileRunning_ReturnsLockHeld() {
        var entered = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        _dream.Setup(d => d.ExecuteAsync(It.IsAny<DreamRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async (DreamRequest request, CancellationToken token) => {
                entered.TrySetResult();
                await release.Task.ConfigureAwait(true);
                return DreamResult.Success("ok", "task-1", 1, 5);
            });
        var trigger = CreateTrigger(new AutoDreamConfig { MinHours = 0, MinSessions = 1 });

        var first = trigger.FireAsync(null);
        await entered.Task.ConfigureAwait(true);

        (await trigger.FireAsync(null).ConfigureAwait(true)).Should().Be(AutoDreamTriggerOutcome.LockHeld);

        release.TrySetResult();
        (await first.ConfigureAwait(true)).Should().Be(AutoDreamTriggerOutcome.Fired);
    }
}
