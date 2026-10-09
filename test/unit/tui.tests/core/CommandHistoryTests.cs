namespace Tui.Tests;

/// <summary>
/// CommandHistory 单元测试 — 验证上下箭头导航历史命令。
/// </summary>
public class CommandHistoryTests {
    [Fact]
    public void Empty_NavigateUp_ReturnsFalse() {
        var history = new CommandHistory();
        Assert.False(history.TryNavigateUp(out _));
    }

    [Fact]
    public void Empty_NavigateDown_ReturnsFalse() {
        var history = new CommandHistory();
        Assert.False(history.TryNavigateDown(out _));
    }

    [Fact]
    public void Add_ThenNavigateUp_ReturnsLastCommand() {
        var history = new CommandHistory();
        history.Add("/help");
        Assert.True(history.TryNavigateUp(out var cmd));
        Assert.Equal("/help", cmd);
    }

    [Fact]
    public void MultipleCommands_NavigateUp_ReturnsInReverseOrder() {
        var history = new CommandHistory();
        history.Add("/help");
        history.Add("/clear");
        history.Add("/build");
        Assert.True(history.TryNavigateUp(out var cmd1));
        Assert.Equal("/build", cmd1);
        Assert.True(history.TryNavigateUp(out var cmd2));
        Assert.Equal("/clear", cmd2);
        Assert.True(history.TryNavigateUp(out var cmd3));
        Assert.Equal("/help", cmd3);
    }

    [Fact]
    public void NavigateUp_ThenDown_ReturnsToEmpty() {
        var history = new CommandHistory();
        history.Add("/help");
        history.Add("/clear");
        history.TryNavigateUp(out _);
        history.TryNavigateUp(out _);
        Assert.True(history.TryNavigateDown(out var cmd));
        Assert.Equal("/clear", cmd);
        Assert.False(history.TryNavigateDown(out _));
    }

    [Fact]
    public void Add_ResetsNavigationPosition() {
        var history = new CommandHistory();
        history.Add("/help");
        history.Add("/clear");
        history.TryNavigateUp(out _);
        history.Add("/build");
        Assert.True(history.TryNavigateUp(out var cmd));
        Assert.Equal("/build", cmd);
    }

    [Fact]
    public void DuplicateConsecutive_NotAdded() {
        var history = new CommandHistory();
        history.Add("/help");
        history.Add("/help");
        Assert.Single(history.AllCommands);
    }

    [Fact]
    public void MaxCapacity_20_Commands() {
        var history = new CommandHistory();
        for (var i = 0; i < 25; i++)
            history.Add($"/cmd{i}");
        Assert.Equal(20, history.AllCommands.Count);
        Assert.Equal("/cmd24", history.AllCommands[^1]);
    }
}
