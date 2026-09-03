namespace Tui.Tests;

/// <summary>
/// TabCompleter 单元测试 — 验证斜杠命令 Tab 补全。
/// </summary>
public class TabCompleterTests
{
    private static readonly IReadOnlyList<string> Commands =
        ["/help", "/exit", "/clear", "/history", "/model", "/config", "/sessions", "/tokens", "/clear-history"];

    [Fact]
    public void EmptyInput_ReturnsNull()
    {
        Assert.Null(TabCompleter.Complete("", Commands));
    }

    [Fact]
    public void NonSlashInput_ReturnsNull()
    {
        Assert.Null(TabCompleter.Complete("hello", Commands));
    }

    [Fact]
    public void JustSlash_ReturnsFirstCommand()
    {
        var result = TabCompleter.Complete("/", Commands);
        Assert.NotNull(result);
        Assert.StartsWith("/", result);
    }

    [Fact]
    public void PartialMatch_ReturnsCompletion()
    {
        var result = TabCompleter.Complete("/he", Commands);
        Assert.Equal("/help", result);
    }

    [Fact]
    public void FullMatch_ReturnsSameCommand()
    {
        var result = TabCompleter.Complete("/help", Commands);
        Assert.Equal("/help", result);
    }

    [Fact]
    public void NoMatch_ReturnsNull()
    {
        Assert.Null(TabCompleter.Complete("/xyz", Commands));
    }

    [Fact]
    public void PartialMatch_MultipleMatches_ReturnsFirstAlphabetically()
    {
        var result = TabCompleter.Complete("/c", Commands);
        Assert.NotNull(result);
        Assert.StartsWith("/c", result);
    }

    [Fact]
    public void CaseInsensitive_Match()
    {
        var result = TabCompleter.Complete("/HELP", Commands);
        Assert.Equal("/help", result);
    }
}
