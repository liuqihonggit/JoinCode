namespace NativeTui.Tests.Repl;

/// <summary>
/// SpinnerItem 扩展态测试（通知与 footer 域）— 时间驱动提示行 + 空闲态 + 终端字符集：
/// 提示行：elapsed &gt; 30s dim `/btw 记录随想`、&gt; 1800s `/clear 清理上下文`
/// （基准 Spinner.tsx:255-259 链 + MessageResponse `  ⎿  ` 缩进形态）、外部 SpinnerTip 兜底、
/// TipsEnabled 开关；空闲态：IsIdle 显示 dim `✻ 空闲` 静态行（Hidden 仍最高优先）；
/// 字符集：SpinnerCharsets.Of 纯分支（ghostty ✽→* / darwin ✳ / 默认集复用 SpinnerMath）。
/// </summary>
public sealed class NotificationSpinnerItemTests
{
    private static SpinnerItem Item(string message = "Working…") => new() { Message = message };

    [Fact]
    public void Tip_Before30s_NoTipRow()
    {
        using var screen = new Screen(40, 3);
        Item().Render(screen, new Rect(0, 0, 40, 3), Theme.Dark);

        // elapsed=0 → 无提示行（高度 2，行 2 空）
        Assert.Equal(2, Item().MeasureHeight(40));
        Assert.Equal(string.Empty, RowText.Of(screen, 2, 0, 40).TrimEnd());
    }

    [Fact]
    public void Tip_After30s_BtwTipDimBelowSpinner()
    {
        using var screen = new Screen(40, 3);
        var item = Item();
        item.WallNowMs = 31_000;
        item.Render(screen, new Rect(0, 0, 40, 3), Theme.Dark);

        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        // spinner 行下方：`  ⎿  ` 缩进（MessageResponse 形态）+ dim 提示文本
        Assert.Equal("  ⎿  提示: /btw 记录随想", RowText.Of(screen, 2, 0, 30).TrimEnd());
        Assert.Equal(dim, screen[2, 0].StyleId);
        Assert.Equal(3, item.MeasureHeight(40));
    }

    [Fact]
    public void Tip_After1800s_ClearTip()
    {
        using var screen = new Screen(40, 3);
        var item = Item();
        item.WallNowMs = 1_800_001;
        item.Render(screen, new Rect(0, 0, 40, 3), Theme.Dark);

        // 1800s 优先级高于 30s（基准 showClearTip 先判）
        Assert.Equal("  ⎿  提示: /clear 清理上下文", RowText.Of(screen, 2, 0, 30).TrimEnd());
    }

    [Fact]
    public void Tip_ExternalSpinnerTip_FallbackLayer()
    {
        using var screen = new Screen(40, 3);
        var item = Item();
        item.SpinnerTip = "自定义提示";
        item.Render(screen, new Rect(0, 0, 40, 3), Theme.Dark);

        // 时间提示未激活（elapsed=0）→ 外部 spinnerTip 兜底显示
        Assert.Equal("  ⎿  提示: 自定义提示", RowText.Of(screen, 2, 0, 30).TrimEnd());
    }

    [Fact]
    public void Tip_Disabled_NoTipRowEvenAfter30s()
    {
        using var screen = new Screen(40, 3);
        var item = Item();
        item.TipsEnabled = false;
        item.WallNowMs = 31_000;
        item.Render(screen, new Rect(0, 0, 40, 3), Theme.Dark);

        Assert.Equal(string.Empty, RowText.Of(screen, 2, 0, 40).TrimEnd());
        Assert.Equal(2, item.MeasureHeight(40));
    }

    [Fact]
    public void Tip_Paused_FollowsFrozenElapsed()
    {
        // 暂停冻结 elapsed：墙钟 40s 但暂停自 5s 起 → elapsed=5s → 无 30s 提示
        using var screen = new Screen(40, 3);
        var item = Item();
        item.LoadingStartMs = 0;
        item.WallNowMs = 40_000;
        item.PauseStartMs = 5_000;
        item.Render(screen, new Rect(0, 0, 40, 3), Theme.Dark);

        Assert.Equal(string.Empty, RowText.Of(screen, 2, 0, 40).TrimEnd());
    }

    [Fact]
    public void Idle_RendersDimIdleRow()
    {
        using var screen = new Screen(40, 3);
        var item = Item();
        item.IsIdle = true;
        item.Render(screen, new Rect(0, 0, 40, 3), Theme.Dark);

        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        // margin 行留空 + 行 1 dim `✻ 空闲`（静态无动画 — 基准 leaderIsIdle 分支）
        Assert.Equal("✻ 空闲", RowText.Of(screen, 1, 0, 10).TrimEnd());
        Assert.Equal(dim, screen[1, 0].StyleId);
        Assert.Equal(2, item.MeasureHeight(40));
    }

