namespace NativeTui.Tests.Repl;

/// <summary>
/// FooterBar 动态源消费测试 — 行 2 右侧三源互斥优先级：
/// 动态通知 ReplNotifications.Current（dim）&gt; 静态 FooterState.Notification（dim）&gt;
/// TokenWarning（Ok dim / Low warning 琥珀）。通知瞬态（到期 Tick 清除），
/// token 警告常驻（无通知时接管行 2）。MeasureHeight 随源内容 1↔2 行联动。
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
    public void Notifications_OverridesStaticNotification()
    {
        using var screen = new Screen(cols: 60, rows: 2);
        var notifications = new ReplNotifications();
        notifications.Tick(0);
        notifications.Enqueue("动态通知");
        var footer = new FooterBar();
        var state = new FooterState { Notification = "静态通知", Notifications = notifications };
        footer.Render(screen, new Rect(0, 0, 60, 2), Theme.Dark, state);

        // 行 2 只出现动态通知（互斥优先级 — 通知瞬态覆盖静态）
        Assert.DoesNotContain("静态通知", RowText.Of(screen, 1, 0, 60));
        Assert.Contains("动态通知", RowText.Of(screen, 1, 0, 60));
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
}
