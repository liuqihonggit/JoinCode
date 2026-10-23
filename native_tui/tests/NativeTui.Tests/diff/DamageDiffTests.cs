namespace NativeTui.Tests.Diff;

/// <summary>
/// damage 区域增量 diff 测试（6.2 — 基准 ink/screen.ts diffEach 只扫 next.damage ∪ prev.damage）：
/// Screen 行级 damage 标记（写入必标、Clear 重置、双缓冲交换后 front 保留）+ ScanSegments 行过滤
///（跳过干净行 — 大终端长会话 O(全屏)→O(变化)）+ 过滤与全量扫描等价性（damage 正确标记时产出相同段集）。
/// </summary>
public sealed class DamageDiffTests
{
    /// <summary>WriteText 标记行 damage。</summary>
    [Fact]
    public void WriteText_MarksRowDamaged()
    {
        using var screen = new Screen(cols: 20, rows: 10);
        screen.WriteText(3, 0, "hi");

        Assert.True(screen.IsRowDamaged(3));
        Assert.False(screen.IsRowDamaged(2));
        Assert.False(screen.IsRowDamaged(4));
    }

    /// <summary>indexer set 标记行 damage（高亮覆写/光标反形等后处理路径）。</summary>
    [Fact]
    public void IndexerSet_MarksRowDamaged()
    {
        using var screen = new Screen(cols: 20, rows: 5);
        screen[2, 7] = screen[2, 7] with { StyleId = 42 };

        Assert.True(screen.IsRowDamaged(2));
    }

    /// <summary>Clear 重置 damage（新帧从零标记）。</summary>
    [Fact]
    public void Clear_ResetsDamage()
    {
        using var screen = new Screen(cols: 20, rows: 5);
        screen.WriteText(1, 0, "x");
        Assert.True(screen.IsRowDamaged(1));

        screen.Clear();

        Assert.False(screen.IsRowDamaged(1));
    }

    /// <summary>ScanSegments 传 damage 行集 → 跳过干净行（只扫 dirty 行）。</summary>
    [Fact]
    public void ScanSegments_WithDamageFilter_SkipsCleanRows()
    {
        using var prev = new Screen(cols: 10, rows: 100);
        using var next = new Screen(cols: 10, rows: 100);
        // 只有 row 50 一行变化
        prev.WriteText(50, 0, "aaaa");
        next.WriteText(50, 0, "aaxa");

        var damage = new bool[100];
        damage[50] = true;
        var segments = CellDiffer.ScanSegments(prev, next, damage).ToList();

        var segment = Assert.Single(segments);
        Assert.Equal(50, segment.Row);
        Assert.Equal(2, segment.StartCol);
    }

    /// <summary>等价性：damage 正确标记（prev ∪ next 全部写入行）时，过滤扫描产出 == 全量扫描产出。</summary>
    [Fact]
    public void ScanSegments_DamageFilter_EquivalentToFullScan()
    {
        using var prev = new Screen(cols: 12, rows: 60);
        using var next = new Screen(cols: 12, rows: 60);
        // 双帧随机写入若干行（部分相同部分不同）
        prev.WriteText(0, 0, "same");
        next.WriteText(0, 0, "same");
        prev.WriteText(10, 2, "old text");
        next.WriteText(10, 2, "new text");
        next.WriteText(30, 5, "added");
        prev.WriteText(45, 1, "gone");
        next[45, 8] = next[45, 8] with { StyleId = 9 };

        var damage = new bool[60];
        for (var row = 0; row < 60; row++)
        {
            damage[row] = prev.IsRowDamaged(row) || next.IsRowDamaged(row);
        }

        var filtered = CellDiffer.ScanSegments(prev, next, damage).ToList();
        var full = CellDiffer.ScanSegments(prev, next).ToList();

        Assert.Equal(full, filtered);
    }

    /// <summary>RenderFrame diff 路径内部接 damage（等价性 — 屏行为不变，性能路径切换对消费者透明）。</summary>
    [Fact]
    public void RenderFrame_UsesDamageInternally_EquivalentOutput()
    {
        using var prev = new Screen(cols: 12, rows: 40);
        using var next = new Screen(cols: 12, rows: 40);
        prev.WriteText(5, 0, "before");
        next.WriteText(5, 0, "after!");

        // damage 集合（prev ∪ next 写入行）驱动与全量驱动产出一致 — CellDiffer 内部自动取 damage
        //（diff 输出按变化段："before"→"after!" 中 'r' 未变 → 段 = "afte" + "!"）
        var differ = new CellDiffer();
        var output = differ.RenderFrame(prev, next, hyperlinksSupported: false);

        Assert.Contains("afte", output);
        Assert.Contains("!", output);
        Assert.DoesNotContain("befo", output);
    }

    /// <summary>patch 优化（6.8 — 基准 ink/optimizer.ts 合并近距光标移动）：同行两段变化
    /// 间距 ≤ 合并阈值 → 单次定位覆盖（中间未变 cell 重写无害，消第二个定位序列）。</summary>
    [Fact]
    public void Optimizer_NearSegmentsSameRow_MergedIntoOnePositioning()
    {
        using var prev = new Screen(cols: 20, rows: 5);
        using var next = new Screen(cols: 20, rows: 5);
        prev.WriteText(2, 3, "aaaa");
        next.WriteText(2, 3, "AAAA");
        prev.WriteText(2, 9, "bbbb");
        next.WriteText(2, 9, "BBBB");

        var output = new CellDiffer().RenderFrame(prev, next);

        // 近距两段（col 3-6 与 col 9-12，间隙 2 ≤ 阈值 8）合并 — 全输出只含一次光标定位
        var positioningCount = CountOccurrences(output, 'H');
        Assert.Equal(1, positioningCount);
        Assert.Contains("AAAA", output);
        Assert.Contains("BBBB", output);
    }

    /// <summary>远距两段（间隙超阈值）→ 保持两段独立定位（合并不划算）。</summary>
    [Fact]
    public void Optimizer_FarSegments_KeepSeparatePositioning()
    {
        using var prev = new Screen(cols: 40, rows: 5);
        using var next = new Screen(cols: 40, rows: 5);
        prev.WriteText(2, 2, "aaaa");
        next.WriteText(2, 2, "Aaaa");
        prev.WriteText(2, 30, "bbbb");
        next.WriteText(2, 30, "Bbbb");

        var output = new CellDiffer().RenderFrame(prev, next);

        Assert.Equal(2, CountOccurrences(output, 'H'));
    }

    /// <summary>跨行两段 → 独立定位（跨行合并不在本优化范围）。</summary>
    [Fact]
    public void Optimizer_DifferentRows_KeepSeparatePositioning()
    {
        using var prev = new Screen(cols: 20, rows: 6);
        using var next = new Screen(cols: 20, rows: 6);
        prev.WriteText(1, 3, "aaaa");
        next.WriteText(1, 3, "Aaaa");
        prev.WriteText(4, 3, "bbbb");
        next.WriteText(4, 3, "Bbbb");

        var output = new CellDiffer().RenderFrame(prev, next);

        Assert.Equal(2, CountOccurrences(output, 'H'));
    }

    private static int CountOccurrences(string text, char marker) =>
        text.Count(ch => ch == marker);
}
