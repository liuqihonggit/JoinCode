namespace NativeTui.Tests.Repl;

/// <summary>
/// FooterBar 动态源消费测试 — 行 2 起右侧多源堆叠（TASK069 N2 对齐基准 Notifications.tsx column）：
/// 动态通知 ReplNotifications.Current（dim）/ 静态 FooterState.Notification（dim）/
/// TokenWarning（Ok dim / Low warning 琥珀）各自成行并行显示，不再单槽互斥；
/// 通知瞬态（到期 Tick 清除）只影响自己那一行。MeasureHeight 随活跃来源数联动。
/// </summary>
public sealed class FooterBarDynamicTests
{
    [Fact]
    public void Notifications_Current_Row2RightAlignedDim()
    {
        using var screen = new Screen(cols: 60, rows: 2);
        var notifications = new ReplNotifications();
        notifications.Tick(0);
        notifications.Enqueue("已切换计划模式");
        var footer = new FooterBar();
        footer.Render(screen, new Rect(0, 0, 60, 2), Theme.Dark, new FooterState { Notifications = notifications });

        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        // 7 汉字 14 列 → 右对齐 col 46（Right=60）
        Assert.Equal("已切换计划模式", RowText.Of(screen, 1, 46, 14));
        Assert.Equal(dim, screen[1, 46].StyleId);
    }

    [Fact]
    public void Notifications_AndStatic_StackOnSeparateRows()
    {
        using var screen = new Screen(cols: 60, rows: 3);
        var notifications = new ReplNotifications();
        notifications.Tick(0);
        notifications.Enqueue("动态通知");
        var footer = new FooterBar();
        var state = new FooterState { Notification = "静态通知", Notifications = notifications };

        // TASK069 N2 — 两源并行：动态通知上一行、静态通知下一行，互不覆盖
        Assert.Equal(3, footer.MeasureHeight(state));
        footer.Render(screen, new Rect(0, 0, 60, 3), Theme.Dark, state);

        Assert.Contains("动态通知", RowText.Of(screen, 1, 0, 60));
        Assert.DoesNotContain("静态通知", RowText.Of(screen, 1, 0, 60));
        Assert.Contains("静态通知", RowText.Of(screen, 2, 0, 60));
    }

    [Fact]
    public void Notifications_Expiry_FallsBackToStatic()
    {
        using var screen = new Screen(cols: 60, rows: 2);
        var notifications = new ReplNotifications();
        notifications.Tick(0);
        notifications.Enqueue("动态通知");
        var footer = new FooterBar();
        var state = new FooterState { Notification = "静态通知", Notifications = notifications };

        notifications.Tick(8_000);
        footer.Render(screen, new Rect(0, 0, 60, 2), Theme.Dark, state);

        // 动态通知到期清除 → 静态通知接管行 2
        Assert.DoesNotContain("动态通知", RowText.Of(screen, 1, 0, 60));
        Assert.Contains("静态通知", RowText.Of(screen, 1, 0, 60));
    }

    [Fact]
    public void StaticNotification_Fallback_WithoutQueue()
    {
        using var screen = new Screen(cols: 60, rows: 2);
        var footer = new FooterBar();
        var state = new FooterState { Notification = "◐ medium · /effort" };
        footer.Render(screen, new Rect(0, 0, 60, 2), Theme.Dark, state);

        // 无队列接线 → 既有静态通知语义不变（回归保护）
        Assert.Equal("◐ medium · /effort", RowText.Of(screen, 1, 42, 18));
    }

    [Fact]
    public void Tokens_OkWarning_Row2Dim()
    {
        using var screen = new Screen(cols: 60, rows: 2);
        var tokens = new TokenUsageTracker();
        // 170k（Ok 带内）："15% 直到自动压缩" = 4 ASCII + 6 汉字 = 16 列 → col 44
        tokens.RecordUsage(0, 0, 170_000, 200_000);
        var footer = new FooterBar();
        footer.Render(screen, new Rect(0, 0, 60, 2), Theme.Dark, new FooterState { Tokens = tokens });

        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        Assert.Equal("15% 直到自动压缩", RowText.Of(screen, 1, 44, 16));
        Assert.Equal(dim, screen[1, 44].StyleId);
    }

