namespace NativeTui.Tests.Repl;

/// <summary>
/// SpinnerItem 状态行测试 — 对齐基准 SpinnerAnimationRow 的 status parts 组装：
/// 顺序 suffix → timer → tokens(↓/↑) → thinking，` · ` dim 分隔 + dim 括号包裹；
/// thinkingOnly 单 part 无括号（shimmer 色）；渐进宽度门控（availableSpace = columns-msgWidth-5）；
/// 停滞红跨 tick 生效 + overrideColor 抑制；暂停冻结 elapsed。
/// </summary>
public sealed class SpinnerItemTests
{
    private static SpinnerItem Item(string message = "Working…", int responseLength = 0, bool verbose = false, SpinnerMode mode = SpinnerMode.Responding) => new()
    {
        Message = message,
        Mode = mode,
        ResponseLength = responseLength,
        Verbose = verbose,
    };

    [Fact]
    public void MeasureHeight_IsMarginPlusRow()
    {
        Assert.Equal(2, new SpinnerItem().MeasureHeight(width: 80));
    }

    [Fact]
    public void Render_Idle_NoStatusParts()
    {
        using var screen = new Screen(40, 2);
        Item().Render(screen, new Rect(0, 0, 40, 2), Theme.Dark);
        // 无 thinking/suffix/verbose 且 elapsed<30s → 无 parts 无括号
        Assert.Equal("· Working… ", RowText.Of(screen, 1, 0, 11));
        Assert.DoesNotContain("(", RowText.Of(screen, 1, 0, 40));
    }

