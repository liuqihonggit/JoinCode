namespace Tui.Tests.Views;

/// <summary>
/// ToolBarView 单元测试 — 验证按钮创建、事件触发、状态管理。
/// </summary>
public class ToolBarViewTests
{
    [Fact]
    public void Constructor_TerminalView_NotNull()
    {
        var view = new ToolBarView();
        Assert.NotNull(view.TerminalView);
    }

    [Fact]
    public void TriggerAction_New_RaisesEvent()
    {
        var view = new ToolBarView();
        ToolBarAction? captured = null;
        view.ActionRequested += a => captured = a;

        view.TriggerAction(ToolBarAction.New);

        Assert.Equal(ToolBarAction.New, captured);
    }

    [Fact]
    public void TriggerAction_Pause_RaisesEvent()
    {
        var view = new ToolBarView();
        ToolBarAction? captured = null;
        view.ActionRequested += a => captured = a;

        view.TriggerAction(ToolBarAction.Pause);

        Assert.Equal(ToolBarAction.Pause, captured);
    }

    [Fact]
    public void TriggerAction_Stop_RaisesEvent()
    {
        var view = new ToolBarView();
        ToolBarAction? captured = null;
        view.ActionRequested += a => captured = a;

        view.TriggerAction(ToolBarAction.Stop);

        Assert.Equal(ToolBarAction.Stop, captured);
    }

    [Fact]
    public void TriggerAction_Chat_RaisesEvent()
    {
        var view = new ToolBarView();
        ToolBarAction? captured = null;
        view.ActionRequested += a => captured = a;

        view.TriggerAction(ToolBarAction.Chat);

        Assert.Equal(ToolBarAction.Chat, captured);
    }

    [Fact]
    public void TriggerAction_Stats_RaisesEvent()
    {
        var view = new ToolBarView();
        ToolBarAction? captured = null;
        view.ActionRequested += a => captured = a;

        view.TriggerAction(ToolBarAction.Stats);

        Assert.Equal(ToolBarAction.Stats, captured);
    }

    [Fact]
    public void SetRunning_True_DoesNotThrow()
    {
        var view = new ToolBarView();
        view.SetRunning(true);
    }

    [Fact]
    public void SetRunning_False_DoesNotThrow()
    {
        var view = new ToolBarView();
        view.SetRunning(false);
    }

    [Fact]
    public void OnQueueChanged_DoesNotThrow()
    {
        var view = new ToolBarView();
        view.OnQueueChanged(new QueueSnapshot([], [], []));
    }

    [Fact]
    public void OnResize_DoesNotThrow()
    {
        var view = new ToolBarView();
        view.OnResize(120, 40);
    }
}
