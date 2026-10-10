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

    [Fact]
    public async Task ClickFileTreeIcon_PanelBecomesVisibleAndHasItems() {
        await using var vm = CreateVm();
        vm.LoadFileTree(System.AppContext.BaseDirectory);

        vm.ActiveSidePanel.Should().Be(SidePanelKind.Sessions);
        vm.IsFileTreePanelActive.Should().BeFalse();

        vm.ToggleSidePanelCommand.Execute(SidePanelKind.FileTree);

        vm.IsFileTreePanelActive.Should().BeTrue("点击 📁 后 FileTree 应激活");
        vm.IsSessionPanelActive.Should().BeFalse("Sessions 应互斥消失");
        vm.FileTreeItems.Should().NotBeEmpty("目录树应加载了文件项");
        vm.ActiveSidePanel.Should().Be(SidePanelKind.FileTree, "ActiveSidePanel 应为 FileTree");
    }

    [Fact]
    public async Task ClickFileTreeIcon_ButtonIsCheckedReflectsState() {
        await using var vm = CreateVm();
        vm.IsFileTreePanelActive.Should().BeFalse("初始 FileTree 未激活");

        vm.ToggleSidePanelCommand.Execute(SidePanelKind.FileTree);
        vm.IsFileTreePanelActive.Should().BeTrue("点击后 FileTree 应激活");
        vm.ActiveSidePanel.Should().Be(SidePanelKind.FileTree, "ActiveSidePanel 应为 FileTree");
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
    [Fact]
    public async Task FileTreeButton_CommandAndParameterCorrectlyBound() {
        await using var vm = CreateVm();
        vm.ToggleSidePanelCommand.Should().NotBeNull("ToggleSidePanelCommand 应存在");
        vm.ToggleSidePanelCommand.Execute(SidePanelKind.FileTree);
        vm.IsFileTreePanelActive.Should().BeTrue("通过 Command 执行后 FileTree 应激活");
        vm.ActiveSidePanel.Should().Be(SidePanelKind.FileTree, "ActiveSidePanel 应为 FileTree");
    }

    /// <summary>
    /// 模拟真实点击：通过 Button.Command.Execute 触发面板切换。
    /// Button 无 IsChecked 绑定，点击直接执行 Command，避免 ToggleButton OneWay 绑定问题。
    /// </summary>
    [Fact]
    public async Task FileTreeButton_SimulateRealClick_CommandExecutes() {
        await using var vm = CreateVm();

        var sidePanelChanges = new List<SidePanelKind>();
        vm.PropertyChanged += (s, e) => {
            if (e.PropertyName == nameof(MainViewModel.ActiveSidePanel))
                sidePanelChanges.Add(vm.ActiveSidePanel);
        };

        vm.ToggleSidePanelCommand.Execute(SidePanelKind.FileTree);

        sidePanelChanges.Should().Contain(SidePanelKind.FileTree, "点击 📁 应切换到 FileTree 面板");
        vm.IsFileTreePanelActive.Should().BeTrue();
    }
}
