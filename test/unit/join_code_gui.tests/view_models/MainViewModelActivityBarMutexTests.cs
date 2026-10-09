namespace JoinCode.Gui.Tests.ViewModels;

/// <summary>
/// Activity Bar 图标按钮互斥逻辑测试 — 验证左侧栏图标按钮互斥行为:
/// 按下一个图标时,其他面板消失,只显示按下的这个。
/// 互斥规则:
/// - 展开 Side Bar 面板(Sessions/FileTree)时,主区切回消息区(编辑器消失)
/// - 切到编辑器时,收起 Side Bar(面板消失)
/// - 再点当前已激活的面板/编辑器,收起回到默认状态
/// </summary>
public class MainViewModelActivityBarMutexTests {
    /// <summary>创建注入 InMemoryFileSystem 会话存储的 ViewModel — 对齐 MainViewModelTests.CreateVm</summary>
    private static MainViewModel CreateVm() => new(
        new JoinCode.Gui.Hosting.PlaceholderChatSession(),
        new GuiSessionStore(new InMemoryFileSystem(), "mem/sessions"),
        new GuiPreferencesStore(new InMemoryFileSystem(), "mem/gui-preferences.json"));

    /// <summary>场景1:初始状态 — Sessions 面板激活,主区为消息区</summary>
    [Fact]
    public async Task InitialState_SessionsActiveAndMessagesView() {
        await using var vm = CreateVm();

        vm.ActiveSidePanel.Should().Be(SidePanelKind.Sessions);
        vm.ActiveMainArea.Should().Be(MainAreaKind.Messages);
        vm.IsSessionPanelActive.Should().BeTrue();
        vm.IsMessagesViewActive.Should().BeTrue();
    }

    /// <summary>场景2:点击 📁 FileTree → FileTree 激活,Sessions 不激活,主区为消息区</summary>
    [Fact]
    public async Task ClickFileTree_FileTreeActiveAndSessionsInactive() {
        await using var vm = CreateVm();

        vm.ToggleSidePanelCommand.Execute(SidePanelKind.FileTree);

        vm.IsFileTreePanelActive.Should().BeTrue();
        vm.IsSessionPanelActive.Should().BeFalse();
        vm.IsMessagesViewActive.Should().BeTrue();
        vm.IsEditorViewActive.Should().BeFalse();
    }

    /// <summary>场景3:点击 💬 Sessions → Sessions 激活,FileTree 不激活,主区为消息区</summary>
    [Fact]
    public async Task ClickSessions_SessionsActiveAndFileTreeInactive() {
        await using var vm = CreateVm();
        // 先切到 FileTree,再切回 Sessions,验证互斥切换
        vm.ToggleSidePanelCommand.Execute(SidePanelKind.FileTree);
        vm.ToggleSidePanelCommand.Execute(SidePanelKind.Sessions);

        vm.IsSessionPanelActive.Should().BeTrue();
        vm.IsFileTreePanelActive.Should().BeFalse();
        vm.IsMessagesViewActive.Should().BeTrue();
        vm.IsEditorViewActive.Should().BeFalse();
    }

    /// <summary>场景4:点击 📝 Editor → 编辑器激活,Side Bar 收起,宽度为0</summary>
    [Fact]
    public async Task ClickEditor_EditorActiveAndSideBarCollapsed() {
        await using var vm = CreateVm();

        vm.ToggleEditorViewCommand.Execute(null);

        vm.IsEditorViewActive.Should().BeTrue();
        vm.IsSessionPanelActive.Should().BeFalse();
        vm.IsFileTreePanelActive.Should().BeFalse();
        vm.IsMessagesViewActive.Should().BeFalse();
        vm.ActiveSidePanel.Should().Be(SidePanelKind.None);
        vm.SidePanelWidth.Should().Be(0);
    }

    /// <summary>场景5:从 Sessions 点 Editor 再点 FileTree → Editor 消失,FileTree 出现,主区切回消息</summary>
    [Fact]
    public async Task SessionsThenEditorThenFileTree_EditorGoneFileTreeActiveMessagesView() {
        await using var vm = CreateVm();
        // 初始 Sessions 激活
        vm.ToggleEditorViewCommand.Execute(null); // 切到编辑器
        vm.IsEditorViewActive.Should().BeTrue();

        vm.ToggleSidePanelCommand.Execute(SidePanelKind.FileTree); // 切到 FileTree

        vm.IsEditorViewActive.Should().BeFalse("Editor 应消失");
        vm.IsFileTreePanelActive.Should().BeTrue("FileTree 应出现");
        vm.IsMessagesViewActive.Should().BeTrue("主区应切回消息");
        vm.IsSessionPanelActive.Should().BeFalse();
    }

    /// <summary>场景6:从 Editor 点 Sessions → Editor 消失,Sessions 出现,主区切回消息</summary>
    [Fact]
    public async Task EditorThenSessions_EditorGoneSessionsActiveMessagesView() {
        await using var vm = CreateVm();

        vm.ToggleEditorViewCommand.Execute(null); // 切到编辑器
        vm.IsEditorViewActive.Should().BeTrue();

        vm.ToggleSidePanelCommand.Execute(SidePanelKind.Sessions); // 切到 Sessions

        vm.IsEditorViewActive.Should().BeFalse("Editor 应消失");
        vm.IsSessionPanelActive.Should().BeTrue("Sessions 应出现");
        vm.IsMessagesViewActive.Should().BeTrue("主区应切回消息");
        vm.IsFileTreePanelActive.Should().BeFalse();
    }

    /// <summary>场景7:再点当前已激活的面板收起 → ActiveSidePanel == None,SidePanelWidth == 0</summary>
    [Fact]
    public async Task ClickActivePanelAgain_CollapsesToNone() {
        await using var vm = CreateVm();
        // 切到 FileTree,再切到 Sessions 激活,再点 Sessions 收起
        vm.ToggleSidePanelCommand.Execute(SidePanelKind.FileTree);
        vm.ToggleSidePanelCommand.Execute(SidePanelKind.Sessions);
        vm.ActiveSidePanel.Should().Be(SidePanelKind.Sessions);

        vm.ToggleSidePanelCommand.Execute(SidePanelKind.Sessions); // 再点收起

        vm.ActiveSidePanel.Should().Be(SidePanelKind.None);
        vm.SidePanelWidth.Should().Be(0);
        vm.IsSessionPanelActive.Should().BeFalse();
        vm.IsFileTreePanelActive.Should().BeFalse();
    }

    /// <summary>场景8:点 Editor 再点 Editor 收回消息区</summary>
    [Fact]
    public async Task ClickEditorTwice_ReturnsToMessagesView() {
        await using var vm = CreateVm();

        vm.ToggleEditorViewCommand.Execute(null); // 切到编辑器
        vm.IsEditorViewActive.Should().BeTrue();

        vm.ToggleEditorViewCommand.Execute(null); // 再点收回

        vm.IsMessagesViewActive.Should().BeTrue();
        vm.IsEditorViewActive.Should().BeFalse();
    }
}
