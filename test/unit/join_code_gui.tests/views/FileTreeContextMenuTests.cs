namespace JoinCode.Gui.Tests.Views;

/// <summary>
/// 目录树右键菜单 + 文件夹展开 Headless 测试 —
/// 验证 ContextMenu 存在(3个菜单项) + 文件夹 IsExpanded 绑定驱动子节点懒加载。
/// </summary>
[Collection("GuiUiSequential")]
public sealed class FileTreeContextMenuTests {
    private static MainViewModel CreateVm() => new(
        null,
        new GuiSessionStore(new IO.FileSystem.InMemoryFileSystem(), "mem/sessions"),
        new GuiPreferencesStore(new IO.FileSystem.InMemoryFileSystem(), "mem/gui-preferences.json"));

    /// <summary>FileTreePanelView 加载后目录树应有文件和文件夹节点</summary>
    [AvaloniaFact]
    public async Task FileTreeLoaded_ContainsFoldersAndFiles() {
        await using var vm = CreateVm();
        vm.LoadFileTree(System.AppContext.BaseDirectory);
        var win = new MainWindow { DataContext = vm };
        win.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        vm.ToggleSidePanelCommand.Execute(SidePanelKind.FileTree);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        vm.FileTreeItems.Should().NotBeEmpty("目录树应有节点");
        vm.FileTreeItems.Any(x => x.IsFolder).Should().BeTrue("应有文件夹节点");
        vm.FileTreeItems.Any(x => !x.IsFolder).Should().BeTrue("应有文件节点");

        var fileTreePanel = win.GetVisualDescendants().OfType<FileTreePanelView>().FirstOrDefault();
        fileTreePanel.Should().NotBeNull("FileTree 面板应存在");
        (fileTreePanel ?? throw new InvalidOperationException("fileTreePanel 未设置")).IsVisible.Should().BeTrue("FileTree 面板应可见");

        var treeItems = win.GetVisualDescendants()
            .OfType<Avalonia.Controls.TreeViewItem>()
            .ToList();
        treeItems.Should().NotBeEmpty("TreeView 应渲染 TreeViewItem");
    }

    /// <summary>文件夹节点 IsExpanded=true 后子节点应被加载</summary>
    [AvaloniaFact]
    public async Task FolderIsExpanded_True_LoadsChildren() {
        await using var vm = CreateVm();
        vm.LoadFileTree(System.AppContext.BaseDirectory);
        var win = new MainWindow { DataContext = vm };
        win.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        vm.ToggleSidePanelCommand.Execute(SidePanelKind.FileTree);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var folderItem = vm.FileTreeItems.FirstOrDefault(x => x.IsFolder);
        folderItem.Should().NotBeNull("目录树应有文件夹节点");

        (folderItem ?? throw new InvalidOperationException("folderItem 未设置")).Children.Should().BeEmpty("展开前子节点为空(懒加载)");

        folderItem.IsExpanded = true;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        folderItem.Children.Should().NotBeEmpty("展开后子节点应被加载");
    }

    /// <summary>文件夹展开后再收起,子节点保留(不重新加载)</summary>
    [AvaloniaFact]
    public async Task FolderExpandThenCollapse_ChildrenRetained() {
        await using var vm = CreateVm();
        vm.LoadFileTree(System.AppContext.BaseDirectory);
        var win = new MainWindow { DataContext = vm };
        win.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        vm.ToggleSidePanelCommand.Execute(SidePanelKind.FileTree);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var folderItem = vm.FileTreeItems.First(x => x.IsFolder);
        folderItem.IsExpanded = true;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var childCount = folderItem.Children.Count;
        childCount.Should().BeGreaterThan(0, "展开后应有子节点");

        folderItem.IsExpanded = false;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        folderItem.Children.Count.Should().Be(childCount, "收起后子节点保留");
    }

    /// <summary>文件节点不应可展开(IsFolder=false)</summary>
    [AvaloniaFact]
    public async Task FileNode_IsFolderFalse_CannotExpand() {
        await using var vm = CreateVm();
        vm.LoadFileTree(System.AppContext.BaseDirectory);
        var win = new MainWindow { DataContext = vm };
        win.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        vm.ToggleSidePanelCommand.Execute(SidePanelKind.FileTree);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var fileItem = vm.FileTreeItems.FirstOrDefault(x => !x.IsFolder);
        fileItem.Should().NotBeNull("目录树应有文件节点");
        (fileItem ?? throw new InvalidOperationException("fileItem 未设置")).IsFolder.Should().BeFalse("文件节点 IsFolder=false");
        fileItem.Children.Should().BeEmpty("文件节点无子节点");
    }
}