    [Fact]
    public void Tokens_LowWarning_Row2WarningColor()
    {
        using var screen = new Screen(cols: 60, rows: 2);
        var tokens = new TokenUsageTracker();
        tokens.RecordUsage(0, 0, 187_000, 200_000);
        var footer = new FooterBar();
        footer.Render(screen, new Rect(0, 0, 60, 2), Theme.Dark, new FooterState { Tokens = tokens });

        var warning = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.Warning });
        Assert.Contains("上下文不足 (7%) · 运行 /compact", RowText.Of(screen, 1, 0, 60));
        var width = RuneWidth.OfText("上下文不足 (7%) · 运行 /compact");
        Assert.Equal(warning, screen[1, 60 - width].StyleId);
    }

    [Fact]
    public void Tokens_NoneWarning_SingleRow()
    {
        using var screen = new Screen(cols: 60, rows: 2);
        var tokens = new TokenUsageTracker();
        tokens.RecordUsage(0, 0, 50_000, 200_000);
        var footer = new FooterBar();
        footer.Render(screen, new Rect(0, 0, 60, 2), Theme.Dark, new FooterState { Tokens = tokens });

        // 剩余充足 → 无警告 → 行 2 空（高度 1）
        Assert.Equal(string.Empty, RowText.Of(screen, 1, 0, 60).TrimEnd());
        Assert.Equal(1, footer.MeasureHeight(new FooterState { Tokens = tokens }));
    }

    [Fact]
    public void MeasureHeight_TracksNotificationLifecycle()
    {
        var notifications = new ReplNotifications();
        notifications.Tick(0);
        notifications.Enqueue("通知");
        var footer = new FooterBar();
        var state = new FooterState { Notifications = notifications };

        Assert.Equal(2, footer.MeasureHeight(state));
        notifications.Tick(8_000);
        Assert.Equal(1, footer.MeasureHeight(state));
    }

    [Fact]
    public void MeasureHeight_TracksTokenWarning()
    {
        var footer = new FooterBar();
        var tokens = new TokenUsageTracker();

        // 无用量 → None → 1 行；越线 → 2 行
        Assert.Equal(1, footer.MeasureHeight(new FooterState { Tokens = tokens }));
        tokens.RecordUsage(0, 0, 190_000, 200_000);
        Assert.Equal(2, footer.MeasureHeight(new FooterState { Tokens = tokens }));
    }

    [Fact]
    public void AllSources_StackInPriorityOrder()
    {
        // TASK069 N2 — 五源并行各占一行（不再互斥），自上而下按优先级
        using var screen = new Screen(cols: 60, rows: 6);
        var notifications = new ReplNotifications();
        notifications.Tick(0);
        notifications.Enqueue("动态通知");
        var tokens = new TokenUsageTracker();
        tokens.RecordUsage(0, 0, 190_000, 200_000);
        var footer = new FooterBar();
        var state = new FooterState
        {
            StatusLine = "状态行",
            ContextWarning = "上下文告急 (5%)",
            ContextWarningLevel = ContextAlertLevel.Critical,
            Notifications = notifications,
            Notification = "静态通知",
            Tokens = tokens,
        };

        Assert.Equal(6, footer.MeasureHeight(state));
        footer.Render(screen, new Rect(0, 0, 60, 6), Theme.Dark, state);

        var error = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.Error });
        Assert.Contains("状态行", RowText.Of(screen, 1, 0, 60));
        Assert.Contains("上下文告急 (5%)", RowText.Of(screen, 2, 0, 60));
        Assert.Equal(error, screen[2, 60 - 15].StyleId);
        Assert.Contains("动态通知", RowText.Of(screen, 3, 0, 60));
        Assert.Contains("静态通知", RowText.Of(screen, 4, 0, 60));
        Assert.Equal(tokens.Warning.Text, RowText.Of(screen, 5, 0, 60).TrimStart());
    }

    [Fact]
    public void EmptyStringSource_DoesNotOccupyRow()
    {
        var footer = new FooterBar();

        Assert.Equal(1, footer.MeasureHeight(new FooterState { Notification = string.Empty, StatusLine = string.Empty }));
    }

    [Fact]
    public void NarrowWithLoginError_StackStartsAfterLoginRow()
    {
        using var screen = new Screen(cols: 60, rows: 4);
        var footer = new FooterBar();
        var state = new FooterState { LoginError = "未登录 · 运行 /login", Notification = "静态通知" };

        Assert.Equal(3, footer.MeasureHeight(state, cols: 60));
        footer.Render(screen, new Rect(0, 0, 60, 4), Theme.Dark, state);

        Assert.Contains("未登录", RowText.Of(screen, 1, 0, 60));
        Assert.Contains("静态通知", RowText.Of(screen, 2, 0, 60));
    }
}
