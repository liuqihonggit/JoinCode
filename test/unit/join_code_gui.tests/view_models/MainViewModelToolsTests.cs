namespace JoinCode.Gui.Tests.ViewModels;

/// <summary>
/// MainViewModel 工具转发测试 — 任务5：GUI 按钮直接调用引擎工具。
/// 覆盖：参数解析、结果文本提取、命令执行（占位会话返回错误提示）。
/// </summary>
public class MainViewModelToolsTests {
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static MainViewModel CreateVm() => new(
        new JoinCode.Gui.Hosting.PlaceholderChatSession(),
        new GuiSessionStore(new InMemoryFileSystem(), "mem/sessions"),
        new GuiPreferencesStore(new InMemoryFileSystem(), "mem/gui-preferences.json"));

    // ── ParseToolParameter ──

    [Fact]
    public void ParseToolParameter_PlainName_ReturnsEmptyArgs() {
        var (name, args) = MainViewModel.ParseToolParameter("git_status");
        name.Should().Be("git_status");
        args.Should().BeEmpty();
    }

    [Fact]
    public void ParseToolParameter_WithJsonArgs_ParsesCorrectly() {
        var (name, args) = MainViewModel.ParseToolParameter("file_read:{\"path\":\"a.txt\"}");
        name.Should().Be("file_read");
        args.Should().ContainKey("path");
        args["path"].GetString().Should().Be("a.txt");
    }

    [Fact]
    public void ParseToolParameter_WithMultipleJsonArgs_ParsesAll() {
        var (name, args) = MainViewModel.ParseToolParameter("bash:{\"command\":\"ls\",\"timeout\":5000}");
        name.Should().Be("bash");
        args.Should().HaveCount(2);
        args["command"].GetString().Should().Be("ls");
        args["timeout"].GetInt32().Should().Be(5000);
    }

    [Fact]
    public void ParseToolParameter_WithEmptyJson_ReturnsEmptyArgs() {
        var (name, args) = MainViewModel.ParseToolParameter("git_status:{}");
        name.Should().Be("git_status");
        args.Should().BeEmpty();
    }

    [Fact]
    public void ParseToolParameter_WithInvalidJson_ReturnsEmptyArgs() {
        var (name, args) = MainViewModel.ParseToolParameter("bash:{invalid}");
        name.Should().Be("bash");
        args.Should().BeEmpty("无效 JSON 应回空参数");
    }

    [Fact]
    public void ParseToolParameter_WithTrailingColon_ReturnsEmptyArgs() {
        var (name, args) = MainViewModel.ParseToolParameter("git_status:");
        name.Should().Be("git_status");
        args.Should().BeEmpty();
    }

    [Fact]
    public void ParseToolParameter_WithWhitespace_TrimsName() {
        var (name, _) = MainViewModel.ParseToolParameter("  git_status  ");
        name.Should().Be("git_status");
    }

    // ── ExtractToolResultText ──

    [Fact]
    public void ExtractToolResultText_EmptyContent_ReturnsEmpty() {
        var result = new ToolResult();
        MainViewModel.ExtractToolResultText(result).Should().BeEmpty();
    }

    [Fact]
    public void ExtractToolResultText_SingleTextContent_ReturnsText() {
        var result = new ToolResult {
            Content = [new() { Text = "hello world" }]
        };
        MainViewModel.ExtractToolResultText(result).Should().Be("hello world");
    }

    [Fact]
    public void ExtractToolResultText_MultipleTextContents_JoinsAll() {
        var result = new ToolResult {
            Content = [
                new() { Text = "line1" },
                new() { Text = "line2" },
                new() { Text = "line3" }
            ]
        };
        MainViewModel.ExtractToolResultText(result).Should().Contain("line1").And.Contain("line2").And.Contain("line3");
    }

    [Fact]
    public void ExtractToolResultText_NullTextEntries_SkipsNulls() {
        var result = new ToolResult {
            Content = [
                new() { Text = null },
                new() { Text = "valid" },
                new() { Text = "" }
            ]
        };
        MainViewModel.ExtractToolResultText(result).Should().Be("valid");
    }

    // ── ExecuteToolCommand ──

    [Fact]
    public async Task ExecuteToolCommand_WithNullParameter_DoesNothing() {
        var vm = CreateVm();
        var initialCount = vm.Messages.Count;
        await Task.Run(() => vm.ExecuteToolCommand.ExecuteAsync(null)).WaitAsync(Timeout);
        vm.Messages.Count.Should().Be(initialCount, "null 参数不应产生消息");
    }

    [Fact]
    public async Task ExecuteToolCommand_WithEmptyParameter_DoesNothing() {
        var vm = CreateVm();
        var initialCount = vm.Messages.Count;
        await Task.Run(() => vm.ExecuteToolCommand.ExecuteAsync("")).WaitAsync(Timeout);
        vm.Messages.Count.Should().Be(initialCount, "空参数不应产生消息");
    }

    [Fact]
    public async Task ExecuteToolCommand_WithToolName_ShowsResultMessage() {
        var vm = CreateVm();
        await Task.Run(() => vm.ExecuteToolCommand.ExecuteAsync("git_status")).WaitAsync(Timeout);

        vm.Messages.Should().NotBeEmpty();
        vm.Messages.Should().Contain(m => m.Content.Contains("git_status"), "应回显工具名");
        vm.Messages.Should().Contain(m => m.Content.Contains("占位会话") || m.Content.Contains("⚠"),
            "占位会话应返回错误提示");
    }

    [Fact]
    public async Task ExecuteToolCommand_WithToolNameAndArgs_ShowsResultMessage() {
        var vm = CreateVm();
        await Task.Run(() => vm.ExecuteToolCommand.ExecuteAsync("file_read:{\"path\":\"test.txt\"}")).WaitAsync(Timeout);

        vm.Messages.Should().NotBeEmpty();
        vm.Messages.Should().Contain(m => m.Content.Contains("file_read"), "应回显工具名");
    }
}
