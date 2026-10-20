namespace NativeTui.Tests.Repl.History;

/// <summary>
/// HistoryRing 单元测试 — 对齐基准 useArrowKeyHistory 导航状态机 + history.ts 容量语义：
/// index 0 = 输入框态（非历史）；Up 翻旧（首次离开 0 暂存草稿、到底保持不动）；
/// Down 翻新（回到 0 恢复草稿）；提交追加（末条去重）；容量 100 淘汰最旧；不回绕。
/// </summary>
public sealed class HistoryRingTests
{
    /// <summary>提交追加：条目按提交顺序可翻回。</summary>
    [Fact]
    public void Add_StoresEntriesInSubmitOrder()
    {
        var ring = new HistoryRing();
        ring.Add("first");
        ring.Add("second");
        Assert.Equal(2, ring.Count);
    }

    /// <summary>末条相同去重：连续提交相同文本不重复入史。</summary>
    [Fact]
    public void Add_SameAsLast_DoesNotDuplicate()
    {
        var ring = new HistoryRing();
        ring.Add("same");
        ring.Add("same");
        Assert.Equal(1, ring.Count);
    }

    /// <summary>非末条相同仍追加（历史保留重复，仅末条去重）。</summary>
    [Fact]
    public void Add_SameAsNonLast_Appends()
    {
        var ring = new HistoryRing();
        ring.Add("a");
        ring.Add("b");
        ring.Add("a");
        Assert.Equal(3, ring.Count);
    }

    /// <summary>空文本不入史。</summary>
    [Fact]
    public void Add_Empty_Ignored()
    {
        var ring = new HistoryRing();
        ring.Add(string.Empty);
        Assert.Equal(0, ring.Count);
    }

    /// <summary>容量 100：超限淘汰最旧（MAX_HISTORY_ITEMS 对齐基准）。</summary>
    [Fact]
    public void Add_OverCapacity_DropsOldest()
    {
        var ring = new HistoryRing();
        for (var i = 0; i < HistoryRing.Capacity + 5; i++)
        {
            ring.Add($"cmd-{i}");
        }

        Assert.Equal(HistoryRing.Capacity, ring.Count);
        Assert.True(ring.TryUp(out var newest));
        Assert.Equal($"cmd-{HistoryRing.Capacity + 4}", newest);
        for (var i = 0; i < HistoryRing.Capacity - 1; i++)
        {
            Assert.True(ring.TryUp(out _));
        }

        Assert.False(ring.TryUp(out _));
    }

    /// <summary>Up：从输入框翻入历史 — 返回最新一条，index 递增。</summary>
    [Fact]
    public void TryUp_FromInput_ReturnsNewest()
    {
        var ring = new HistoryRing();
        ring.Add("old");
        ring.Add("new");
        Assert.True(ring.TryUp(out var text));
        Assert.Equal("new", text);
        Assert.Equal(1, ring.Index);
    }

    /// <summary>Up 连按：逐条翻旧。</summary>
    [Fact]
    public void TryUp_Repeated_WalksOlder()
    {
        var ring = new HistoryRing();
        ring.Add("a");
        ring.Add("b");
        ring.Add("c");
        Assert.True(ring.TryUp(out var first));
        Assert.True(ring.TryUp(out var second));
        Assert.True(ring.TryUp(out var third));
        Assert.Equal("c", first);
        Assert.Equal("b", second);
        Assert.Equal("a", third);
    }

    /// <summary>Up 首次离开输入框：以传入 draft 暂存。</summary>
    [Fact]
    public void TryUp_FirstTime_StoresDraft()
    {
        var ring = new HistoryRing();
        ring.Add("hist");
        Assert.True(ring.TryUp(out _, "draft"));
        Assert.Equal("draft", ring.Draft);
    }

    /// <summary>Up 到底（越过最旧一条）：返回 false、index 不变、可再次尝试仍 false。</summary>
    [Fact]
    public void TryUp_BeyondOldest_ReturnsFalseAndKeepsIndex()
    {
        var ring = new HistoryRing();
        ring.Add("only");
        Assert.True(ring.TryUp(out _));
        Assert.False(ring.TryUp(out _));
        Assert.Equal(1, ring.Index);
    }

