namespace NativeTui.Tests.Repl;

/// <summary>
/// RawStdinPump 全链测试（DSG042 P2）— FeedForTest 注入绕过 stdin Read，验证
/// 解析→通道→Drain 全链：单键/VT 序列（方向键修饰组合 ReadKey 通道不可得的形态）/
/// bracketed paste 整块（重组状态机天然不需要）/ 不完整前缀跨块攒齐 / 孤立 ESC 超时产出 /
/// 键序保序 / 空排空返回 false。真实 stdin 阻塞行为由宿主 exe 手动验收（ADR 0080）。
/// </summary>
public sealed class RawStdinPumpTests
{
    /// <summary>手动时钟 — 前缀滞留计时完全由测试推进（消除全量并行调度导致的真实时间竞态）。</summary>
    private sealed class ManualClock
    {
        private long _now;

        public long Now => Volatile.Read(ref _now);

        public void Advance(long ms) => Volatile.Write(ref _now, Now + ms);
    }

    /// <summary>构造即启动读线程（IsBackground — stdin Read 阻塞由进程退出回收；测试不依赖它）。
    /// 默认注入冻结时钟（0）— 前缀滞留策略只由测试显式推进的 <see cref="ManualClock"/> 触发。</summary>
    private static RawStdinPump CreatePump(ManualClock? clock = null) => new(clock: () => clock?.Now ?? 0);

    /// <summary>注入并排空全部产出键（Drain 非阻塞 — feed 后立即收）。</summary>
    private static List<KeyPress> FeedAndDrain(RawStdinPump pump, string text)
    {
        pump.FeedForTest(text);
        return DrainKeys(pump);
    }

    /// <summary>非阻塞排空当前产出键。</summary>
    private static List<KeyPress> DrainKeys(RawStdinPump pump)
    {
        var keys = new List<KeyPress>();
        pump.Drain(keys.Add);
        return keys;
    }

    [Fact]
    public void SingleChar_ProducesCharKey()
    {
        using var pump = CreatePump();
        var keys = FeedAndDrain(pump, "a");
        var key = Assert.Single(keys);
        Assert.Equal(KeyKind.Char, key.Kind);
        Assert.Equal("a", key.Input);
    }

    [Fact]
    public void CtrlSequence_ProducesCtrlKey()
    {
        using var pump = CreatePump();
        var keys = FeedAndDrain(pump, "\u0015");
        var key = Assert.Single(keys);
        Assert.True(key.Ctrl);
        Assert.Equal("u", key.Input);
    }

    [Fact]
    public void ArrowSequence_ProducesNamedKey()
    {
        using var pump = CreatePump();
        var keys = FeedAndDrain(pump, "\u001b[D");
        var key = Assert.Single(keys);
        Assert.Equal(KeyKind.LeftArrow, key.Kind);
    }

    [Fact]
    public void CtrlShiftArrow_ModifierCombo_Preserved()
    {
        // ReadKey 通道不可得形态：ctrl+shift+方向（CSI 1;5D = ctrl+shift 修饰? 实际 5=ctrl — 用 1;5D 验证修饰位保留）
        using var pump = CreatePump();
        var keys = FeedAndDrain(pump, "\u001b[1;5D");
        var key = Assert.Single(keys);
        Assert.Equal(KeyKind.LeftArrow, key.Kind);
        Assert.True(key.Ctrl);
    }

    [Fact]
    public void BracketedPaste_WholeBlock_SinglePasteEvent()
    {
        using var pump = CreatePump();
        var keys = FeedAndDrain(pump, "\u001b[200~hello world\u001b[201~");
        var key = Assert.Single(keys);
        Assert.Equal(KeyKind.Paste, key.Kind);
        Assert.Equal("hello world", key.Input);
    }