    [Fact]
    public void Render_Verbose_TimerAndTokensWithDimParens()
    {
        using var screen = new Screen(60, 2);
        Item(responseLength: 400, verbose: true).Render(screen, new Rect(0, 0, 60, 2), Theme.Dark);
        // glyph(2) + "Working…"(9) + 空格 + "(0s · ↓ 100 tokens)"
        Assert.Equal("· Working… (0s · ↓ 100 tokens)", RowText.Of(screen, 1, 0, 30));
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        // 括号 dim、timer dim、分隔符 dim
        Assert.Equal(dim, screen[1, 12].StyleId);
        Assert.Equal(dim, screen[1, 13].StyleId);
        Assert.Equal(dim, screen[1, 16].StyleId);
        // glyph 与消息仍是 claude 色（非 dim）
        var claude = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.Claude });
        Assert.Equal(claude, screen[1, 0].StyleId);
        Assert.Equal(claude, screen[1, 2].StyleId);
    }

    [Fact]
    public void Render_Requesting_TokensArrowUp()
    {
        using var screen = new Screen(60, 2);
        Item(responseLength: 400, verbose: true, mode: SpinnerMode.Requesting).Render(screen, new Rect(0, 0, 60, 2), Theme.Dark);
        Assert.Equal("· Working… (0s · ↑ 100 tokens)", RowText.Of(screen, 1, 0, 30));
    }

    [Fact]
    public void Render_After30s_TimerWithoutVerbose()
    {
        using var screen = new Screen(40, 2);
        var item = Item();
        item.WallNowMs = 31_000;
        item.Render(screen, new Rect(0, 0, 40, 2), Theme.Dark);
        // elapsed>30s → 显示 timer；tokens=0 → 隐藏
        Assert.Equal("· Working… (31s)", RowText.Of(screen, 1, 0, 16));
    }

    [Fact]
    public void Render_ThinkingOnly_ShimmerColorNoParens()
    {
        using var screen = new Screen(40, 2);
        var item = Item();
        item.Mode = SpinnerMode.Thinking;
        item.AnimTimeMs = 4000;
        item.Render(screen, new Rect(0, 0, 40, 2), Theme.Dark);
        // thinkingOnly："(thinking)" 以 shimmer 色整体渲染（无 dim 外层括号）
        Assert.Equal("* Working… (thinking)", RowText.Of(screen, 1, 0, 21));
        // animTime 4000：elapsedSec=1 → opacity=(sin(π)+1)/2=0.5 → 153↔185 插值 → (169,169,169)
        var shimmer = screen.Styles.GetOrAdd(new TextStyle { Foreground = Color.FromRgb(169, 169, 169) });
        Assert.Equal(shimmer, screen[1, 12].StyleId);
        Assert.Equal(shimmer, screen[1, 13].StyleId);
        // 延迟期内（<3s）opacity=0 → 纯 153 灰
        var item2 = Item();
        item2.Mode = SpinnerMode.Thinking;
        item2.AnimTimeMs = 2000;
        item2.Render(screen, new Rect(0, 0, 40, 2), Theme.Dark);
        var gray = screen.Styles.GetOrAdd(new TextStyle { Foreground = Color.FromRgb(153, 153, 153) });
        Assert.Equal(gray, screen[1, 12].StyleId);
    }

    [Fact]
    public void Render_ThinkingWithEffortSuffix_FullText()
    {
        using var screen = new Screen(60, 2);
        var item = Item();
        item.Mode = SpinnerMode.Thinking;
        item.EffortSuffix = " hard";
        item.AnimTimeMs = 1000;
        item.Render(screen, new Rect(0, 0, 60, 2), Theme.Dark);
        Assert.Equal("✶ Working… (thinking hard)", RowText.Of(screen, 1, 0, 26));
    }

    [Fact]
    public void Render_NarrowThinking_ShrinksToBare()
    {
        using var screen = new Screen(30, 2);
        // columns=30：availableSpace = 30 - (9+2) - 5 = 14；"thinking hard"=13 → 全显
        var item = Item();
        item.Mode = SpinnerMode.Thinking;
        item.EffortSuffix = " hard";
        item.AnimTimeMs = 1000;
        item.Render(screen, new Rect(0, 0, 30, 2), Theme.Dark);
        Assert.Equal("✶ Working… (thinking hard)", RowText.Of(screen, 1, 0, 26));

        // columns=25：availableSpace = 9；13 不行但 >8 → 缩为裸 "thinking"
        var item2 = Item();
        item2.Mode = SpinnerMode.Thinking;
        item2.EffortSuffix = " hard";
        item2.AnimTimeMs = 1000;
        item2.Render(screen, new Rect(0, 0, 25, 2), Theme.Dark);
        Assert.Equal("✶ Working… (thinking)", RowText.Of(screen, 1, 0, 21));
    }

    [Fact]
    public void Render_ThoughtFor_DimWithParens()
    {
        // 先思考一帧（tick 记录 start），再停止 5s 后渲染 → ThoughtFor(5000)
        var item = Item();
        item.Mode = SpinnerMode.Thinking;
        using var warmup = new Screen(40, 2);
        item.Render(warmup, new Rect(0, 0, 40, 2), Theme.Dark);

        using var screen = new Screen(40, 2);
        item.Mode = SpinnerMode.Responding;
        item.WallNowMs = 5000;
        item.Render(screen, new Rect(0, 0, 40, 2), Theme.Dark);
        // ThoughtFor 非 thinkingOnly → dim 括号包裹
        Assert.Equal("· Working… (thought for 5s)", RowText.Of(screen, 1, 0, 27));
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        Assert.Equal(dim, screen[1, 12].StyleId);
        Assert.Equal(dim, screen[1, 26].StyleId);
    }

    [Fact]
    public void Render_Suffix_IsFirstPart()
    {
        using var screen = new Screen(60, 2);
        var item = Item(responseLength: 400, verbose: true);
        item.SpinnerSuffix = "hook running";
        item.Render(screen, new Rect(0, 0, 60, 2), Theme.Dark);
        Assert.Equal("· Working… (hook running · 0s · ↓ 100 tokens)", RowText.Of(screen, 1, 0, 45));
    }

    [Fact]
    public void Render_NarrowColumns_HidesAllParts()
    {
        using var screen = new Screen(25, 2);
        // availableSpace = 25 - 11 - 5 = 9：timer "0s"=2 → 9>2 显示；tokens=12 → 隐藏
        Item(responseLength: 400, verbose: true).Render(screen, new Rect(0, 0, 25, 2), Theme.Dark);
        Assert.Equal("· Working… (0s)", RowText.Of(screen, 1, 0, 15));

        // columns=17：availableSpace=1 → timer 也隐藏 → 无括号（换新屏避免残留）
        using var narrow = new Screen(17, 2);
        Item(responseLength: 400, verbose: true).Render(narrow, new Rect(0, 0, 17, 2), Theme.Dark);
        Assert.Equal("· Working… ", RowText.Of(narrow, 1, 0, 11));
    }

    [Fact]
    public void Render_Paused_ElapsedFrozen()
    {
        using var screen = new Screen(40, 2);
        var item = Item(verbose: true);
        item.LoadingStartMs = 1000;
        item.WallNowMs = 20_000;
        item.PauseStartMs = 8000;
        item.TotalPausedMs = 2000;
        item.Render(screen, new Rect(0, 0, 40, 2), Theme.Dark);
        // elapsed = 8000 - 1000 - 2000 = 5000（墙钟 20s 不影响）
        Assert.Equal("· Working… (5s)", RowText.Of(screen, 1, 0, 15));
    }

    [Fact]
    public void Render_StallAcrossTicks_TurnsRed()
    {
        using var screen = new Screen(40, 2);
        var item = Item();
        item.AnimTimeMs = 0;
        item.Render(screen, new Rect(0, 0, 40, 2), Theme.Dark);
        // 第二帧 5200ms：停滞平滑收敛 → glyph 与消息全红
        item.AnimTimeMs = 5200;
        item.Render(screen, new Rect(0, 0, 40, 2), Theme.Dark);
        var red = screen.Styles.GetOrAdd(new TextStyle { Foreground = Color.FromRgb(171, 43, 63) });
        Assert.Equal(red, screen[1, 0].StyleId);
        Assert.Equal(red, screen[1, 2].StyleId);
    }

    [Fact]
    public void Render_OverrideColor_SuppressesStallRed()
    {
        using var screen = new Screen(40, 2);
        var item = Item();
        item.OverrideColor = Theme.Dark.Permission;
        item.AnimTimeMs = 0;
        item.Render(screen, new Rect(0, 0, 40, 2), Theme.Dark);
        item.AnimTimeMs = 5200;
        item.Render(screen, new Rect(0, 0, 40, 2), Theme.Dark);
        var permission = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.Permission });
        Assert.Equal(permission, screen[1, 0].StyleId);
        Assert.Equal(permission, screen[1, 2].StyleId);
    }

    [Fact]
    public void ScrollIntegration_SpinnerRowAtScrollTail()
    {
        var view = new ReplView { Busy = true };
        view.Messages.Add(new UserMessageItem("hi"));
        view.Messages.Add(Item());
        using var screen = new Screen(40, 8);
        view.Render(screen, Theme.Dark);
        // rows=8：footer 1 行 + 输入 2 行 + 间隔 1 行 → 消息区 0..3
        // user 项占 0-1（margin+正文），spinner 占 2-3（margin+正文）→ 行 3 为 spinner
        Assert.Equal(new Rune('h'), screen[1, 2].Value);
        Assert.Equal(new Rune('·'), screen[3, 0].Value);
        Assert.Equal("· Working… ", RowText.Of(screen, 3, 0, 11));
        // 输入框在行 5-6，footer 行 7
        Assert.Equal(new Rune('❯'), screen[6, 0].Value);
        Assert.Equal(new Rune('╭'), screen[5, 0].Value);
    }
}
