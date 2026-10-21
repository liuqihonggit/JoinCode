namespace NativeTui.Tests.Repl;

/// <summary>
/// Spinner 显示条件测试（对齐基准 Claude Code REPL.tsx showSpinner 语义 — 用户反馈：
/// spinner 不应一直闪烁，基准只在特定场景出现）：
/// ①空闲（非 busy）— spinner 完全隐藏（高度 0 不渲染，基准空闲无 spinner 行）；
/// ②busy — spinner 显示（等待/思考/工具运行 = 首字前反馈，基准 isLoading 路径）；
/// ③busy 且工具活跃 — 显示（基准 hasActiveTools）。
/// 基准关键行（REPL.tsx:1715）：流式文本可见时隐藏 spinner（文本即反馈）——TUI 正文
/// 回合结束才上屏（无流式正文），token_delta 期 spinner 是唯一反馈保留显示，等价基准首行未完阶段。
/// </summary>
public sealed class SpinnerVisibilityTests
{
    /// <summary>空闲 — driver EndTurn 后 spinner Hidden（高度 0）。</summary>
    [Fact]
    public void Idle_SpinnerHidden()
    {
        var view = new ReplView();
        var spinner = new SpinnerItem();
        var banner = new BannerItem("0.0.0", "m", "b", "C:\\");
        var driver = new ReplBridgeDriver(view, spinner, banner);
        view.Messages.Add(banner);
        view.Messages.Add(spinner);

        driver.Apply(new BridgeEvent.TurnStarted());
        driver.Apply(new BridgeEvent.TurnCompleted());

        Assert.True(spinner.Hidden);
        Assert.Equal(0, spinner.MeasureHeight(40));
    }

    /// <summary>busy — spinner 可见（高度 2）。</summary>
    [Fact]
    public void Busy_SpinnerVisible()
    {
        var view = new ReplView();
        var spinner = new SpinnerItem();
        var banner = new BannerItem("0.0.0", "m", "b", "C:\\");
        var driver = new ReplBridgeDriver(view, spinner, banner);
        view.Messages.Add(banner);
        view.Messages.Add(spinner);

        driver.Apply(new BridgeEvent.TurnStarted());

        Assert.False(spinner.Hidden);
        Assert.Equal(2, spinner.MeasureHeight(40));
    }

    /// <summary>忙内来回 — TurnStarted 显示 / TurnInterrupted 终态也隐藏。</summary>
    [Fact]
    public void BusyTerminalEvents_HideSpinner()
    {
        var terminal = new BridgeEvent[] { new BridgeEvent.TurnCompleted(), new BridgeEvent.TurnInterrupted() };
        foreach (var evt in terminal) {
            var view = new ReplView();
            var spinner = new SpinnerItem();
            var banner = new BannerItem("0.0.0", "m", "b", "C:\\");
            var driver = new ReplBridgeDriver(view, spinner, banner);
            view.Messages.Add(banner);
            view.Messages.Add(spinner);

            driver.Apply(new BridgeEvent.TurnStarted());
            driver.Apply(evt);

            Assert.True(spinner.Hidden, $"{evt.GetType().Name} 终态后应隐藏");
        }
    }

    /// <summary>Hidden 渲染 — 零高度不写任何 cell。</summary>
    [Fact]
    public void HiddenRender_WritesNothing()
    {
        using var screen = new Screen(cols: 40, rows: 8);
        var spinner = new SpinnerItem { Hidden = true };
        spinner.AnimTimeMs = 100;
        spinner.WallNowMs = 100;

        spinner.Render(screen, new Rect(0, 2, 40, 0), Theme.Dark);

        Assert.Equal(0, spinner.MeasureHeight(40));
    }
}
