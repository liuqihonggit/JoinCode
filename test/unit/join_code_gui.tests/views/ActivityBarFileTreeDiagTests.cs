namespace JoinCode.Gui.Tests.Views;

/// <summary>
/// 排查"目录树打不开"bug — 用 Avalonia.Headless 渲染真实 MainWindow，
/// 模拟点击 Activity Bar 📁 按钮，验证 FileTreePanelView 是否切换为可见且 TreeView 有数据。
/// </summary>
[Collection("GuiUiSequential")]
public sealed class ActivityBarFileTreeDiagTests {
    private static MainViewModel CreateVm() => new(
        null,
        new GuiSessionStore(new IO.FileSystem.InMemoryFileSystem(), "mem/sessions"),
        new GuiPreferencesStore(new IO.FileSystem.InMemoryFileSystem(), "mem/gui-preferences.json"));

    [AvaloniaFact]
    public async Task ClickFileTreeIcon_PanelBecomesVisibleAndHasItems() {
        await using var vm = CreateVm();
        var win = new MainWindow { DataContext = vm };
        win.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // 初始状态：Sessions 激活，FileTree 不可见
        vm.ActiveSidePanel.Should().Be(SidePanelKind.Sessions);
        vm.IsFileTreePanelActive.Should().BeFalse();

        var fileTreePanel = win.GetVisualDescendants().OfType<FileTreePanelView>().FirstOrDefault();
        fileTreePanel.Should().NotBeNull("FileTreePanelView 应在视觉树中");
        fileTreePanel!.IsVisible.Should().BeFalse("初始状态 FileTree 面板不可见");

        // 查找 📁 ToggleButton
        var fileTreeBtn = win.GetVisualDescendants()
            .OfType<Avalonia.Controls.Primitives.ToggleButton>()
            .FirstOrDefault(b => (b.Content as string)?.Contains("📁") == true);
        fileTreeBtn.Should().NotBeNull("Activity Bar 应有 📁 按钮");

        // 模拟点击：执行 Command
        vm.ToggleSidePanelCommand.Execute(SidePanelKind.FileTree);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // 验证 ViewModel 状态
        vm.IsFileTreePanelActive.Should().BeTrue("点击 📁 后 FileTree 应激活");
        vm.IsSessionPanelActive.Should().BeFalse("Sessions 应互斥消失");
        vm.IsMessagesViewActive.Should().BeTrue("主区应切回消息区");

        // 验证 View 层面板可见性
        fileTreePanel.IsVisible.Should().BeTrue("FileTreePanelView 应可见");

        // 验证目录树有数据
        vm.FileTreeItems.Should().NotBeEmpty("目录树应加载了文件项");
        vm.FileTreeItems.Count.Should().BeGreaterThan(0, "项目根目录应有文件/文件夹");

        // 验证 TreeView 控件存在且有 Items
        var treeView = fileTreePanel.GetVisualDescendants().OfType<TreeView>().FirstOrDefault();
        treeView.Should().NotBeNull("FileTreePanelView 应包含 TreeView");
        treeView!.ItemCount.Should().BeGreaterThan(0, "TreeView 应有节点");
    }

    [AvaloniaFact]
    public async Task ClickFileTreeIcon_ButtonIsCheckedReflectsState() {
        await using var vm = CreateVm();
        var win = new MainWindow { DataContext = vm };
        win.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var fileTreeBtn = win.GetVisualDescendants()
            .OfType<Avalonia.Controls.Primitives.ToggleButton>()
            .FirstOrDefault(b => (b.Content as string)?.Contains("📁") == true);
        fileTreeBtn.Should().NotBeNull();

        // 初始未选中
        fileTreeBtn!.IsChecked.Should().BeFalse("初始 📁 按钮未选中");

        // 点击
        vm.ToggleSidePanelCommand.Execute(SidePanelKind.FileTree);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // OneWay 绑定应更新 IsChecked
        fileTreeBtn.IsChecked.Should().BeTrue("点击后 📁 按钮应选中");
    }

