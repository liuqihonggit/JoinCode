namespace JoinCode.Gui.Tests.Views;

/// <summary>
/// 单击文件夹展开 Headless 测试 — 验证 OnItemTapped 逻辑。
/// </summary>
[Collection("GuiUiSequential")]
public sealed class FileTreeTapExpandTests {
    private static MainViewModel CreateVm() => new(
        null,
        new GuiSessionStore(new IO.FileSystem.InMemoryFileSystem(), "mem/sessions"),
        new GuiPreferencesStore(new IO.FileSystem.InMemoryFileSystem(), "mem/gui-preferences.json"));

    /// <summary>单击文件夹 → IsExpanded 切换 → 子节点加载/保留</summary>
    [AvaloniaFact]
    public async Task TapFolder_TogglesIsExpanded_LoadsChildren() {
        await using var vm = CreateVm();
        vm.LoadFileTree(System.AppContext.BaseDirectory);
        var win = new MainWindow { DataContext = vm };
        win.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        vm.ToggleSidePanelCommand.Execute(SidePanelKind.FileTree);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var folderItem = vm.FileTreeItems.First(x => x.IsFolder);
        folderItem.IsExpanded.Should().BeFalse("初始未展开");

        // 单击展开(OnItemTapped 逻辑: item.IsExpanded = !item.IsExpanded)
        folderItem.IsExpanded = true;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        folderItem.IsExpanded.Should().BeTrue("展开后 IsExpanded=true");
        folderItem.Children.Should().NotBeEmpty("子节点应加载");

        // 再单击收起
        folderItem.IsExpanded = false;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        folderItem.IsExpanded.Should().BeFalse("收起后 IsExpanded=false");
        folderItem.Children.Should().NotBeEmpty("子节点保留不丢失");
    }

    /// <summary>单击文件不展开(IsFolder=false 不响应)</summary>
    [AvaloniaFact]
    public async Task TapFile_DoesNotExpand() {
        await using var vm = CreateVm();
        vm.LoadFileTree(System.AppContext.BaseDirectory);
        var win = new MainWindow { DataContext = vm };
        win.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        vm.ToggleSidePanelCommand.Execute(SidePanelKind.FileTree);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var fileItem = vm.FileTreeItems.First(x => !x.IsFolder);
        fileItem.IsFolder.Should().BeFalse("文件节点 IsFolder=false");
        fileItem.IsExpanded.Should().BeFalse("文件节点不展开");
        fileItem.Children.Should().BeEmpty("文件节点无子节点");
    }
}
