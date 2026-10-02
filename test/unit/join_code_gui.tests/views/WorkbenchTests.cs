namespace JoinCode.Gui.Tests.Views;

/// <summary>工作台入口与实际可操作界面验收。</summary>
[Collection("GuiUiSequential")]
public sealed class WorkbenchTests {
    [AvaloniaFact]
    public async Task Workspace_OffersAllThreeWorkbenches() {
        var fs = new InMemoryFileSystem();
        await using var vm = new MainViewModel(new PlaceholderChatSession(),
            new GuiSessionStore(fs, "mem/sessions"), new GuiPreferencesStore(fs, "mem/preferences.json"));
        var window = new MainWindow { DataContext = vm };
        try {
            window.Show();
            var topMenuItems = window.GetVisualDescendants().OfType<Avalonia.Controls.MenuItem>().ToArray();
            var fileMenu = topMenuItems.FirstOrDefault(m => m.Header is string h && h == "文件");
            fileMenu.Should().NotBeNull("应存在'文件'顶层菜单");
            var childHeaders = fileMenu!.Items.OfType<Avalonia.Controls.MenuItem>().Select(m => m.Header).ToArray();
            childHeaders.Should().Contain("文件 / 变更");
            childHeaders.Should().Contain("模型 / MCP / 插件");
        } finally { window.Close(); }
    }

    [Fact]
    public void ToolGroups_AreDerivedFromRegisteredNames() {
        WorkbenchCatalog.Group("file_read").Should().Be("文件");
        WorkbenchCatalog.Group("git_diff").Should().Be("Git");
        WorkbenchCatalog.Group("mcp_connect").Should().Be("MCP");
        WorkbenchCatalog.Group("plugin_install").Should().Be("插件");
        WorkbenchCatalog.Group("other").Should().Be("其他");
    }

    [Theory]
    [InlineData("+new", '+')] [InlineData("-old", '-')]
    [InlineData("+++ b/app.cs", ' ')] [InlineData("--- a/app.cs", ' ')]
    [InlineData("@@ -1 +1 @@", '@')]
    public void DiffColors_DistinguishChangesFromFileHeaders(string text, char expected)
        => JoinCode.Gui.Markdown.UnifiedDiffColorizer.Kind(text).Should().Be(expected);

    [Fact]
    public void InvalidArguments_AreRejectedInsteadOfExecutingEmptyArguments() {
        var action = () => WorkbenchCatalog.ParseArguments("{broken}");
        action.Should().Throw<System.Text.Json.JsonException>();
        WorkbenchCatalog.ParseArguments("{\"path\":\"a.cs\"}")["path"].GetString().Should().Be("a.cs");
    }

    [AvaloniaFact]
    public async Task ToolForm_PreservesStringsAndExecutesSelectedTool() {
        var fs = new InMemoryFileSystem();
        await using var main = new MainViewModel(new PlaceholderChatSession(), new GuiSessionStore(fs, "mem/sessions"), new GuiPreferencesStore(fs, "mem/preferences"));
        var session = new Moq.Mock<IJccChatSession>();
        session.Setup(s => s.GetToolInfoAsync("mcp_connect", It.IsAny<CancellationToken>())).ReturnsAsync(new ToolInfo {
            InputSchema = new ToolSchema { Required = ["server"], Properties = new() { ["server"] = new ToolSchemaProperty { Type = "string" } } }
        });
        session.Setup(s => s.ExecuteToolAsync("mcp_connect", It.IsAny<Dictionary<string, System.Text.Json.JsonElement>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolResult { Content = [new ToolContent { Text = "connected" }] });
        var vm = new WorkbenchViewModel(main, () => session.Object) { SelectedTool = new ToolSummary("mcp_connect", "连接") };
        vm.Parameters.Single().Value = "server \"one\"";
        await vm.RunToolCommand.ExecuteAsync(null);
        vm.OperationResult.Should().Be("connected");
        session.Verify(s => s.ExecuteToolAsync("mcp_connect", It.Is<Dictionary<string, System.Text.Json.JsonElement>>(a => a["server"].GetString() == "server \"one\""), It.IsAny<CancellationToken>()), Moq.Times.Once);
    }

    [AvaloniaFact]
    public async Task MissingRequiredField_DoesNotExecuteTool() {
        var fs = new InMemoryFileSystem();
        await using var main = new MainViewModel(new PlaceholderChatSession(), new GuiSessionStore(fs, "mem/sessions"), new GuiPreferencesStore(fs, "mem/preferences"));
        var session = new Moq.Mock<IJccChatSession>();
        session.Setup(s => s.GetToolInfoAsync("mcp_connect", It.IsAny<CancellationToken>())).ReturnsAsync(new ToolInfo {
            InputSchema = new ToolSchema { Required = ["server"], Properties = new() { ["server"] = new ToolSchemaProperty { Type = "string" } } }
        });
        var vm = new WorkbenchViewModel(main, () => session.Object) { SelectedTool = new ToolSummary("mcp_connect", "连接") };
        await vm.RunToolCommand.ExecuteAsync(null);
        vm.Status.Should().Contain("server").And.Contain("必填");
        session.Verify(s => s.ExecuteToolAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, System.Text.Json.JsonElement>>(), It.IsAny<CancellationToken>()), Moq.Times.Never);
    }

