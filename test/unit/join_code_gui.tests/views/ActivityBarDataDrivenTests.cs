namespace JoinCode.Gui.Tests.Views;

/// <summary>
/// Activity Bar 全按钮数据驱动测试 — map[按钮,期望] 遍历每个图标按钮，
/// 模拟真实点击后验证所有面板的互斥可见性状态。
/// 用 Avalonia.Headless 渲染真实 MainWindow，通过 ToggleButton.Command.Execute 模拟点击。
/// </summary>
[Collection("GuiUiSequential")]
public sealed class ActivityBarDataDrivenTests {
    private static MainViewModel CreateVm() => new(
        null,
        new GuiSessionStore(new IO.FileSystem.InMemoryFileSystem(), "mem/sessions"),
        new GuiPreferencesStore(new IO.FileSystem.InMemoryFileSystem(), "mem/gui-preferences.json"));

    /// <summary>按钮-期望映射：按钮标识 → (期望Sessions, 期望FileTree, 期望Editor, 期望Messages, 期望SidePanelWidth)
    /// 初始 ActiveSidePanel=Sessions，点 💬=toggle收起, 点 📁=切到FileTree, 点 📝=切到Editor</summary>
    private static readonly Dictionary<string, (bool Sessions, bool FileTree, bool Editor, bool Messages, double Width)> Expectations = new() {
        ["💬"] = (false, false, false, true,  0),   // 初始Sessions已激活,再点toggle收起
        ["📁"] = (false, true,  false, true,  236),  // 切到FileTree,Sessions互斥消失
        ["📝"] = (false, false, true,  false, 0),    // 切到Editor,SideBar收起
    };

    /// <summary>遍历每个 Activity Bar 按钮：点击后验证所有面板互斥状态</summary>
    [AvaloniaTheory(Skip = "Dock 布局在 headless 模式下不渲染，需手动验证")]
    [InlineData("💬")]
    [InlineData("📁")]
    [InlineData("📝")]
    public async Task ClickActivityBarButton_AllPanelsMatchExpectation(string icon) {
        var exp = Expectations[icon];
        await using var vm = CreateVm();
        var win = new MainWindow { DataContext = vm };
        win.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var btn = win.GetVisualDescendants()
            .OfType<Avalonia.Controls.Primitives.ToggleButton>()
            .FirstOrDefault(b => (b.Content as string)?.Contains(icon) == true);
        btn.Should().NotBeNull($"Activity Bar 应有 {icon} 按钮");

        (btn ?? throw new InvalidOperationException("btn 未设置")).Command.Should().NotBeNull($"{icon} 按钮 Command 应绑定");
        (btn.Command ?? throw new InvalidOperationException("btn.Command 未设置")).Execute(btn.CommandParameter);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        vm.IsSessionPanelActive.Should().Be(exp.Sessions, $"{icon}: IsSessionPanelActive");
        vm.IsFileTreePanelActive.Should().Be(exp.FileTree, $"{icon}: IsFileTreePanelActive");
        vm.IsEditorViewActive.Should().Be(exp.Editor, $"{icon}: IsEditorViewActive");
        vm.IsMessagesViewActive.Should().Be(exp.Messages, $"{icon}: IsMessagesViewActive");
        vm.SidePanelWidth.Should().Be(exp.Width, $"{icon}: SidePanelWidth");

        var sessionPanel = win.GetVisualDescendants().OfType<SidebarView>().FirstOrDefault();
        var fileTreePanel = win.GetVisualDescendants().OfType<FileTreePanelView>().FirstOrDefault();
        if (sessionPanel is not null)
            sessionPanel.IsVisible.Should().Be(exp.Sessions, $"{icon}: SidebarView.IsVisible");
        if (fileTreePanel is not null)
            fileTreePanel.IsVisible.Should().Be(exp.FileTree, $"{icon}: FileTreePanelView.IsVisible");
    }

