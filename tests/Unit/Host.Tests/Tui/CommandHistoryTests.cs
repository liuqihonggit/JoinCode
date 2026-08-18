namespace Host.Tests.Tui;

/// <summary>
/// CommandHistory 单元测试 — 验证上下箭头导航历史命令。
/// </summary>
public class CommandHistoryTests
{
    [Fact]
    public void Empty_NavigateUp_ReturnsNull()
    {
        var history = new CommandHistory();
        Assert.Null(history.NavigateUp());
    }

    [Fact]
    public void Empty_NavigateDown_ReturnsNull()
    {
        var history = new CommandHistory();
        Assert.Null(history.NavigateDown());
    }

    [Fact]
    public void Add_ThenNavigateUp_ReturnsLastCommand()
    {
        var history = new CommandHistory();
        history.Add("/help");
        Assert.Equal("/help", history.NavigateUp());
    }

    [Fact]
    public void MultipleCommands_NavigateUp_ReturnsInReverseOrder()
    {
        var history = new CommandHistory();
        history.Add("/help");
        history.Add("/clear");
        history.Add("/build");
        Assert.Equal("/build", history.NavigateUp());
        Assert.Equal("/clear", history.NavigateUp());
        Assert.Equal("/help", history.NavigateUp());
    }

    [Fact]
    public void NavigateUp_ThenDown_ReturnsToEmpty()
    {
        var history = new CommandHistory();
        history.Add("/help");
        history.Add("/clear");
        history.NavigateUp();
        history.NavigateUp();
        Assert.Equal("/clear", history.NavigateDown());
        Assert.Null(history.NavigateDown());
    }

    [Fact]
    public void Add_ResetsNavigationPosition()
    {
        var history = new CommandHistory();
        history.Add("/help");
        history.Add("/clear");
        history.NavigateUp();
        history.Add("/build");
        Assert.Equal("/build", history.NavigateUp());
    }

    [Fact]
    public void DuplicateConsecutive_NotAdded()
    {
        var history = new CommandHistory();
        history.Add("/help");
        history.Add("/help");
        Assert.Single(history.AllCommands);
    }

    [Fact]
    public void MaxCapacity_20_Commands()
    {
        var history = new CommandHistory();
        for (var i = 0; i < 25; i++)
            history.Add($"/cmd{i}");
        Assert.Equal(20, history.AllCommands.Count);
        Assert.Equal("/cmd24", history.AllCommands[^1]);
    }
}
