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
}
