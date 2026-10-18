namespace Mcp.Tests;

/// <summary>
/// JCC_PAGE_SIZE 环境变量 + 混合截断策略测试 — 行数主约束 + 字符安全阀取交集
/// </summary>
public sealed partial class GitHubToolHandlersTests {
    [Fact]
    public void Pagination_DefaultPageSize_TruncatesAt200Lines() {
        using var env = EnvVarScope.Set("JCC_PAGE_SIZE", null);
        var input = BuildLines(250, "line");
        var result = GitHubToolHandlers.ApplyPaginationAndTruncation(input, null, null, toolName: "gh_test", addLineNumbers: false);
        result.Should().Contain("已截断");
        result.Should().Contain("共 250 行");
        result.Should().Contain("显示第 1-200 行");
    }

    [Fact]
    public void Pagination_JccPageSizeEnv_OverridesDefault() {
        using var env = EnvVarScope.Set("JCC_PAGE_SIZE", "50");
        var input = BuildLines(250, "line");
        var result = GitHubToolHandlers.ApplyPaginationAndTruncation(input, null, null, toolName: "gh_test", addLineNumbers: false);
        result.Should().Contain("已截断");
        result.Should().Contain("显示第 1-50 行");
    }

    [Fact]
    public void Pagination_CharSafetyValve_TruncatesBeforePageSizeWhenLinesAreLong() {
        using var envPage = EnvVarScope.Set("JCC_PAGE_SIZE", "200");
        using var envChar = EnvVarScope.Set("JCC_MAX_OUTPUT_CHARS", "5000");
        var longLine = new string('x', 200);
        var input = BuildLines(200, longLine);
        var result = GitHubToolHandlers.ApplyPaginationAndTruncation(input, null, null, toolName: "gh_test", addLineNumbers: false);
        result.Should().Contain("已截断");
        result.Should().Contain("共 200 行");
        result.Should().NotContain("显示第 1-200 行");
    }

    [Fact]
    public void Pagination_BelowThreshold_NoTruncation() {
        using var env = EnvVarScope.Set("JCC_PAGE_SIZE", "200");
        var input = BuildLines(100, "line");
        var result = GitHubToolHandlers.ApplyPaginationAndTruncation(input, null, null, toolName: "gh_test", addLineNumbers: false);
        result.Should().NotContain("已截断");
    }

    private static string BuildLines(int count, string prefix) {
        var sb = new System.Text.StringBuilder(count * (prefix.Length + 2));
        for (var i = 0; i < count; i++) {
            sb.Append(prefix).Append(' ').Append(i).Append('\n');
        }
        return sb.ToString();
    }
}