    [Fact]
    public void PasteBlock_SplitAcrossFeeds_Accumulates()
    {
        // 终止标记未到达 = 不完整前缀不消耗（跨块攒齐 — KeyPressParser 契约）
        using var pump = CreatePump();
        var first = FeedAndDrain(pump, "\u001b[200~hel");
        Assert.Empty(first);

        var second = FeedAndDrain(pump, "lo\u001b[201~");
        var key = Assert.Single(second);
        Assert.Equal(KeyKind.Paste, key.Kind);
        Assert.Equal("hello", key.Input);
    }

    [Fact]
    public void IncompleteEscapePrefix_AccumulatesAcrossFeeds()
    {
        var clock = new ManualClock();
        using var pump = CreatePump(clock);
        var first = FeedAndDrain(pump, "\u001b[");
        Assert.Empty(first);

        // 推进 49ms（未超 50ms 阈值）— 真实调度延迟再久也不 flush（原缺陷：真实时钟在 Feed 与
        // 首次 Drain 之间超过 50ms 时强制拆成 Escape + "["，全量并行下偶发）
        clock.Advance(49);
        Assert.Empty(DrainKeys(pump));

        var second = FeedAndDrain(pump, "D");
        var key = Assert.Single(second);
        Assert.Equal(KeyKind.LeftArrow, key.Kind);
    }

    /// <summary>跨块攒齐重复 16 轮 — 每轮 split ESC 前缀都不丢键不 flush（时序压力回归，时钟由测试推进）。</summary>
    [Fact]
    public void IncompleteEscapePrefix_RepeatedSplit_AccumulatesEveryRound()
    {
        var clock = new ManualClock();
        using var pump = CreatePump(clock);
        for (var round = 0; round < 16; round++)
        {
            clock.Advance(40);
            Assert.Empty(FeedAndDrain(pump, "\u001b["));

            var key = Assert.Single(FeedAndDrain(pump, "D"));
            Assert.Equal(KeyKind.LeftArrow, key.Kind);
            Assert.Equal(0, pump.PendingCount);
        }
    }

    [Fact]
    public void LoneEscape_ChunkBoundary_WaitsForContinuation()
    {
        var clock = new ManualClock();
        using var pump = CreatePump(clock);
        Assert.Empty(FeedAndDrain(pump, "\u001b"));

        clock.Advance(51);
        var key = Assert.Single(DrainKeys(pump));
        Assert.Equal(KeyKind.Escape, key.Kind);
    }

    [Fact]
    public void EscapeSequence_SplitAfterEscape_Accumulates()
    {
        var clock = new ManualClock();
        using var pump = CreatePump(clock);
        Assert.Empty(FeedAndDrain(pump, "\u001b"));

        clock.Advance(20);
        var key = Assert.Single(FeedAndDrain(pump, "[D"));
        Assert.Equal(KeyKind.LeftArrow, key.Kind);
        Assert.Equal(0, pump.PendingCount);
    }

    [Fact]
    public void AstralCharacter_SplitAcrossFeeds_ParsesAsSingleChar()
    {
        using var pump = CreatePump();
        Assert.Empty(FeedAndDrain(pump, "\ud83d"));
        Assert.Equal(1, pump.PendingCount);

        var key = Assert.Single(FeedAndDrain(pump, "\ude00"));
        Assert.Equal(KeyKind.Char, key.Kind);
        Assert.Equal("😀", key.Input);
        Assert.Equal(0, pump.PendingCount);
    }

    [Fact]
    public void MetaAstralCharacter_SplitAfterHighSurrogate_ParsesAsSingleChar()
    {
        using var pump = CreatePump();
        Assert.Empty(FeedAndDrain(pump, "\u001b\ud83d"));
        Assert.Equal(2, pump.PendingCount);

        var key = Assert.Single(FeedAndDrain(pump, "\ude00"));
        Assert.Equal(KeyKind.Char, key.Kind);
        Assert.True(key.Meta);
        Assert.Equal("😀", key.Input);
        Assert.Equal(0, pump.PendingCount);
    }

