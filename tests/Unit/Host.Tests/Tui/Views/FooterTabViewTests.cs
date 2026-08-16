namespace Host.Tests.Tui.Views;

/// <summary>
/// FooterTabView 单元测试 — 验证 Tab 创建、切换事件、计时器设置。
/// </summary>
public class FooterTabViewTests
{
    [Fact]
    public void Constructor_TerminalView_NotNull()
    {
        var view = new FooterTabView();
        Assert.NotNull(view.TerminalView);
    }

    [Fact]
    public void SwitchTo_Log_RaisesTabSwitchedEvent()
    {
        var view = new FooterTabView();
        FooterTab? captured = null;
        view.TabSwitched += t => captured = t;

        view.SwitchTo(FooterTab.Log);

        Assert.Equal(FooterTab.Log, captured);
    }

    [Fact]
    public void SwitchTo_Files_RaisesTabSwitchedEvent()
    {
        var view = new FooterTabView();
        FooterTab? captured = null;
        view.TabSwitched += t => captured = t;

        view.SwitchTo(FooterTab.Files);

        Assert.Equal(FooterTab.Files, captured);
    }

    [Fact]
    public void SwitchTo_Memory_RaisesTabSwitchedEvent()
    {
        var view = new FooterTabView();
        FooterTab? captured = null;
        view.TabSwitched += t => captured = t;

        view.SwitchTo(FooterTab.Memory);

        Assert.Equal(FooterTab.Memory, captured);
    }

    [Fact]
    public void SwitchTo_Settings_RaisesTabSwitchedEvent()
    {
        var view = new FooterTabView();
        FooterTab? captured = null;
        view.TabSwitched += t => captured = t;

        view.SwitchTo(FooterTab.Settings);

        Assert.Equal(FooterTab.Settings, captured);
    }

    [Fact]
    public void SetElapsedTime_DoesNotThrow()
    {
        var view = new FooterTabView();
        view.SetElapsedTime(TimeSpan.FromHours(1).Add(TimeSpan.FromMinutes(2)).Add(TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public void SetElapsedTime_Zero_DoesNotThrow()
    {
        var view = new FooterTabView();
        view.SetElapsedTime(TimeSpan.Zero);
    }

    [Fact]
    public void OnQueueChanged_DoesNotThrow()
    {
        var view = new FooterTabView();
        view.OnQueueChanged(new QueueSnapshot([], [], []));
    }

    [Fact]
    public void OnResize_DoesNotThrow()
    {
        var view = new FooterTabView();
        view.OnResize(120, 40);
    }
}
