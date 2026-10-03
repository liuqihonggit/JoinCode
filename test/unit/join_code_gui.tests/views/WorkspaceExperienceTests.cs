namespace JoinCode.Gui.Tests.Views;

/// <summary>工作区可发现性与动效控制验收。</summary>
[Collection("GuiUiSequential")]
public sealed class WorkspaceExperienceTests {
    [AvaloniaFact]
    public async Task NarrowWorkspace_SendActionStaysInsideWindow() {
        var fs = new InMemoryFileSystem();
        await using var vm = new MainViewModel(new PlaceholderChatSession(),
            new GuiSessionStore(fs, "mem/sessions"), new GuiPreferencesStore(fs, "mem/preferences.json"));
        vm.AnimationsEnabled = false;
        vm.IsSettingsPanelOpen = true;
        var window = new MainWindow { DataContext = vm, Width = 800, Height = 820 };
        try {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var send = window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "发送"));
            var point = (send.TransformToVisual(window) ?? default).Transform(default);
            (point.X + send.Bounds.Width).Should().BeLessThanOrEqualTo(window.Bounds.Width);
        } finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Workspace_OffersMarkdownFileExport() {
        var fs = new InMemoryFileSystem();
        await using var vm = new MainViewModel(new PlaceholderChatSession(),
            new GuiSessionStore(fs, "mem/sessions"), new GuiPreferencesStore(fs, "mem/preferences.json"));
        var window = new MainWindow { DataContext = vm, Width = 1200, Height = 800 };
        try {
            window.Show();
            window.GetVisualDescendants().OfType<Button>().Select(b => b.Content)
                .Should().Contain("↓ Markdown");
        } finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Sidebar_OffersSessionSearch() {
        var fs = new InMemoryFileSystem();
        await using var vm = new MainViewModel(new PlaceholderChatSession(),
            new GuiSessionStore(fs, "mem/sessions"), new GuiPreferencesStore(fs, "mem/preferences.json"));
        var window = new MainWindow { DataContext = vm, Width = 1200, Height = 800 };
        try {
            window.Show();
            window.GetVisualDescendants().OfType<TextBox>().Select(t => t.Watermark)
                .Should().Contain("筛选会话…");
        } finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Workspace_ExplainsActualSendGesture() {
        var fs = new InMemoryFileSystem();
        await using var vm = new MainViewModel(new PlaceholderChatSession(),
            new GuiSessionStore(fs, "mem/sessions"), new GuiPreferencesStore(fs, "mem/preferences.json"));
        var window = new MainWindow { DataContext = vm, Width = 1200, Height = 800 };
        try {
            window.Show();
            window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text)
                .Should().Contain(vm.SendHintText);
        } finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ReducedMotion_CompletionsOpenImmediately() {
        var fs = new InMemoryFileSystem();
        await using var vm = new MainViewModel(new PlaceholderChatSession(),
            new GuiSessionStore(fs, "mem/sessions"), new GuiPreferencesStore(fs, "mem/preferences.json"));
        vm.AnimationsEnabled = false;
        var window = new MainWindow { DataContext = vm, Width = 1200, Height = 800 };
        try {
            window.Show();
            vm.InputText = "/";
            vm.InputCaretIndex = 1;
            vm.RefreshSlashSuggestions();
            vm.IsSlashPopupOpen.Should().BeTrue();
            var panel = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PaletteRoot");
            panel.Opacity.Should().Be(1);
        } finally { window.Close(); }
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
