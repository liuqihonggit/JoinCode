namespace Mcp.Tests;

/// <summary>
/// run view start_line 参数测试 — GAP-038-04: 行号定位跳转(1-indexed,等价 skip_lines=N-1)
/// </summary>
public sealed partial class GitHubToolHandlersTests {
    [Fact]
    public async Task RunView_StartLine_ReturnsLinesFromStartPosition() {
        var lines = Enumerable.Range(0, 100).Select(i => $"line {i}").ToArray();
        _api.NextLogLines = lines;

        var result = await _handler.GhRunViewAsync("42", log: true, max_lines: 10, start_line: 51, common: new GitHubCommonOptions { Repo = "owner/repo" });

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().Contain("line 50");
        text.Should().Contain("line 59");
        text.Should().NotContain("line 49");
    }

    [Fact]
    public async Task RunView_StartLine1_EquivalentToSkip0() {
        var lines = Enumerable.Range(0, 10).Select(i => $"line {i}").ToArray();
        _api.NextLogLines = lines;

        var result = await _handler.GhRunViewAsync("42", log: true, max_lines: 5, start_line: 1, common: new GitHubCommonOptions { Repo = "owner/repo" });

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().Contain("line 0");
        text.Should().Contain("line 4");
    }
}
