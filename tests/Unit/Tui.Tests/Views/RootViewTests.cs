namespace Tui.Tests.Views;

/// <summary>
/// RootView 单元测试 — 验证5层区域创建、组件安装。
/// </summary>
public class RootViewTests
{
    [Fact]
    public void Constructor_FiveAreas_AllNotNull()
    {
        var (root, _) = CreateRootView();

        Assert.NotNull(root.StatusBarArea);
        Assert.NotNull(root.ToolBarArea);
        Assert.NotNull(root.ContentArea);
        Assert.NotNull(root.PromptArea);
        Assert.NotNull(root.FooterArea);
    }

    [Fact]
    public void SetStatusBar_DoesNotThrow()
    {
        var (root, _) = CreateRootView();
        root.SetStatusBar(new MockComponent());
    }

    [Fact]
    public void SetToolBar_DoesNotThrow()
    {
        var (root, _) = CreateRootView();
        root.SetToolBar(new MockComponent());
    }

    [Fact]
    public void SetPrompt_DoesNotThrow()
    {
        var (root, _) = CreateRootView();
        root.SetPrompt(new MockComponent());
    }

    [Fact]
    public void SetFooter_DoesNotThrow()
    {
        var (root, _) = CreateRootView();
        root.SetFooter(new MockComponent());
    }

    [Fact]
    public void AddComponent_DoesNotThrow()
    {
        var (root, _) = CreateRootView();
        root.AddComponent(new MockComponent());
    }

    [Fact]
    public void RemoveComponent_DoesNotThrow()
    {
        var (root, _) = CreateRootView();
        var component = new MockComponent();
        root.AddComponent(component);
        root.RemoveComponent(component);
    }

    private static (RootView root, TerminalPainter painter) CreateRootView()
    {
        var painter = new TerminalPainter(a => a());
        var queue = new CommandQueue();
        var root = new RootView(painter, queue);
        return (root, painter);
    }

    private sealed class MockComponent : ITuiComponent
    {
        public View TerminalView { get; } = new View();
        public void OnQueueChanged(QueueSnapshot snapshot) { }
        public void OnResize(int cols, int rows) { }
    }
}
