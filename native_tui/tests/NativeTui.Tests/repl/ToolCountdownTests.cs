namespace NativeTui.Tests.Repl;

/// <summary>
/// 工具执行倒计时测试（TASK079 用户指令 — bash/powershell 限时工具执行中 spinner 状态行显示
/// "剩余 Ns" 让用户知道何时自动中断）：SpinnerItem 倒计时 part 渲染（逐帧递减/清除）+
/// ReplBridgeDriver 生命周期（ToolCall 启动 → ToolResult/TurnInterrupted/EndTurn 清除）。
/// </summary>
public sealed class ToolCountdownTests
{
    private static string RowTextOf(Screen screen, int row) => RowText.Of(screen, row, 0, 120).TrimEnd();

    [Fact]
    public void Spinner_Countdown_Active_StatusPartShowsRemaining()
    {
        using var screen = new Screen(cols: 120, rows: 5);
        var spinner = new SpinnerItem { Message = "执行中", Hidden = false };
        spinner.BeginToolCountdown(120);
        spinner.WallNowMs = 5_000;
        spinner.LoadingStartMs = 0;

        spinner.Render(screen, new Rect(0, 0, 120, 5), Theme.Dark);

        var statusRow = RowTextOf(screen, 1);
        Assert.Contains("bash 剩余 115s", statusRow);
    }

    [Fact]
    public void Spinner_Countdown_TicksDownWithWallClock()
    {
        var spinner = new SpinnerItem();
        spinner.BeginToolCountdown(120);
        spinner.WallNowMs = 0;
        spinner.LoadingStartMs = 0;

        spinner.WallNowMs = 10_000;
        using var screen = new Screen(cols: 120, rows: 5);
        spinner.Render(screen, new Rect(0, 0, 120, 5), Theme.Dark);
        Assert.Contains("剩余 110s", RowTextOf(screen, 1));
    }

    [Fact]
    public void Spinner_Countdown_ClampsAtZero_AfterDeadline()
    {
        var spinner = new SpinnerItem();
        spinner.BeginToolCountdown(120);
        spinner.LoadingStartMs = 0;
        spinner.WallNowMs = 500_000; // 远超截止 — 钳制 0s 不出现负数

        using var screen = new Screen(cols: 120, rows: 5);
        spinner.Render(screen, new Rect(0, 0, 120, 5), Theme.Dark);
        Assert.Contains("剩余 0s", RowTextOf(screen, 1));
        Assert.DoesNotContain("剩余 -", RowTextOf(screen, 1));
    }

    [Fact]
    public void Spinner_Countdown_Ended_PartGone()
    {
        var spinner = new SpinnerItem();
        spinner.BeginToolCountdown(120);
        spinner.EndToolCountdown();
        spinner.LoadingStartMs = 0;
        spinner.WallNowMs = 1_000;

        using var screen = new Screen(cols: 120, rows: 5);
        spinner.Render(screen, new Rect(0, 0, 120, 5), Theme.Dark);
        Assert.DoesNotContain("剩余", RowTextOf(screen, 1));
    }

    [Fact]
    public void Driver_BashToolCall_StartsCountdown_ResultClears()
    {
        using var screen = new Screen(cols: 120, rows: 6);
        var spinner = new SpinnerItem();
        var view = new ReplView();
        view.Messages.Add(spinner);
        var driver = new ReplBridgeDriver(view, spinner, new BannerItem("1.0.0", "m", "计费", "/tmp"), shellTimeoutSeconds: 90);

        driver.Apply(new BridgeEvent.ToolCall("bash", "echo hi"));
        Assert.True(spinnerToolCountdownActive(spinner));

        driver.Apply(new BridgeEvent.ToolResult("hi", null, null, 0));
        Assert.False(spinnerToolCountdownActive(spinner));
    }

    [Fact]
    public void Driver_NonTimedTool_NoCountdown()
    {
        var spinner = new SpinnerItem();
        var view = new ReplView();
        view.Messages.Add(spinner);
        var driver = new ReplBridgeDriver(view, spinner, new BannerItem("1.0.0", "m", "计费", "/tmp"), shellTimeoutSeconds: 90);

        driver.Apply(new BridgeEvent.ToolCall("read", "a.cs"));
        Assert.False(spinnerToolCountdownActive(spinner));
    }

    [Fact]
    public void Driver_TokenUsage_UpdatesContextItemPromptTokens()
    {
        var spinner = new SpinnerItem();
        var view = new ReplView();
        view.Messages.Add(spinner);
        var contextItem = new PromptContextItem("快照", 3, () => ("【工具清单】", 505, 35));
        view.Messages.Add(contextItem);
        var driver = new ReplBridgeDriver(view, spinner, new BannerItem("1.0.0", "m", "计费", "/tmp"), shellTimeoutSeconds: 90);
        driver.PromptContextItemTarget = contextItem;

        driver.Apply(new BridgeEvent.TokenUsage(4200, 14, null, null));

        Assert.Equal(4200, contextItem.PromptTokens);
    }

    [Fact]
    public void ContextItem_Summary_ShowsRealTokens_WhenAvailable()
    {
        using var screen = new Screen(cols: 140, rows: 3);
        var item = new PromptContextItem("快照", 12307, () => ("【工具清单】", 505, 35)) { PromptTokens = 4200 };

        item.Render(screen, new Rect(0, 0, 140, 3), Theme.Dark);

        Assert.Contains("约 4200 tokens", RowTextOf(screen, 0));
    }

    [Fact]
    public void Driver_TurnInterrupted_ClearsCountdown()
    {
        var spinner = new SpinnerItem();
        var view = new ReplView();
        view.Messages.Add(spinner);
        var driver = new ReplBridgeDriver(view, spinner, new BannerItem("1.0.0", "m", "计费", "/tmp"), shellTimeoutSeconds: 90);

        driver.Apply(new BridgeEvent.ToolCall("bash", "sleep 300"));
        driver.Apply(new BridgeEvent.TurnInterrupted());
        Assert.False(spinnerToolCountdownActive(spinner));
    }

    /// <summary>倒计时激活探针 — 经 Driver 事件流后 spinner 状态行含 "剩余"（渲染级断言）。</summary>
    private static bool spinnerToolCountdownActive(SpinnerItem spinner)
    {
        using var screen = new Screen(cols: 120, rows: 6);
        spinner.Render(screen, new Rect(0, 0, 120, 6), Theme.Dark);
        return RowTextOf(screen, 1).Contains("剩余");
    }
}