    [Fact]
    public void LinuxFunctionKey_SplitAfterSecondBracket_Accumulates()
    {
        using var pump = CreatePump();
        Assert.Empty(FeedAndDrain(pump, "\u001b[["));

        var key = Assert.Single(FeedAndDrain(pump, "A"));
        Assert.Equal(KeyKind.F1, key.Kind);
        Assert.Equal(0, pump.PendingCount);
    }

    [Fact]
    public void MixedSequence_PreservesOrder()
    {
        using var pump = CreatePump();
        var keys = FeedAndDrain(pump, "ab\u001b[Dc\u007f");
        Assert.Equal(5, keys.Count);
        Assert.Equal("a", keys[0].Input);
        Assert.Equal("b", keys[1].Input);
        Assert.Equal(KeyKind.LeftArrow, keys[2].Kind);
        Assert.Equal("c", keys[3].Input);
        Assert.Equal(KeyKind.Backspace, keys[4].Kind);
    }

    [Fact]
    public void Drain_NoKeys_ReturnsFalse()
    {
        using var pump = CreatePump();
        Assert.False(pump.Drain(_ => { }));
    }

    [Fact]
    public void Feed_ManyKeys_BoundedChannel_StillDelivers()
    {
        // 键风暴（超通道容量 256）— 满即停不丢键（残留 pending 由 Drain 两轮续投收齐）；
        // 超时熔断：2s 上限防实现回归成阻塞/死循环（收不齐 fail-fast 带诊断而非永转）
        using var pump = CreatePump();
        var text = new string('k', 300);
        pump.FeedForTest(text);
        var keys = new List<KeyPress>();
        var timeoutAt = Environment.TickCount64 + 2000;
        while (keys.Count < 300)
        {
            Assert.True(Environment.TickCount64 < timeoutAt,
                $"键风暴收集超时熔断触发：已收 {keys.Count}/300，pending 残留 {pump.PendingCount} 字符");
            pump.Drain(keys.Add);
        }

        Assert.Equal(300, keys.Count);
        Assert.All(keys, key => Assert.Equal("k", key.Input));
        Assert.Equal(0, pump.PendingCount);
    }

    [Fact]
    public void Feed_KeyStorm_OverTripleCapacity_DrainsAllWithoutBlock()
    {
        // 三倍容量键风暴 — 非阻塞续投全链压力（单线程 Feed+Drain 交替无死锁不丢键自证；
        // 超时熔断 2s：实现回归阻塞/丢键时 fail-fast 带诊断）
        using var pump = CreatePump();
        var keys = new List<KeyPress>();
        var timeoutAt = Environment.TickCount64 + 2000;
        for (var i = 0; i < 3; i++)
        {
            pump.FeedForTest(new string('k', 256));
            while (pump.PendingCount > 0)
            {
                Assert.True(Environment.TickCount64 < timeoutAt,
                    $"轮 {i} 超时熔断：pending={pump.PendingCount}，已收 {keys.Count}");
                pump.Drain(keys.Add);
            }
        }

        // 收通道残留（末轮 Drain 后可能仍在通道未收）
        pump.Drain(keys.Add);
        Assert.Equal(768, keys.Count);
        Assert.Equal(0, pump.PendingCount);
    }

    [Fact]
    public void Dispose_MakesDrain_TerminalNoMoreKeys()
    {
        var pump = CreatePump();
        pump.FeedForTest("a");
        pump.Dispose();
        // Dispose 后已入通道键仍可排空（TryComplete 不清既有项 — 优雅收尾语义）
        var keys = new List<KeyPress>();
        pump.Drain(keys.Add);
        Assert.Single(keys);
        // 后续注入面停用（通道完成静默 — Drain 恒空防御不抛）
        pump.FeedForTest("b");
        pump.Drain(keys.Add);
        Assert.Single(keys);
    }

