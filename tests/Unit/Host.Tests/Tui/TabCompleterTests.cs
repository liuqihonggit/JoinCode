namespace Host.Tests.Tui;

/// <summary>
/// TabCompleter 单元测试 — 验证斜杠命令 Tab 补全。
/// </summary>
public class TabCompleterTests
{
    [Fact]
    public void EmptyInput_ReturnsNull()
    {
        Assert.Null(TabCompleter.Complete(""));
    }

    [Fact]
    public void NonSlashInput_ReturnsNull()
    {
        Assert.Null(TabCompleter.Complete("hello"));
    }

    [Fact]
    public void JustSlash_ReturnsFirstCommand()
    {
        var result = TabCompleter.Complete("/");
        Assert.NotNull(result);
        Assert.StartsWith("/", result);
    }

    [Fact]
    public void PartialMatch_ReturnsCompletion()
    {
        var result = TabCompleter.Complete("/he");
        Assert.Equal("/help", result);
    }

    [Fact]
    public void FullMatch_ReturnsSameCommand()
    {
        var result = TabCompleter.Complete("/help");
        Assert.Equal("/help", result);
    }

    [Fact]
    public void NoMatch_ReturnsNull()
    {
        Assert.Null(TabCompleter.Complete("/xyz"));
    }

    [Fact]
    public void PartialMatch_MultipleMatches_ReturnsFirstAlphabetically()
    {
        var result = TabCompleter.Complete("/c");
        Assert.NotNull(result);
        Assert.StartsWith("/c", result);
    }

    [Fact]
    public void CaseInsensitive_Match()
    {
        var result = TabCompleter.Complete("/HELP");
        Assert.Equal("/help", result);
    }
}
