namespace NativeTui.Tests.Repl;

/// <summary>
/// RawStdinPump 全链测试（DSG042 P2）— FeedForTest 注入绕过 stdin Read，验证
/// 解析→通道→Drain 全链：单键/VT 序列（方向键修饰组合 ReadKey 通道不可得的形态）/
/// bracketed paste 整块（重组状态机天然不需要）/ 不完整前缀跨块攒齐 / 孤立 ESC 即时产出 /
/// 键序保序 / 空排空返回 false。真实 stdin 阻塞行为由宿主 exe 手动验收（ADR 0080）。
/// </summary>
public sealed class RawStdinPumpTests
{
    /// <summary>构造即启动读线程（IsBackground — stdin Read 阻塞由进程退出回收；测试不依赖它）。</summary>
    private static RawStdinPump CreatePump() => new();

    /// <summary>注入并排空全部产出键（Drain 非阻塞 — feed 后立即收）。</summary>
    private static List<KeyPress> FeedAndDrain(RawStdinPump pump, string text)
    {
        pump.FeedForTest(text);
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
        using var pump = CreatePump();
        var first = FeedAndDrain(pump, "\u001b[");
        Assert.Empty(first);

        var second = FeedAndDrain(pump, "D");
        var key = Assert.Single(second);
        Assert.Equal(KeyKind.LeftArrow, key.Kind);
    }

    [Fact]
    public void LoneEscape_ChunkBoundary_ProducesEscapeImmediately()
    {
        // 孤立 ESC 按 chunk 边界即时产出（不等超时 — ReadKey 通道 KeyAvailable 语义近似）
        using var pump = CreatePump();
        var keys = FeedAndDrain(pump, "\u001b");
        var key = Assert.Single(keys);
        Assert.Equal(KeyKind.Escape, key.Kind);
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

    /// <summary>终端查询响应前置分流（TASK067 批4）— DA1 回复路由给 querier 等待方，
    /// 不入键流（对齐基准 parse-keypress.ts:484-487 路由语义）；后续键正常解析。</summary>
    [Fact]
    public async Task TerminalReply_RoutesToQuerier_NotIntoKeyStream()
    {
        using var querier = new TerminalQuerier(new StringWriter());
        using var pump = new RawStdinPump(querier);
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

    /// <summary>不完整 ESC 前缀滞留超 50ms — 强制 flush 产出 Escape（基准 App.tsx:309-333 flushIncomplete
    ///——非 force 路径永久滞留 = 输入静默失联）；50ms 内不 flush（等待凑齐窗口）。</summary>
    [Fact]
    public void IncompleteEscapePrefix_StuckBeyond50Ms_ForcesEscapeFlush()
    {
        using var pump = CreatePump();
        pump.FeedForTest("\u001b[1;");
        var keys = new List<KeyPress>();
        pump.Drain(keys.Add);
        Assert.Empty(keys);

        // 50ms 内（未超阈值）— 不 flush 继续等待凑齐
        pump.Drain(keys.Add);
        Assert.Empty(keys);
        Assert.True(pump.PendingCount > 0);

        Thread.Sleep(80);
        pump.Drain(keys.Add);
        Assert.Contains(keys, static k => k.Kind == KeyKind.Escape);
        Assert.Equal(0, pump.PendingCount);
    }

    /// <summary>不完整粘贴滞留超 500ms — 强制产出已有部分内容（基准 PASTE_TIMEOUT emit pasteBuffer
    ///——终止标记丢失释放通道）；500ms 内不 flush。</summary>
    [Fact]
    public void IncompletePaste_StuckBeyond500Ms_FlushesPartialContent()
    {
        using var pump = CreatePump();
        pump.FeedForTest("\u001b[200~partial content");
        var keys = new List<KeyPress>();
        pump.Drain(keys.Add);
        Assert.Empty(keys);

        Thread.Sleep(520);
        pump.Drain(keys.Add);
        var paste = Assert.Single(keys);
        Assert.Equal(KeyKind.Paste, paste.Kind);
        Assert.Equal("partial content", paste.Input);
        Assert.Equal(0, pump.PendingCount);
    }

    /// <summary>滞留窗口内新数据到达凑齐序列 — 正常解析不 flush（超时计时随新数据重置）。</summary>
    [Fact]
    public void StuckPrefix_NewDataCompletesSequence_ParsesNormally()
    {
        using var pump = CreatePump();
        pump.FeedForTest("\u001b[1;");
        Thread.Sleep(20);

        var keys = FeedAndDrain(pump, "5D");

        var key = Assert.Single(keys);
        Assert.Equal(KeyKind.LeftArrow, key.Kind);
        Assert.True(key.Ctrl);
        Assert.Equal(0, pump.PendingCount);
    }
}