    /// <summary>Down：翻回更新一条，再翻回输入框恢复草稿。</summary>
    [Fact]
    public void TryDown_WalksNewer()
    {
        var ring = new HistoryRing();
        ring.Add("a");
        ring.Add("b");
        ring.TryUp(out _, "draft");
        ring.TryUp(out _);
        Assert.True(ring.TryDown(out var first));
        Assert.True(ring.TryDown(out var second));
        Assert.Equal("b", first);
        Assert.Equal("draft", second);
        Assert.Equal(0, ring.Index);
    }

    /// <summary>Down 回到输入框：恢复草稿（index 归 0）。</summary>
    [Fact]
    public void TryDown_ToInput_RestoresDraft()
    {
        var ring = new HistoryRing();
        ring.Add("hist");
        ring.TryUp(out _, "draft");
        Assert.True(ring.TryDown(out var draft));
        Assert.Equal("draft", draft);
        Assert.Equal(0, ring.Index);
    }

    /// <summary>Down 无草稿回到输入框：返回空串（调用方清空输入）。</summary>
    [Fact]
    public void TryDown_ToInput_NoDraft_ReturnsEmpty()
    {
        var ring = new HistoryRing();
        ring.Add("hist");
        ring.TryUp(out _);
        Assert.True(ring.TryDown(out var draft));
        Assert.Equal(string.Empty, draft);
    }

    /// <summary>Down 已在输入框（index 0）：返回 false。</summary>
    [Fact]
    public void TryDown_AtInput_ReturnsFalse()
    {
        var ring = new HistoryRing();
        ring.Add("hist");
        Assert.False(ring.TryDown(out _));
    }

    /// <summary>Reset：回输入框态 + 清草稿（提交/清空后调用）。</summary>
    [Fact]
    public void Reset_ClearsIndexAndDraft()
    {
        var ring = new HistoryRing();
        ring.Add("hist");
        ring.TryUp(out _);
        ring.Reset();
        Assert.Equal(0, ring.Index);
        Assert.Null(ring.Draft);
        Assert.False(ring.TryDown(out _));
    }

    /// <summary>Seed：宿主注入种子（旧→新顺序），与运行时追加共存。</summary>
    [Fact]
    public void Seed_AppendsInOrder()
    {
        var ring = new HistoryRing();
        ring.Seed(["old-seed", "new-seed"]);
        ring.Add("live");
        Assert.Equal(3, ring.Count);
        Assert.True(ring.TryUp(out var newest));
        Assert.Equal("live", newest);
        Assert.True(ring.TryUp(out var second));
        Assert.Equal("new-seed", second);
        Assert.True(ring.TryUp(out var third));
        Assert.Equal("old-seed", third);
    }

    /// <summary>Seed 超容量：同 Add 淘汰语义。</summary>
    [Fact]
    public void Seed_OverCapacity_DropsOldest()
    {
        var ring = new HistoryRing();
        var entries = Enumerable.Range(0, HistoryRing.Capacity + 10).Select(i => $"s-{i}").ToArray();
        ring.Seed(entries);
        Assert.Equal(HistoryRing.Capacity, ring.Count);
        Assert.True(ring.TryUp(out var newest));
        Assert.Equal($"s-{HistoryRing.Capacity + 9}", newest);
    }

    /// <summary>草稿仅在首次离开输入框捕获：翻历史中途草稿不变（后续 draft 参数忽略）。</summary>
    [Fact]
    public void Draft_CapturedOnlyOnFirstLeave()
    {
        var ring = new HistoryRing();
        ring.Add("a");
        ring.Add("b");
        Assert.True(ring.TryUp(out _, "draft"));
        Assert.Equal("draft", ring.Draft);
        Assert.True(ring.TryUp(out _, "changed-later"));
        Assert.Equal("draft", ring.Draft);
    }
}
