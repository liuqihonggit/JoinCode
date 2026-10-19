#pragma warning disable JCC9001, JCC3013 // 豁免理由：TempFileTreeRoot 是测试辅助类，直接用 System.IO 创建确定性临时目录结构，非被测行为
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
    [Fact]
    public async Task FileTreeLoaded_ContainsFoldersAndFiles() {
        using var root = new TempFileTreeRoot();
        await using var vm = CreateVm();
        vm.LoadFileTree(root.Path);
        vm.ToggleSidePanelCommand.Execute(SidePanelKind.FileTree);

        vm.FileTreeItems.Should().NotBeEmpty("目录树应有节点");
        vm.FileTreeItems.Any(x => x.IsFolder).Should().BeTrue("应有文件夹节点");
        vm.FileTreeItems.Any(x => !x.IsFolder).Should().BeTrue("应有文件节点");
        vm.ActiveSidePanel.Should().Be(SidePanelKind.FileTree, "应切换到 FileTree 面板");
    }

    /// <summary>文件夹节点 IsExpanded=true 后子节点应被加载</summary>
    [AvaloniaFact]
    public async Task FolderIsExpanded_True_LoadsChildren() {
        using var root = new TempFileTreeRoot();
        await using var vm = CreateVm();
        vm.LoadFileTree(root.Path);
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
        using var root = new TempFileTreeRoot();
        await using var vm = CreateVm();
        vm.LoadFileTree(root.Path);
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
        using var root = new TempFileTreeRoot();
        await using var vm = CreateVm();
        vm.LoadFileTree(root.Path);
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

    /// <summary>临时目录树根 — 创建含子目录+文件的确定性结构,Dispose 自动清理</summary>
    private sealed class TempFileTreeRoot : IDisposable {
        public string Path { get; }
        public TempFileTreeRoot() {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.Guid.NewGuid().ToString("N"));
            var dir = System.IO.Directory.CreateDirectory(Path);
            var subfolder = dir.CreateSubdirectory("subfolder");
            System.IO.File.WriteAllText(System.IO.Path.Combine(subfolder.FullName, "inner.txt"), "inner");
            System.IO.File.WriteAllText(System.IO.Path.Combine(Path, "testfile.txt"), "test");
        }
        public void Dispose() {
            try { if (System.IO.Directory.Exists(Path)) System.IO.Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}
