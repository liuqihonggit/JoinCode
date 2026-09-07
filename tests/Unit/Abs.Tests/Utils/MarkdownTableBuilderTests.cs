namespace Abs.Tests.Utils;

public class MarkdownTableBuilderTests
{
    [Fact]
    public void Empty_WithoutHeader_ReturnsEmpty()
    {
        var result = new MarkdownTableBuilder().Build();
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void WithTitle_RendersTitleHeading()
    {
        var result = new MarkdownTableBuilder()
            .WithTitle("测试标题")
            .AddHeader("A", "B")
            .AddRow("1", "2")
            .Build();

        Assert.Contains("## 测试标题", result);
    }

    [Fact]
    public void AddHeader_AddRow_RendersValidMarkdownTable()
    {
        var result = new MarkdownTableBuilder()
            .AddHeader("列A", "列B", "列C")
            .AddRow("1", "2", "3")
            .AddRow("x", "y", "z")
            .Build();

        var lines = result.Split(Environment.NewLine);
        Assert.Equal("| 列A | 列B | 列C |", lines[0]);
        Assert.Equal("|---|---|---|", lines[1]);
        Assert.Equal("| 1 | 2 | 3 |", lines[2]);
        Assert.Equal("| x | y | z |", lines[3]);
    }

    [Fact]
    public void RowWithFewerValues_PadsWithEmpty()
    {
        var result = new MarkdownTableBuilder()
            .AddHeader("A", "B", "C")
            .AddRow("1")
            .Build();

        var lines = result.Split(Environment.NewLine);
        Assert.Equal("| 1 |  |  |", lines[2]);
    }

    [Fact]
    public void PipeInValue_IsEscaped()
    {
        var result = new MarkdownTableBuilder()
            .AddHeader("A")
            .AddRow("a|b")
            .Build();

        Assert.Contains("| a\\|b |", result);
    }

    [Fact]
    public void NewlineInValue_IsReplacedWithSpace()
    {
        var result = new MarkdownTableBuilder()
            .AddHeader("A")
            .AddRow("a\nb")
            .Build();

        Assert.Contains("| a b |", result);
        Assert.DoesNotContain("\n", result.Split(Environment.NewLine)[2]);
    }
}

public class CommandExecutionResultExtensionsTests
{
    [Fact]
    public void ToMarkdownTable_IncludesAllFields()
    {
        var result = new ProcessResult
        {
            ExitCode = 0,
            StandardOutput = "hello",
            StandardError = "",
            ExecutionTime = TimeSpan.FromMilliseconds(1500),
        };

        var table = result.ToMarkdownTable();

        Assert.Contains("## 命令执行结果", table);
        Assert.Contains("| 退出码 | 0 |", table);
        Assert.Contains("| 成功 | 是 |", table);
        Assert.Contains("| 执行时长 | 1.50s |", table);
        Assert.Contains("| 输出摘要 | hello |", table);
        Assert.Contains("| 错误摘要 | (空) |", table);
    }

    [Fact]
    public void ToMarkdownSummary_CompactThreeColumns()
    {
        var result = new ProcessResult
        {
            ExitCode = 1,
            StandardOutput = "out",
            StandardError = "err",
            ExecutionTime = TimeSpan.FromMilliseconds(42),
        };

        var summary = result.ToMarkdownSummary();

        var lines = summary.Split(Environment.NewLine);
        Assert.Equal("| ExitCode | Success | Duration |", lines[0]);
        Assert.Equal("|---|---|---|", lines[1]);
        Assert.Equal("| 1 | FAIL | 42ms |", lines[2]);
    }

    [Fact]
    public void ToMarkdownTable_FailedResult_ShowsNo()
    {
        var result = new ProcessResult
        {
            ExitCode = 127,
            StandardOutput = "",
            StandardError = "command not found",
            ExecutionTime = TimeSpan.FromMilliseconds(10),
        };

        var table = result.ToMarkdownTable();

        Assert.Contains("| 成功 | 否 |", table);
        Assert.Contains("| 错误摘要 | command not found |", table);
    }

    [Fact]
    public void ToMarkdownTable_LongOutput_IsTruncated()
    {
        var longText = new string('x', 200);
        var result = new ProcessResult
        {
            ExitCode = 0,
            StandardOutput = longText,
            StandardError = "",
            ExecutionTime = TimeSpan.Zero,
        };

        var table = result.ToMarkdownTable();

        Assert.Contains("...", table);
    }

    [Fact]
    public void ToJsonBlock_ProducesValidJsonBlock()
    {
        var result = new ProcessResult
        {
            ExitCode = 0,
            StandardOutput = "hello",
            StandardError = "",
            ExecutionTime = TimeSpan.FromMilliseconds(1500),
        };

        var jsonBlock = result.ToJsonBlock();

        Assert.StartsWith("```json\n", jsonBlock);
        Assert.EndsWith("```", jsonBlock);
        var json = jsonBlock.Replace("```json\n", "").Replace("\n```", "");
        Assert.Contains("\"exit_code\":0", json);
        Assert.Contains("\"success\":true", json);
        Assert.Contains("\"duration_ms\":1500", json);
    }

    [Fact]
    public void ToJsonBlock_FailedResult_HasFalseSuccess()
    {
        var result = new ProcessResult
        {
            ExitCode = 1,
            StandardOutput = "",
            StandardError = "error",
            ExecutionTime = TimeSpan.FromMilliseconds(42),
        };

        var jsonBlock = result.ToJsonBlock();
        var json = jsonBlock.Replace("```json\n", "").Replace("\n```", "");

        Assert.Contains("\"exit_code\":1", json);
        Assert.Contains("\"success\":false", json);
        Assert.Contains("\"duration_ms\":42", json);
    }
}
