namespace Host.Tests.Tui;

/// <summary>
/// TuiCommandProcessor 单元测试 — 验证斜杠命令解析与执行。
/// 覆盖 /help /clear /history /shell /build /test /save /load /unknown。
/// </summary>
public class TuiCommandProcessorTests
{
    [Fact]
    public void Help_ReturnsCommandList()
    {
        var result = TuiCommandProcessor.Process("/help");
        Assert.True(result.IsHandled);
        Assert.Contains("/help", result.Output);
        Assert.Contains("/clear", result.Output);
        Assert.Contains("/shell", result.Output);
        Assert.Contains("/build", result.Output);
        Assert.Contains("/test", result.Output);
    }

    [Fact]
    public void Clear_ReturnsClearAction()
    {
        var result = TuiCommandProcessor.Process("/clear");
        Assert.True(result.IsHandled);
        Assert.Equal(TuiCommandAction.ClearOutput, result.Action);
    }

    [Fact]
    public void History_WithEmptyHistory_ReturnsEmptyMessage()
    {
        var history = new MessageList();
        var result = TuiCommandProcessor.Process("/history", history);
        Assert.True(result.IsHandled);
        Assert.Contains("无历史", result.Output);
    }

    [Fact]
    public void History_WithMessages_ReturnsFormattedMessage()
    {
        var history = new MessageList();
        history.AddUserMessage("hello");
        history.AddAssistantMessage("hi there");
        var result = TuiCommandProcessor.Process("/history", history);
        Assert.True(result.IsHandled);
        Assert.Contains("hello", result.Output);
        Assert.Contains("hi there", result.Output);
    }

    [Fact]
    public void Shell_WithCommand_ReturnsShellAction()
    {
        var result = TuiCommandProcessor.Process("/shell echo hello");
        Assert.True(result.IsHandled);
        Assert.Equal(TuiCommandAction.ExecuteShell, result.Action);
        Assert.Equal("echo hello", result.ShellCommand);
    }

    [Fact]
    public void Shell_WithoutCommand_ReturnsUsageHint()
    {
        var result = TuiCommandProcessor.Process("/shell");
        Assert.True(result.IsHandled);
        Assert.Contains("用法", result.Output);
    }

    [Fact]
    public void Build_ReturnsBuildAction()
    {
        var result = TuiCommandProcessor.Process("/build");
        Assert.True(result.IsHandled);
        Assert.Equal(TuiCommandAction.ExecuteBuild, result.Action);
    }

    [Fact]
    public void Test_ReturnsTestAction()
    {
        var result = TuiCommandProcessor.Process("/test");
        Assert.True(result.IsHandled);
        Assert.Equal(TuiCommandAction.ExecuteTest, result.Action);
    }

    [Fact]
    public void Save_WithEmptyHistory_ReturnsWarning()
    {
        var history = new MessageList();
        var result = TuiCommandProcessor.Process("/save", history);
        Assert.True(result.IsHandled);
        Assert.Contains("无历史", result.Output);
    }

    [Fact]
    public void Save_WithMessages_ReturnsSaveAction()
    {
        var history = new MessageList();
        history.AddUserMessage("test");
        var result = TuiCommandProcessor.Process("/save", history);
        Assert.True(result.IsHandled);
        Assert.Equal(TuiCommandAction.SaveSession, result.Action);
    }

    [Fact]
    public void UnknownCommand_ReturnsError()
    {
        var result = TuiCommandProcessor.Process("/foobar");
        Assert.True(result.IsHandled);
        Assert.Contains("未知命令", result.Output);
    }

    [Fact]
    public void NonSlashCommand_ReturnsNotHandled()
    {
        var result = TuiCommandProcessor.Process("hello world");
        Assert.False(result.IsHandled);
    }

    [Fact]
    public void Exit_ReturnsExitAction()
    {
        var result = TuiCommandProcessor.Process("/exit");
        Assert.True(result.IsHandled);
        Assert.Equal(TuiCommandAction.Exit, result.Action);
    }

    [Fact]
    public void Grep_WithPattern_ReturnsGrepAction()
    {
        var result = TuiCommandProcessor.Process("/grep TODO");
        Assert.True(result.IsHandled);
        Assert.Equal(TuiCommandAction.ExecuteGrep, result.Action);
        Assert.Equal("TODO", result.ShellCommand);
    }

    [Fact]
    public void Grep_WithoutPattern_ReturnsUsageHint()
    {
        var result = TuiCommandProcessor.Process("/grep");
        Assert.True(result.IsHandled);
        Assert.Contains("用法", result.Output);
    }

    [Fact]
    public void Diff_ReturnsDiffAction()
    {
        var result = TuiCommandProcessor.Process("/diff");
        Assert.True(result.IsHandled);
        Assert.Equal(TuiCommandAction.ExecuteDiff, result.Action);
    }

