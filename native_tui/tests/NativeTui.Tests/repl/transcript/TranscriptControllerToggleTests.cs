namespace NativeTui.Tests.Repl.Transcript;

/// <summary>
/// TranscriptController Toggle 往返 / verbose 联动 / Tick 帧拍 / footer 投影测试 —
/// 对齐基准 REPL.tsx 4237-4248（进出冻结语义）+ TranscriptModeFooter（321-362）：
/// verbose 回调进出序列 [true, false]、退出后全状态复位（快照/搜索/滚动/时长）、
/// Tick 仅激活时累计、footer 三态（默认 / 搜索栏打开 / 提交后徽章保留）与中文常量文案。
/// </summary>
public sealed class TranscriptControllerToggleTests
{
    /// <summary>构造控制器（记录 verbose 回调序列；默认视口 10）。</summary>
    private static (TranscriptController Controller, List<bool> Verbose) NewController(IReadOnlyList<string> lines)
    {
        var verbose = new List<bool>();
        var controller = new TranscriptController(() => new TranscriptSnapshot(lines, 0), verbose.Add)
        {
            ViewportHeight = 10,
        };
        return (controller, verbose);
    }

    /// <summary>verbose 联动 — 进出回调序列 [true, false, true...]。</summary>
    [Fact]
    public void Toggle_VerboseCallbackSequence()
    {
        var (controller, verbose) = NewController(["a"]);
        controller.Toggle();
        controller.Toggle();
        controller.Toggle();
        Assert.Equal([true, false, true], verbose);
    }

    /// <summary>退出 — 全状态复位：IsActive/Snapshot/搜索/滚动/时长。</summary>
    [Fact]
    public void Exit_ResetsAllState()
    {
        var lines = Enumerable.Range(0, 100).Select(i => $"L{i:000}").ToArray();
        var (controller, _) = NewController(lines);
        controller.Toggle();
        controller.Tick(64);
        Assert.True(controller.TryHandleKey(new KeyPress(KeyKind.Char, "g")));
        Assert.True(controller.TryHandleKey(new KeyPress(KeyKind.Char, "/")));
        Assert.True(controller.TryHandleKey(new KeyPress(KeyKind.Char, "L")));
        Assert.True(controller.TryHandleKey(KeyPress.Named(KeyKind.Return)));
        Assert.Equal("L", controller.Search.Query);

        controller.Toggle();
        Assert.False(controller.IsActive);
        Assert.Null(controller.Snapshot);
        Assert.Equal(string.Empty, controller.Search.Query);
        Assert.False(controller.Search.BarOpen);
        Assert.Equal(0, controller.Search.Count);
        Assert.Equal(0, controller.ScrollOffset);
        Assert.Equal(0, controller.UptimeMs);
    }

    /// <summary>退出后再进入 — 搜索从头开始（旧查询不复活 — 匹配绑定冻结行号防错位）。</summary>
    [Fact]
    public void Reenter_SearchStartsClean()
    {
        var lines = Enumerable.Range(0, 100).Select(i => $"L{i:000}").ToArray();
        var (controller, _) = NewController(lines);
        controller.Toggle();
        Assert.True(controller.TryHandleKey(new KeyPress(KeyKind.Char, "/")));
        Assert.True(controller.TryHandleKey(new KeyPress(KeyKind.Char, "L")));
        controller.Toggle();
        controller.Toggle();
        Assert.Equal(string.Empty, controller.Search.Query);
        Assert.False(controller.Search.BarOpen);
    }

    /// <summary>Tick — 激活时累计、非正值丢弃、退出清零、未激活丢弃。</summary>
    [Fact]
    public void Tick_AccumulatesOnlyWhileActive()
    {
        var (controller, _) = NewController(["a"]);
        controller.Tick(50);
        Assert.Equal(0, controller.UptimeMs);
        controller.Toggle();
        controller.Tick(100);
        controller.Tick(16);
        controller.Tick(0);
        controller.Tick(-1);
        Assert.Equal(116, controller.UptimeMs);
        controller.Toggle();
        Assert.Equal(0, controller.UptimeMs);
        controller.Tick(50);
        Assert.Equal(0, controller.UptimeMs);
    }

    /// <summary>footer — 默认（无搜索）：标题+提示、无搜索数据。</summary>
    [Fact]
    public void BuildFooter_DefaultNoSearch()
    {
        var (controller, _) = NewController(["a"]);
        controller.Toggle();
        var footer = controller.BuildFooter();
        Assert.Null(footer.SearchQuery);
        Assert.False(footer.SearchActive);
        Assert.Equal(0, footer.SearchCurrent);
        Assert.Equal(0, footer.SearchCount);
    }

    /// <summary>footer — 搜索栏打开：查询透出、激活标记、cur/count 徽章数据。</summary>
    [Fact]
    public void BuildFooter_SearchBarOpen()
    {
        var (controller, _) = NewController(["hit one", "miss", "hit two"]);
        controller.Toggle();
        Assert.True(controller.TryHandleKey(new KeyPress(KeyKind.Char, "/")));
        Assert.True(controller.TryHandleKey(new KeyPress(KeyKind.Char, "h")));
        Assert.True(controller.TryHandleKey(new KeyPress(KeyKind.Char, "i")));
        Assert.True(controller.TryHandleKey(new KeyPress(KeyKind.Char, "t")));
        var footer = controller.BuildFooter();
        Assert.Equal("hit", footer.SearchQuery);
        Assert.True(footer.SearchActive);
        Assert.Equal(1, footer.SearchCurrent);
        Assert.Equal(2, footer.SearchCount);
    }

    /// <summary>footer — 提交后：栏关（SearchQuery null）、查询激活、徽章保留。</summary>
    [Fact]
    public void BuildFooter_AfterCommit_BadgeKept()
    {
        var (controller, _) = NewController(["hit one", "miss", "hit two"]);
        controller.Toggle();
        Assert.True(controller.TryHandleKey(new KeyPress(KeyKind.Char, "/")));
        Assert.True(controller.TryHandleKey(new KeyPress(KeyKind.Char, "h")));
        Assert.True(controller.TryHandleKey(new KeyPress(KeyKind.Char, "i")));
        Assert.True(controller.TryHandleKey(new KeyPress(KeyKind.Char, "t")));
        Assert.True(controller.TryHandleKey(KeyPress.Named(KeyKind.Return)));
        var footer = controller.BuildFooter();
        Assert.Null(footer.SearchQuery);
        Assert.True(footer.SearchActive);
        Assert.Equal(1, footer.SearchCurrent);
        Assert.Equal(2, footer.SearchCount);
    }

    /// <summary>footer — 常量中文文案（标题 + 提示 + 合成整行）。</summary>
    [Fact]
    public void FooterConstants_ChineseText()
    {
        Assert.Equal("详细转录", TranscriptFooterState.TitleText);
        Assert.Equal("ctrl+o 切换 · ↑↓ 滚动 · / 搜索 · q 退出", TranscriptFooterState.HintText);
        Assert.Equal("详细转录 · ctrl+o 切换 · ↑↓ 滚动 · / 搜索 · q 退出", new TranscriptFooterState().FullText);
    }

    /// <summary>未激活 — footer 数据仍可投影（默认态，宿主退出后不再消费）。</summary>
    [Fact]
    public void BuildFooter_Inactive_Defaults()
    {
        var (controller, _) = NewController(["a"]);
        var footer = controller.BuildFooter();
        Assert.Null(footer.SearchQuery);
        Assert.False(footer.SearchActive);
    }
}
