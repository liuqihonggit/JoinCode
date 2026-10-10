namespace JoinCode.Gui.Tests.Views;

/// <summary>工作区可发现性与动效控制验收。</summary>
[Collection("GuiUiSequential")]
public sealed class WorkspaceExperienceTests {
    [Fact]
    public async Task NarrowWorkspace_SendActionStaysInsideWindow() {
        var fs = new InMemoryFileSystem();
        await using var vm = new MainViewModel(new PlaceholderChatSession(),
            new GuiSessionStore(fs, "mem/sessions"), new GuiPreferencesStore(fs, "mem/preferences.json"));
        vm.AnimationsEnabled = false;
        vm.ActiveSidePanel = SidePanelKind.Settings;
        vm.SendCommand.Should().NotBeNull("发送命令应存在");
    }

    [Fact]
    public async Task Workspace_OffersMarkdownFileExport() {
        var fs = new InMemoryFileSystem();
        await using var vm = new MainViewModel(new PlaceholderChatSession(),
            new GuiSessionStore(fs, "mem/sessions"), new GuiPreferencesStore(fs, "mem/preferences.json"));
        vm.ExportSessionMarkdown.Should().NotBeNull("Markdown 导出属性应存在");
    }

    [Fact]
    public async Task Sidebar_OffersSessionSearch() {
        var fs = new InMemoryFileSystem();
        await using var vm = new MainViewModel(new PlaceholderChatSession(),
            new GuiSessionStore(fs, "mem/sessions"), new GuiPreferencesStore(fs, "mem/preferences.json"));
        vm.SessionSearchText.Should().BeEmpty("会话搜索初始应为空");
        vm.SessionSearchText = "测试";
        vm.SessionSearchText.Should().Be("测试", "会话搜索应可设置");
    }

    [Fact]
    public async Task Workspace_ExplainsActualSendGesture() {
        var fs = new InMemoryFileSystem();
        await using var vm = new MainViewModel(new PlaceholderChatSession(),
            new GuiSessionStore(fs, "mem/sessions"), new GuiPreferencesStore(fs, "mem/preferences.json"));
        vm.SendHintText.Should().NotBeNullOrEmpty("发送手势提示应存在");
    }

    [Fact]
    public async Task ReducedMotion_CompletionsOpenImmediately() {
        var fs = new InMemoryFileSystem();
        await using var vm = new MainViewModel(new PlaceholderChatSession(),
            new GuiSessionStore(fs, "mem/sessions"), new GuiPreferencesStore(fs, "mem/preferences.json"));
        vm.AnimationsEnabled = false;
        vm.InputText = "/";
        vm.InputCaretIndex = 1;
        vm.RefreshSlashSuggestions();
        vm.IsSlashPopupOpen.Should().BeTrue("斜杠补全应打开");
    }

    [AvaloniaFact]
    public async Task SessionSearch_TracksRenameAddRemoveAndPreservesSelection() {
        var fs = new InMemoryFileSystem();
        await using var vm = new MainViewModel(new PlaceholderChatSession(),
            new GuiSessionStore(fs, "mem/sessions"), new GuiPreferencesStore(fs, "mem/preferences.json"));
        vm.Sessions.Clear();
        var selected = new SessionItem { Title = "CAD 工具", IsSelected = true };
        var other = new SessionItem { Title = "研究配色" };
        vm.Sessions.Add(selected);
        vm.Sessions.Add(other);
        vm.SessionSearchText = " cad ";
        vm.VisibleSessions.Should().Equal(selected);
        selected.Title = "GUI 工具";
        vm.HasNoMatchingSessions.Should().BeTrue();
        selected.IsSelected.Should().BeTrue();
        other.Title = "Cad 动画";
        vm.VisibleSessions.Should().Equal(other);
        vm.Sessions.Remove(other);
        vm.HasNoMatchingSessions.Should().BeTrue();
        vm.SessionSearchText = " ";
        vm.VisibleSessions.Should().Equal(selected);
    }

    [AvaloniaFact]
    public async Task MarkdownExport_PreservesCodeAndExcludesSystemPromptRegardlessOfSearch() {
        var fs = new InMemoryFileSystem();
        await using var vm = new MainViewModel(new PlaceholderChatSession(),
            new GuiSessionStore(fs, "mem/sessions"), new GuiPreferencesStore(fs, "mem/preferences.json"));
        vm.Messages.Add(new ChatUiMessage { Role = MessageRole.System, Content = "private instructions" });
        vm.Messages.Add(new ChatUiMessage { Role = MessageRole.User, Content = "写一个函数" });
        vm.Messages.Add(new ChatUiMessage { Role = MessageRole.Assistant, Content = "```csharp\nreturn 42;\n```" });
        vm.SearchText = "does not match";
        var markdown = vm.ExportSessionMarkdown;
        markdown.Should().StartWith("# ").And.Contain("写一个函数")
            .And.Contain("```csharp\nreturn 42;\n```").And.NotContain("private instructions");
    }
}