    [Fact]
    public async Task Dispose_QueuedCommand_CompletesWaiter()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var pump = new RawStdinPump(clock: () =>
        {
            entered.TrySetResult();
            release.Task.GetAwaiter().GetResult();
            return 0;
        });

        try
        {
            var first = Task.Run(() => pump.FeedForTest("\u001b["));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(1));

            var second = Task.Run(() => pump.FeedForTest("x"));
            Assert.True(SpinWait.SpinUntil(() => pump.PendingCommandCount > 0, TimeSpan.FromSeconds(1)));

            pump.Dispose();
            release.TrySetResult();

            await first.WaitAsync(TimeSpan.FromSeconds(1));
            var completed = await Task.WhenAny(second, Task.Delay(TimeSpan.FromSeconds(1)));
            Assert.Same(second, completed);
            await second;
        }
        finally
        {
            release.TrySetResult();
        }
    }

    /// <summary>终端查询响应前置分流（TASK067 批4）— DA1 回复路由给 querier 等待方，
    /// 不入键流（对齐基准 parse-keypress.ts:484-487 路由语义）；后续键正常解析。</summary>
    [Fact]
    public async Task TerminalReply_RoutesToQuerier_NotIntoKeyStream()
    {
        using var querier = new TerminalQuerier(new StringWriter());
        using var pump = new RawStdinPump(querier, clock: static () => 0);
        var query = querier.QueryAsync("\u001b[c", TerminalResponseKind.Da1, TimeSpan.FromSeconds(5));

        var keys = FeedAndDrain(pump, "\u001b[?62;1c" + "a");

        // DA1 回复被路由（键流零产出 — "a" 之前无 Unknown/泄漏键）
        var response = await query.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.NotNull(response);
        Assert.Equal([62, 1], response.Numbers);
        var key = Assert.Single(keys);
        Assert.Equal(KeyKind.Char, key.Kind);
        Assert.Equal("a", key.Input);
        Assert.Equal(0, pump.PendingCount);
    }

    [Fact]
    public async Task TerminalReply_OscSplitAcrossFeeds_RoutesWithoutLeakingKeys()
    {
        using var querier = new TerminalQuerier(new StringWriter());
        var clock = new ManualClock();
        using var pump = new RawStdinPump(querier, clock: () => clock.Now);
        var query = querier.QueryAsync("\u001b]10;?\u0007", TerminalResponseKind.Osc, TimeSpan.FromSeconds(5));

        Assert.Empty(FeedAndDrain(pump, "\u001b]10;rgb:"));
        var keys = FeedAndDrain(pump, "1/1/1\u0007a");

        var response = await query.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.NotNull(response);
        Assert.Equal("10;rgb:1/1/1", response.Payload);
        var key = Assert.Single(keys);
        Assert.Equal(KeyKind.Char, key.Kind);
        Assert.Equal("a", key.Input);
        Assert.Equal(0, pump.PendingCount);
    }

    [Fact]
    public void TerminalReply_IncompleteCsiPrefix_StillTimesOut()
    {
        using var querier = new TerminalQuerier(new StringWriter());
        var clock = new ManualClock();
        using var pump = new RawStdinPump(querier, clock: () => clock.Now);
        Assert.Empty(FeedAndDrain(pump, "\u001b[?2026"));
        Assert.True(pump.PendingCount > 0);

        clock.Advance(51);
        var keys = DrainKeys(pump);

        Assert.Equal(KeyKind.Unknown, Assert.Single(keys).Kind);
        Assert.Equal(0, pump.PendingCount);
    }

    [Fact]
    public void TerminalReply_IncompleteOscPrefix_TimesOutWithoutBlockingInput()
    {
        using var querier = new TerminalQuerier(new StringWriter());
        var clock = new ManualClock();
        using var pump = new RawStdinPump(querier, clock: () => clock.Now);
        Assert.Empty(FeedAndDrain(pump, "\u001b]10;rgb:"));

        clock.Advance(51);
        Assert.Equal(KeyKind.Unknown, Assert.Single(DrainKeys(pump)).Kind);
        Assert.Equal(0, pump.PendingCount);
        Assert.Equal("x", Assert.Single(FeedAndDrain(pump, "x")).Input);
    }

    /// <summary>querier 未注入 — 回复序列走键解析路径（Unknown 吞噬防泄漏，泵独立场景兼容）。</summary>
    [Fact]
    public void TerminalReply_WithoutQuerier_SwallowsAsUnknownKey()
    {
        using var pump = CreatePump();
        var keys = FeedAndDrain(pump, "\u001b[?62;1c");
        var key = Assert.Single(keys);
        Assert.Equal(KeyKind.Unknown, key.Kind);
        Assert.Equal(0, pump.PendingCount);
    }

    /// <summary>不完整 ESC 前缀滞留超 50ms — 整体吞 Unknown 释放 pending，残缺参数不泄漏成文本；
    /// 50ms 内不 flush（等待凑齐窗口）。</summary>
    [Fact]
    public void IncompleteEscapePrefix_StuckBeyond50Ms_SwallowsIncompleteSequence()
    {
        var clock = new ManualClock();
        using var pump = CreatePump(clock);
        pump.FeedForTest("\u001b[1;");
        Assert.Empty(DrainKeys(pump));

        // 50ms 内（未超阈值）— 不 flush 继续等待凑齐
        clock.Advance(50);
        Assert.Empty(DrainKeys(pump));
        Assert.True(pump.PendingCount > 0);

        clock.Advance(30);
        var key = Assert.Single(DrainKeys(pump));
        Assert.Equal(KeyKind.Unknown, key.Kind);
        Assert.Equal(0, pump.PendingCount);
    }

    /// <summary>不完整粘贴滞留超 500ms — 强制产出已有部分内容（基准 PASTE_TIMEOUT emit pasteBuffer
    ///——终止标记丢失释放通道）；500ms 内不 flush。</summary>
    [Fact]
    public void IncompletePaste_StuckBeyond500Ms_FlushesPartialContent()
    {
        var clock = new ManualClock();
        using var pump = CreatePump(clock);
        pump.FeedForTest("\u001b[200~partial content");
        Assert.Empty(DrainKeys(pump));

        clock.Advance(499);
        Assert.Empty(DrainKeys(pump));

        clock.Advance(2);
        var paste = Assert.Single(DrainKeys(pump));
        Assert.Equal(KeyKind.Paste, paste.Kind);
        Assert.Equal("partial content", paste.Input);
        Assert.Equal(0, pump.PendingCount);
    }

    [Fact]
    public void ForcedFlush_ChannelFull_DoesNotLosePaste()
    {
        var clock = new ManualClock();
        using var pump = CreatePump(clock);
        pump.FeedForTest(new string('k', 256));
        pump.FeedForTest("\u001b[200~partial");
        Assert.True(pump.PendingCount > 0);

        clock.Advance(501);
        var keys = DrainKeys(pump);

        Assert.Equal(257, keys.Count);
        Assert.Equal(KeyKind.Paste, keys[^1].Kind);
        Assert.Equal("partial", keys[^1].Input);
        Assert.Equal(0, pump.PendingCount);
    }

    /// <summary>滞留窗口内新数据到达凑齐序列 — 正常解析不 flush（超时计时随新数据重置）。</summary>
    [Fact]
    public void StuckPrefix_NewDataCompletesSequence_ParsesNormally()
    {
        var clock = new ManualClock();
        using var pump = CreatePump(clock);
        pump.FeedForTest("\u001b[1;");
        clock.Advance(20);

        var keys = FeedAndDrain(pump, "5D");

        var key = Assert.Single(keys);
        Assert.Equal(KeyKind.LeftArrow, key.Kind);
        Assert.True(key.Ctrl);
        Assert.Equal(0, pump.PendingCount);
    }
}