    [AvaloniaFact]
    public async Task InvalidBoolean_DoesNotReachEngine() {
        var fs = new InMemoryFileSystem();
        await using var main = new MainViewModel(new PlaceholderChatSession(), new GuiSessionStore(fs, "mem/sessions"), new GuiPreferencesStore(fs, "mem/preferences"));
        var session = new Moq.Mock<IJccChatSession>();
        session.Setup(s => s.GetToolInfoAsync("mcp_test", It.IsAny<CancellationToken>())).ReturnsAsync(new ToolInfo {
            InputSchema = new ToolSchema { Properties = new() { ["enabled"] = new ToolSchemaProperty { Type = "boolean" } } }
        });
        var vm = new WorkbenchViewModel(main, () => session.Object) { SelectedTool = new ToolSummary("mcp_test", "test") };
        vm.Parameters.Single().Value = "123";
        await vm.RunToolCommand.ExecuteAsync(null);
        vm.Status.Should().Contain("enabled").And.Contain("boolean");
        session.Verify(s => s.ExecuteToolAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, System.Text.Json.JsonElement>>(), It.IsAny<CancellationToken>()), Moq.Times.Never);
    }

    [AvaloniaFact]
    public async Task CancelOperation_CancelsEngineAndRestoresControls() {
        var fs = new InMemoryFileSystem();
        await using var main = new MainViewModel(new PlaceholderChatSession(), new GuiSessionStore(fs, "mem/sessions"), new GuiPreferencesStore(fs, "mem/preferences"));
        var session = new Moq.Mock<IJccChatSession>();
        session.Setup(s => s.GetToolInfoAsync("mcp_wait", It.IsAny<CancellationToken>())).ReturnsAsync(new ToolInfo());
        session.Setup(s => s.ExecuteToolAsync("mcp_wait", It.IsAny<Dictionary<string, System.Text.Json.JsonElement>>(), It.IsAny<CancellationToken>()))
            .Returns((string name, Dictionary<string, System.Text.Json.JsonElement> args, CancellationToken token) => WaitForCancellationAsync(token));
        var vm = new WorkbenchViewModel(main, () => session.Object) { SelectedTool = new ToolSummary("mcp_wait", "wait") };
        var operation = vm.RunToolCommand.ExecuteAsync(null);
        vm.IsWorking.Should().BeTrue();
        vm.CancelOperationCommand.Execute(null);
        await operation.WaitAsync(TimeSpan.FromSeconds(5));
        vm.Status.Should().Be("已取消操作");
        vm.IsWorking.Should().BeFalse();
    }

    private static async Task<ToolResult> WaitForCancellationAsync(CancellationToken token) {
        var completion = new TaskCompletionSource<ToolResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var registration = token.Register(() => completion.TrySetCanceled(token));
        return await completion.Task;
    }

    [AvaloniaFact]
    public async Task PluginButtons_RouteToExistingCommand() {
        var fs = new InMemoryFileSystem();
        await using var main = new MainViewModel(new PlaceholderChatSession(), new GuiSessionStore(fs, "mem/sessions"), new GuiPreferencesStore(fs, "mem/preferences"));
        var session = new Moq.Mock<IJccChatSession>();
        session.Setup(s => s.GetAvailableSlashCommands()).Returns([new SlashCommandMetadata { Name = "/plugin", Usage = "usage" }]);
        session.Setup(s => s.ExecuteSlashCommandAsync("/plugin enable sample", It.IsAny<CancellationToken>())).ReturnsAsync("enabled");
        var vm = new WorkbenchViewModel(main, () => session.Object) { PluginTarget = "sample" };
        await vm.PluginActionCommand.ExecuteAsync("enable");
        vm.OperationResult.Should().Be("enabled");
    }

    [AvaloniaTheory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task Workbench_AllTabsRenderAtMinimumWidth(int tab) {
        var fs = new InMemoryFileSystem();
        await using var main = new MainViewModel(new PlaceholderChatSession(), new GuiSessionStore(fs, "mem/sessions"), new GuiPreferencesStore(fs, "mem/preferences"));
        var window = new WorkbenchWindow { Width = 760, Height = 680, DataContext = main.Workbench };
        try {
            window.Show(); main.Workbench.TabIndex = tab;
            Dispatcher.UIThread.RunJobs();
            window.GetVisualDescendants().OfType<TabControl>().Single().SelectedIndex.Should().Be(tab);
            window.GetVisualDescendants().OfType<Button>().Where(b => b.IsVisible && b.Bounds.Width > 0).Should().NotBeEmpty();
        } finally { window.Close(); }
    }

    [Fact]
    public async Task WorkspaceReader_ReadsRealRepositoryAndGitWithoutModelCalls() {
        var root = FindRoot();
        WorkspaceReader.List(root).Should().Contain(f => f.Name == "app" && f.IsDirectory);
        (await WorkspaceReader.ReadAsync(System.IO.Path.Combine(root, "app", "gui", "JoinCodeGui.csproj"))).Should().Contain("Project");
        (await WorkspaceReader.GitAsync(root, "status", "--short")).Should().NotBeNullOrWhiteSpace();
        (await WorkspaceReader.GitAsync(root, "diff", "--no-ext-diff", "--no-color")).Should().NotBeNullOrWhiteSpace();
    }

    [AvaloniaFact]
    public async Task InvalidDirectory_ReportsActionableFailureWithoutCrashing() {
        var fs = new InMemoryFileSystem();
        await using var main = new MainViewModel(new PlaceholderChatSession(), new GuiSessionStore(fs, "mem/sessions"), new GuiPreferencesStore(fs, "mem/preferences"));
        main.Workbench.Directory = System.IO.Path.Combine(FindRoot(), "directory_that_does_not_exist");
        await main.Workbench.RefreshFilesCommand.ExecuteAsync(null);
        main.Workbench.Status.Should().Contain("操作失败");
        main.Workbench.IsWorking.Should().BeFalse();
    }

    [AvaloniaTheory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task CaptureWorkbench(int tab) {
        var fs = new InMemoryFileSystem();
        await using var main = new MainViewModel(new PlaceholderChatSession(), new GuiSessionStore(fs, "mem/sessions"), new GuiPreferencesStore(fs, "mem/preferences"));
        main.AnimationsEnabled = false;
        var vm = main.Workbench;
        vm.Directory = FindRoot();
        await vm.RefreshFilesCommand.ExecuteAsync(null);
        vm.FileContent = "// JoinCode 工作台\npublic sealed class Workspace {\n    public string Name => \"JoinCode\";\n}";
        vm.GitContent = "diff --git a/workspace.cs b/workspace.cs\n@@ -1 +1 @@\n-old workspace\n+new workspace";
        vm.OperationResult = "操作结果会显示在这里。";
        var window = new WorkbenchWindow { DataContext = vm, Width = 1100, Height = 780 };
        try {
            window.Show(); vm.TabIndex = tab;
            Dispatcher.UIThread.RunJobs();
            if (tab == 0) {
                var editor = window.GetVisualDescendants().OfType<AvaloniaEdit.TextEditor>().Single(e => e.Name == "CodePreview");
                editor.Text.Should().Contain("Workspace");
                editor.GetVisualDescendants().OfType<ScrollViewer>().Should().NotBeEmpty("code preview must have a rendered editor template");
            }
            using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No Skia frame.");
#pragma warning disable JCC9001 // Diagnostic screenshots follow the repository dumps convention.
            var folder = System.IO.Path.Combine(FindRoot(), "dumps", "gui_workbench");
            System.IO.Directory.CreateDirectory(folder);
            frame.Save(System.IO.Path.Combine(folder, $"tab-{tab}.png"));
#pragma warning restore JCC9001
        } finally { window.Close(); }
    }

    private static string FindRoot() {
        var root = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
#pragma warning disable JCC9001 // Locate the real repository for read-only integration checks and screenshot artifacts.
        while (root.Parent is not null && !System.IO.File.Exists(System.IO.Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
#pragma warning restore JCC9001
        return root.FullName;
    }
}
