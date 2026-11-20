namespace NativeTui.Tests.Bridge;

/// <summary>
/// TASK069 #13 流式预览门控 — 对齐基准 REPL.tsx:1495 `showStreamingText = !reducedMotion &&
/// !hasCursorUpViewportYankBug()`（terminal.ts:177 yank bug = win32 || WT_SESSION）。
/// 门控关闭时 TokenDelta 文本不建 StreamingTextItem（等整条落定），但 ResponseLength 仍累加
/// （spinner token 计数语义不变）。
/// </summary>
public sealed class StreamingPreviewGateTests
{
    [Fact]
    public void GateOff_TokenDelta_NoStreamingItem_ButResponseLengthAccumulates()
    {
        var view = new ReplView();
        var spinner = new SpinnerItem();
        var banner = new BannerItem("0.0.0", "m", "API", @"C:\wd");
        var driver = new ReplBridgeDriver(view, spinner, banner, allowStreamingText: false);
        view.Messages.Add(banner);
        view.Messages.Add(spinner);

        driver.Apply(new BridgeEvent.TurnStarted());
        driver.Apply(new BridgeEvent.TokenDelta(6, "第一段"));
        driver.Apply(new BridgeEvent.TokenDelta(6, "第二段"));

        Assert.DoesNotContain(view.Messages.Items, static item => item is StreamingTextItem);
        Assert.Equal(12, spinner.ResponseLength);
    }

    [Fact]
    public void GateOn_TokenDelta_BuildsStreamingItem()
    {
        var view = new ReplView();
        var spinner = new SpinnerItem();
        var banner = new BannerItem("0.0.0", "m", "API", @"C:\wd");
        var driver = new ReplBridgeDriver(view, spinner, banner, allowStreamingText: true);
        view.Messages.Add(banner);
        view.Messages.Add(spinner);

        driver.Apply(new BridgeEvent.TurnStarted());
        driver.Apply(new BridgeEvent.TokenDelta(6, "第一段"));

        Assert.Contains(view.Messages.Items, static item => item is StreamingTextItem);
    }
}
