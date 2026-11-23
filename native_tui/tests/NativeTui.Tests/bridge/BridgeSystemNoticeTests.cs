namespace NativeTui.Tests.Bridge;

/// <summary>
/// 系统旁白事件测试（TASK130-A5）— hook <c>systemMessage</c> 的 bridge 投影：
/// 独立 dim 行落位、不结束回合（与 Error 分支正交）、不并入助手正文（旁白不进上下文历史）。
/// </summary>
public sealed class BridgeSystemNoticeTests
{
    private readonly ReplView _view = new();
    private readonly SpinnerItem _spinner = new();
    private readonly BannerItem _banner = new("0.0.0", "占位模型", "API 按量计费", @"C:\placeholder");
    private readonly ReplBridgeDriver _driver;

    public BridgeSystemNoticeTests()
    {
        _driver = new ReplBridgeDriver(_view, _spinner, _banner);
        _view.Messages.Add(_banner);
        _view.Messages.Add(_spinner);
    }

    /// <summary>旁白落为独立行项，文本原样保留。</summary>
    [Fact]
    public void SystemNotice_AppendsNoticeItem()
    {
        _driver.Apply(new BridgeEvent.SystemNotice("hook: 已检查提交信息"));

        var item = Assert.Single(_view.Messages.Items, static i => i is SystemNoticeItem);
        Assert.Equal("hook: 已检查提交信息", Assert.IsType<SystemNoticeItem>(item).Text);
    }

    /// <summary>旁白不结束回合 — 与 Error 分支不同，spinner 继续跑、IsBusy 保持。</summary>
    [Fact]
    public void SystemNotice_DoesNotEndTurn()
    {
        _driver.Apply(new BridgeEvent.TurnStarted());
        Assert.True(_driver.IsBusy);

        _driver.Apply(new BridgeEvent.SystemNotice("旁白"));

        Assert.True(_driver.IsBusy);
        Assert.False(_spinner.Hidden);
    }

    /// <summary>旁白不进助手正文 — 回合结算只收 Text 流，旁白不混入 assistant_message。</summary>
    [Fact]
    public void SystemNotice_NotMergedIntoAssistantMessage()
    {
        _driver.Apply(new BridgeEvent.TurnStarted());
        _driver.Apply(new BridgeEvent.SystemNotice("旁白"));
        _driver.Apply(new BridgeEvent.TokenDelta(6, "正式回复"));
        _driver.Apply(new BridgeEvent.TurnCompleted());

        var assistant = Assert.Single(_view.Messages.Items, static i => i is AssistantMessageItem);
        Assert.Equal("正式回复", Assert.IsType<AssistantMessageItem>(assistant).Text);
    }
}
