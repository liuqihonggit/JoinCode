namespace Host.Tests.Tui.Views;

/// <summary>
/// OutputView 单元测试 — 验证追加/清空/上限行为（ListView 内容用 GetLines 断言）。
/// P2-2 滚动优化：Label 全量重绘 → ListView + ObservableCollection。
/// </summary>
public class OutputViewTests
{
    [Fact]
    public void Empty_GetLines_ReturnsEmpty()
    {
        var view = new OutputView();
        Assert.Empty(view.GetLines());
    }

    [Fact]
    public void AppendLine_GetLines_ContainsLine()
    {
        var view = new OutputView();
        view.AppendLine("👤 hello");
        view.Flush();

        var lines = view.GetLines();
        Assert.Single(lines);
        Assert.Equal("👤 hello", lines[0]);
    }

    [Fact]
    public void AppendText_MultipleLines_AllPresent()
    {
        var view = new OutputView();
        view.AppendText("line1\nline2\nline3");
        view.Flush();

        var lines = view.GetLines();
        Assert.Equal(3, lines.Count);
        Assert.Equal("line1", lines[0]);
        Assert.Equal("line2", lines[1]);
        Assert.Equal("line3", lines[2]);
    }

    [Fact]
    public void Clear_GetLines_Empty()
    {
        var view = new OutputView();
        view.AppendLine("a");
        view.AppendLine("b");
        view.Clear();

        Assert.Empty(view.GetLines());
    }

    [Fact]
    public void MaxLines_Exceeded_OldestRemoved()
    {
        var view = new OutputView(maxLines: 10000);
        for (var i = 0; i < 10005; i++)
            view.AppendLine($"line{i}");
        view.Flush();

        var lines = view.GetLines();
        Assert.Equal(10000, lines.Count);
        Assert.Equal("line5", lines[0]);
        Assert.Equal("line10004", lines[^1]);
    }
}
