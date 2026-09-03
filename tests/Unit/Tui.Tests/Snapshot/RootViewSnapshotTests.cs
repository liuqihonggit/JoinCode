namespace Tui.Tests.Snapshot;

/// <summary>
/// RootView 渲染快照测试 — 验证5层布局结构、组件安装后的 View 树。
/// 红阶段：首次运行生成 .received.txt，审核后创建 .approved.txt。
/// </summary>
public class RootViewSnapshotTests
{
    [Fact]
    public void EmptyRootView_FiveLayerStructure()
    {
        var (root, _) = CreateRootView();

        var actual = ViewTreeSerializer.Serialize(root);
        SnapshotVerifier.Verify(actual, "RootView_Empty");
    }

    [Fact]
    public void WithOutputView_ContentAreaHasOutput()
    {
        var (root, _) = CreateRootView();
        root.AddComponent(new OutputView());

        var actual = ViewTreeSerializer.Serialize(root);
        SnapshotVerifier.Verify(actual, "RootView_WithOutput");
    }

    [Fact]
    public void WithStatusBarAndToolBar_AllLayersPopulated()
    {
        var (root, _) = CreateRootView();
        root.SetStatusBar(new StatusBarView());
        root.SetToolBar(new ToolBarView());

        var actual = ViewTreeSerializer.Serialize(root);
        SnapshotVerifier.Verify(actual, "RootView_WithBars");
    }

    private static (RootView root, TerminalPainter painter) CreateRootView()
    {
        var painter = new TerminalPainter(static a => a());
        var queue = new CommandQueue();
        var root = new RootView(painter, queue);
        return (root, painter);
    }
}