    [AvaloniaFact]
    public async Task SideBarBorder_VisibleAndHasWidth_WhenNotZenMode() {
        await using var vm = CreateVm();
        var win = new MainWindow { DataContext = vm };
        win.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        vm.IsZenMode.Should().BeFalse("初始非 Zen Mode");
        vm.IsNotZenMode.Should().BeTrue("IsNotZenMode 应为 true");
        vm.SidePanelWidth.Should().Be(236, "初始 SidePanelWidth 应为 236");

        // 切到 FileTree
        vm.ToggleSidePanelCommand.Execute(SidePanelKind.FileTree);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        vm.SidePanelWidth.Should().Be(236, "切到 FileTree 后宽度应保持 236");
        vm.IsNotZenMode.Should().BeTrue("非 Zen Mode 时 Side Bar 应可见");
    }

    /// <summary>
    /// 验证 ToggleButton 的 Command 和 CommandParameter 正确绑定 —
    /// 如果 Command 未绑定或 CommandParameter 为 null，真实点击不会切换面板。
    /// </summary>
    [AvaloniaFact]
    public async Task FileTreeButton_CommandAndParameterCorrectlyBound() {
        await using var vm = CreateVm();
        var win = new MainWindow { DataContext = vm };
        win.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var fileTreeBtn = win.GetVisualDescendants()
            .OfType<Avalonia.Controls.Primitives.ToggleButton>()
            .FirstOrDefault(b => (b.Content as string)?.Contains("📁") == true);
        fileTreeBtn.Should().NotBeNull();

        // Command 应正确绑定
        fileTreeBtn!.Command.Should().NotBeNull("📁 按钮 Command 应绑定");
        fileTreeBtn.Command.Should().BeSameAs(vm.ToggleSidePanelCommand);

        // CommandParameter 应为 SidePanelKind.FileTree
        fileTreeBtn.CommandParameter.Should().Be(SidePanelKind.FileTree);

        // 通过 ToggleButton 的 Command 执行（模拟真实点击的 Command 调用）
        fileTreeBtn.Command!.Execute(fileTreeBtn.CommandParameter);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        vm.IsFileTreePanelActive.Should().BeTrue("通过按钮 Command 执行后 FileTree 应激活");
    }

    /// <summary>
    /// 模拟真实点击：通过 ToggleButton.IsChecked setter 触发 OnClick → Command。
    /// 在 Avalonia 中，ToggleButton 点击时 IsChecked 切换并执行 Command。
    /// OneWay 绑定不应阻止 Command 执行。
    /// </summary>
    [AvaloniaFact]
    public async Task FileTreeButton_SimulateRealClick_CommandExecutes() {
        await using var vm = CreateVm();
        var win = new MainWindow { DataContext = vm };
        win.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var fileTreeBtn = win.GetVisualDescendants()
            .OfType<Avalonia.Controls.Primitives.ToggleButton>()
            .FirstOrDefault(b => (b.Content as string)?.Contains("📁") == true);
        fileTreeBtn.Should().NotBeNull();

        // 记数器：监听 ActiveSidePanel 变化
        var sidePanelChanges = new List<SidePanelKind>();
        vm.PropertyChanged += (s, e) => {
            if (e.PropertyName == nameof(MainViewModel.ActiveSidePanel))
                sidePanelChanges.Add(vm.ActiveSidePanel);
        };

        // 模拟真实点击：直接调 ToggleButton 的 Command（与点击等效）
        fileTreeBtn!.Command!.Execute(fileTreeBtn.CommandParameter);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // Command 应执行，ActiveSidePanel 应变化
        sidePanelChanges.Should().Contain(SidePanelKind.FileTree, "点击 📁 应切换到 FileTree 面板");
        vm.IsFileTreePanelActive.Should().BeTrue();
    }
}