    [Fact]
    public void Idle_CustomText_Variant()
    {
        using var screen = new Screen(40, 3);
        var item = Item();
        item.IsIdle = true;
        item.IdleText = "已工作 5s";
        item.Render(screen, new Rect(0, 0, 40, 3), Theme.Dark);

        Assert.Equal("✻ 已工作 5s", RowText.Of(screen, 1, 0, 20).TrimEnd());
    }

    [Fact]
    public void Idle_NoTipRow_EvenAfter30s()
    {
        using var screen = new Screen(40, 3);
        var item = Item();
        item.IsIdle = true;
        item.WallNowMs = 31_000;
        item.Render(screen, new Rect(0, 0, 40, 3), Theme.Dark);

        // 基准 leaderIsIdle 分支独立 return — 空闲态不渲染提示行
        Assert.Equal(string.Empty, RowText.Of(screen, 2, 0, 40).TrimEnd());
        Assert.Equal(2, item.MeasureHeight(40));
    }

    [Fact]
    public void Idle_HiddenStillWins()
    {
        var item = Item();
        item.IsIdle = true;
        item.Hidden = true;

        // Hidden = 零输出（最高优先 — 既有 busy/idle 显示语义不变）
        Assert.Equal(0, item.MeasureHeight(40));
        using var screen = new Screen(40, 3);
        item.Render(screen, new Rect(0, 0, 40, 3), Theme.Dark);
        Assert.Equal(string.Empty, RowText.Of(screen, 1, 0, 40).TrimEnd());
    }

    [Fact]
    public void Frames_DefaultCharset_Unchanged()
    {
        // 回归保护：Frames=null → 默认集（AnimTimeMs 240 → Frame 2 → 默认集 '*'）
        using var screen = new Screen(40, 2);
        var item = Item();
        item.AnimTimeMs = 240;
        item.Render(screen, new Rect(0, 0, 40, 2), Theme.Dark);

        Assert.Equal(new Rune('*'), screen[1, 0].Value);
    }

    [Fact]
    public void Frames_Ghostty_StarAtTail()
    {
        // ghostty 集 index 5 = '*'（默认集 index 5 = '✽'）— AnimTimeMs 600 → Frame 5
        using var screen = new Screen(40, 2);
        var item = Item();
        item.Frames = SpinnerCharsets.Of("xterm-ghostty", isDarwin: false);
        item.AnimTimeMs = 600;
        item.Render(screen, new Rect(0, 0, 40, 2), Theme.Dark);

        Assert.Equal(new Rune('*'), screen[1, 0].Value);
    }

    [Fact]
    public void Frames_Darwin_AsteriskAtThird()
    {
        // darwin 集 index 2 = '✳'（默认集 index 2 = '*'）— AnimTimeMs 240 → Frame 2
        using var screen = new Screen(40, 2);
        var item = Item();
        item.Frames = SpinnerCharsets.Of(term: null, isDarwin: true);
        item.AnimTimeMs = 240;
        item.Render(screen, new Rect(0, 0, 40, 2), Theme.Dark);

        Assert.Equal(new Rune('✳'), screen[1, 0].Value);
    }

    [Fact]
    public void SpinnerCharsets_Of_DefaultBranch_ReusesSpinnerMathSource()
    {
        // 默认分支复用 SpinnerMath.SpinnerFrames 唯一数据源（不重复定义）
        Assert.Same(SpinnerMath.SpinnerFrames, SpinnerCharsets.Of(null, isDarwin: false));
        Assert.Same(SpinnerMath.SpinnerFrames, SpinnerCharsets.Of("xterm-256color", isDarwin: false));
    }

    [Fact]
    public void SpinnerCharsets_Of_BounceSequence()
    {
        // 12 帧往返弹跳：前向 6 + 反转 6（ghostty 集 [·,✢,✳,✶,✻,*] → [.., *, ✻, ✶, ✳, ✢, ·]）
        var frames = SpinnerCharsets.Of("xterm-ghostty", isDarwin: false);

        Assert.Equal(12, frames.Length);
        Assert.Equal("*", frames[5]);
        Assert.Equal("*", frames[6]);
        Assert.Equal("✻", frames[7]);
        Assert.Equal("·", frames[11]);
    }

    [Fact]
    public void SpinnerCharsets_Of_DarwinKeepsTeardropTail()
    {
        var frames = SpinnerCharsets.Of(null, isDarwin: true);

        Assert.Equal("✳", frames[2]);
        Assert.Equal("✽", frames[5]);
    }
}
