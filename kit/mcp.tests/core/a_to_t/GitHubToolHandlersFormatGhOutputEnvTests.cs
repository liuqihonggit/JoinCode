namespace Mcp.Tests;

/// <summary>
/// FormatGhOutput 环境变量默认值测试 — GAP-038-03: JCC_OUTPUT_FORMAT 系统变量切换三档阅读模式
/// </summary>
public sealed partial class GitHubToolHandlersTests {
    [Fact]
    public void FormatGhOutput_NullVerbosity_NoEnv_UsesSummarizer() {
        using var env = EnvVarScope.Set("JCC_OUTPUT_FORMAT", null);
        var result = GitHubToolHandlers.FormatGhOutput("body", null, null, _ => "summarized");
        result.Should().Be("summarized");
    }

    [Fact]
    public void FormatGhOutput_NullVerbosity_CompactJsonEnv_UsesCompactJson() {
        using var env = EnvVarScope.Set("JCC_OUTPUT_FORMAT", "compact_json");
        var body = """{"id":1,"number":2,"title":"x","state":"open","name":"n"}""";
        var result = GitHubToolHandlers.FormatGhOutput(body, null, null, _ => "summarized");
        result.Should().Contain("\"number\":2");
        result.Should().NotContain("summarized");
    }

    [Fact]
    public void FormatGhOutput_NullVerbosity_FullJsonEnv_UsesFullJson() {
        using var env = EnvVarScope.Set("JCC_OUTPUT_FORMAT", "full_json");
        var result = GitHubToolHandlers.FormatGhOutput("raw_body", null, null, _ => "summarized");
        result.Should().Be("raw_body");
    }

    [Fact]
    public void FormatGhOutput_NullVerbosity_GhStyleEnv_UsesSummarizer() {
        using var env = EnvVarScope.Set("JCC_OUTPUT_FORMAT", "gh_style");
        var result = GitHubToolHandlers.FormatGhOutput("body", null, null, _ => "summarized");
        result.Should().Be("summarized");
    }

    [Fact]
    public void FormatGhOutput_ExplicitVerbosity_OverridesEnv() {
        using var env = EnvVarScope.Set("JCC_OUTPUT_FORMAT", "full_json");
        var result = GitHubToolHandlers.FormatGhOutput("body", 0, null, _ => "summarized");
        result.Should().Be("summarized");
    }
}