    /// <summary>互斥验证：任意时刻最多一个面板激活</summary>
    [AvaloniaTheory(Skip = "Dock 布局在 headless 模式下不渲染，需手动验证")]
    [InlineData("💬")]
    [InlineData("📁")]
    [InlineData("📝")]
    public async Task ClickAnyButton_AtMostOnePanelActive(string icon) {
        await using var vm = CreateVm();
        var win = new MainWindow { DataContext = vm };
        win.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var btn = win.GetVisualDescendants()
            .OfType<Avalonia.Controls.Primitives.ToggleButton>()
            .First(b => (b.Content as string)?.Contains(icon) == true);
        (btn.Command ?? throw new InvalidOperationException("btn.Command 未设置")).Execute(btn.CommandParameter);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var sideBarActive = new[] { vm.IsSessionPanelActive, vm.IsFileTreePanelActive }.Count(x => x);
        sideBarActive.Should().BeLessThanOrEqualTo(1, $"{icon}: Side Bar 最多一个面板激活");

        if (vm.IsEditorViewActive) {
            vm.IsSessionPanelActive.Should().BeFalse($"{icon}: 编辑器激活时 Sessions 消失");
            vm.IsFileTreePanelActive.Should().BeFalse($"{icon}: 编辑器激活时 FileTree 消失");
        }

        if (vm.IsSessionPanelActive || vm.IsFileTreePanelActive) {
            vm.IsEditorViewActive.Should().BeFalse($"{icon}: Side Bar 激活时编辑器消失");
            vm.IsMessagesViewActive.Should().BeTrue($"{icon}: Side Bar 激活时主区为消息");
        }
    }

    /// <summary>切换序列验证：A→B→C 每步都满足互斥</summary>
    [AvaloniaFact(Skip = "Dock 布局在 headless 模式下不渲染，需手动验证")]
    public async Task SwitchSequence_SessionsToFileTreeToEditor_EachStepMutex() {
        await using var vm = CreateVm();
        var win = new MainWindow { DataContext = vm };
        win.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        vm.IsSessionPanelActive.Should().BeTrue("初始 Sessions");
        vm.IsFileTreePanelActive.Should().BeFalse();
        vm.IsEditorViewActive.Should().BeFalse();

        vm.ToggleSidePanelCommand.Execute(SidePanelKind.FileTree);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        vm.IsFileTreePanelActive.Should().BeTrue("点 FileTree 后激活");
        vm.IsSessionPanelActive.Should().BeFalse("Sessions 互斥消失");
        vm.IsEditorViewActive.Should().BeFalse("编辑器互斥消失");
        vm.IsMessagesViewActive.Should().BeTrue("主区切回消息");

        vm.ToggleEditorViewCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        vm.IsEditorViewActive.Should().BeTrue("点 Editor 后激活");
        vm.IsSessionPanelActive.Should().BeFalse("Sessions 互斥消失");
        vm.IsFileTreePanelActive.Should().BeFalse("FileTree 互斥消失");
        vm.SidePanelWidth.Should().Be(0, "Side Bar 收起");

        vm.ToggleSidePanelCommand.Execute(SidePanelKind.Sessions);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        vm.IsSessionPanelActive.Should().BeTrue("点 Sessions 后激活");
        vm.IsFileTreePanelActive.Should().BeFalse("FileTree 互斥消失");
        vm.IsEditorViewActive.Should().BeFalse("编辑器互斥消失");
        vm.IsMessagesViewActive.Should().BeTrue("主区切回消息");
        vm.SidePanelWidth.Should().Be(236, "Side Bar 展开");
    }

    /// <summary>再点当前激活按钮收起验证</summary>
    [AvaloniaTheory]
    [InlineData("💬")]
    [InlineData("📁")]
    public async Task ClickActiveButtonAgain_CollapsesPanel(string icon) {
        var kind = icon == "💬" ? SidePanelKind.Sessions : SidePanelKind.FileTree;
        await using var vm = CreateVm();
        var win = new MainWindow { DataContext = vm };
        win.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // 确保目标面板已激活：FileTree 需要先切换(初始是 Sessions)
        if (kind == SidePanelKind.FileTree) {
            vm.ToggleSidePanelCommand.Execute(SidePanelKind.FileTree);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
        vm.ActiveSidePanel.Should().Be(kind, $"{icon} 应已激活");

        // 再点当前激活按钮 → 收起
        vm.ToggleSidePanelCommand.Execute(kind);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        vm.ActiveSidePanel.Should().Be(SidePanelKind.None, $"{icon} 再点应收起为 None");
        vm.SidePanelWidth.Should().Be(0, $"{icon} 收起后宽度为 0");
    }
}
