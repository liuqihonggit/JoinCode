namespace Tui.Tests;

/// <summary>
/// StatusBarView 单元测试 — 验证状态栏显示模型和连接状态。
/// </summary>
public class StatusBarViewTests
{
    [Fact]
    public void DefaultDisplay_ContainsAgentOS()
    {
        var bar = new StatusBarView();
        var tree = ViewTreeSerializer.Serialize(bar.TerminalView);
        Assert.Contains("AgentOS", tree);
    }

    [Fact]
    public void SetModel_UpdatesDisplay()
    {
        var bar = new StatusBarView();
        bar.SetModel("gpt-4o");
        var tree = ViewTreeSerializer.Serialize(bar.TerminalView);
        Assert.Contains("gpt-4o", tree);
    }

    [Fact]
    public void SetConnected_True_ShowsConnectedIndicator()
    {
        var bar = new StatusBarView();
        bar.SetConnected(true);
        var tree = ViewTreeSerializer.Serialize(bar.TerminalView);
        Assert.Contains("●", tree);
    }

    [Fact]
    public void SetConnected_False_ShowsDisconnectedIndicator()
    {
        var bar = new StatusBarView();
        bar.SetConnected(false);
        var tree = ViewTreeSerializer.Serialize(bar.TerminalView);
        Assert.Contains("○", tree);
    }

    [Fact]
    public void SetMode_UpdatesDisplay()
    {
        var bar = new StatusBarView();
        bar.SetMode("plan");
        var tree = ViewTreeSerializer.Serialize(bar.TerminalView);
        Assert.Contains("plan", tree);
    }

    [Fact]
    public void SetTokenCount_UpdatesDisplay()
    {
        var bar = new StatusBarView();
        bar.SetTokenCount(1234);
        var tree = ViewTreeSerializer.Serialize(bar.TerminalView);
        Assert.Contains("1234", tree);
    }
}