    [Fact]
    public void Files_WithPattern_ReturnsFilesAction()
    {
        var result = TuiCommandProcessor.Process("/files *.cs");
        Assert.True(result.IsHandled);
        Assert.Equal(TuiCommandAction.ExecuteFiles, result.Action);
        Assert.Equal("*.cs", result.ShellCommand);
    }

    [Fact]
    public void Files_WithoutPattern_ReturnsDefaultFilesAction()
    {
        var result = TuiCommandProcessor.Process("/files");
        Assert.True(result.IsHandled);
        Assert.Equal(TuiCommandAction.ExecuteFiles, result.Action);
        Assert.Equal("*", result.ShellCommand);
    }

    [Fact]
    public void Open_WithFile_ReturnsOpenAction()
    {
        var result = TuiCommandProcessor.Process("/open README.md");
        Assert.True(result.IsHandled);
        Assert.Equal(TuiCommandAction.ExecuteOpen, result.Action);
        Assert.Equal("README.md", result.ShellCommand);
    }

    [Fact]
    public void Open_WithoutFile_ReturnsUsageHint()
    {
        var result = TuiCommandProcessor.Process("/open");
        Assert.True(result.IsHandled);
        Assert.Contains("用法", result.Output);
    }

    [Fact]
    public void Patch_WithFile_ReturnsPatchAction()
    {
        var result = TuiCommandProcessor.Process("/patch fix.patch");
        Assert.True(result.IsHandled);
        Assert.Equal(TuiCommandAction.ExecutePatch, result.Action);
        Assert.Equal("fix.patch", result.ShellCommand);
    }

    [Fact]
    public void Patch_WithoutFile_ReturnsUsageHint()
    {
        var result = TuiCommandProcessor.Process("/patch");
        Assert.True(result.IsHandled);
        Assert.Contains("用法", result.Output);
    }

    [Fact]
    public void Apply_WithFile_ReturnsApplyAction()
    {
        var result = TuiCommandProcessor.Process("/apply fix.patch");
        Assert.True(result.IsHandled);
        Assert.Equal(TuiCommandAction.ExecuteApply, result.Action);
        Assert.Equal("fix.patch", result.ShellCommand);
    }

    [Fact]
    public void Apply_WithoutFile_ReturnsUsageHint()
    {
        var result = TuiCommandProcessor.Process("/apply");
        Assert.True(result.IsHandled);
        Assert.Contains("用法", result.Output);
    }

    [Fact]
    public void Undo_ReturnsUndoAction()
    {
        var result = TuiCommandProcessor.Process("/undo");
        Assert.True(result.IsHandled);
        Assert.Equal(TuiCommandAction.ExecuteUndo, result.Action);
    }

    [Fact]
    public void Load_WithFile_ReturnsLoadAction()
    {
        var result = TuiCommandProcessor.Process("/load session.txt");
        Assert.True(result.IsHandled);
        Assert.Equal(TuiCommandAction.ExecuteLoad, result.Action);
        Assert.Equal("session.txt", result.ShellCommand);
    }

    [Fact]
    public void Load_WithoutFile_ReturnsUsageHint()
    {
        var result = TuiCommandProcessor.Process("/load");
        Assert.True(result.IsHandled);
        Assert.Contains("用法", result.Output);
    }

    [Fact]
    public void Config_ReturnsConfigAction()
    {
        var result = TuiCommandProcessor.Process("/config");
        Assert.True(result.IsHandled);
        Assert.Equal(TuiCommandAction.ShowConfig, result.Action);
    }

    [Fact]
    public void Model_WithoutArg_ReturnsShowModelAction()
    {
        var result = TuiCommandProcessor.Process("/model");
        Assert.True(result.IsHandled);
        Assert.Equal(TuiCommandAction.ShowModel, result.Action);
    }

    [Fact]
    public void Model_WithArg_ReturnsSetModelAction()
    {
        var result = TuiCommandProcessor.Process("/model gpt-4o");
        Assert.True(result.IsHandled);
        Assert.Equal(TuiCommandAction.SetModel, result.Action);
        Assert.Equal("gpt-4o", result.ShellCommand);
    }

    [Fact]
    public void Sessions_ReturnsSessionsAction()
    {
        var result = TuiCommandProcessor.Process("/sessions");
        Assert.True(result.IsHandled);
        Assert.Equal(TuiCommandAction.ListSessions, result.Action);
    }

    [Fact]
    public void Tokens_ReturnsTokensAction()
    {
        var result = TuiCommandProcessor.Process("/tokens");
        Assert.True(result.IsHandled);
        Assert.Equal(TuiCommandAction.ShowTokens, result.Action);
    }

    [Fact]
    public void ClearHistory_ReturnsClearHistoryAction()
    {
        var result = TuiCommandProcessor.Process("/clear-history");
        Assert.True(result.IsHandled);
        Assert.Equal(TuiCommandAction.ClearHistory, result.Action);
    }
}
