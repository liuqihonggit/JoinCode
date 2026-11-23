namespace Gh.Tests;

/// <summary>
/// FormatGhOutput 环境变量默认值测试 — GAP-038-03: JCC_OUTPUT_FORMAT 系统变量切换三档阅读模式
/// </summary>
public sealed partial class GitHubToolHandlersTests {
    [Fact]
    public void FormatGhOutput_NullVerbosity_NoEnv_UsesSummarizer() {
        using var env = EnvVarScope.Set("JCC_OUTPUT_FORMAT", null);
        var result = _handler.FormatGhOutput("body", null, null, _ => "summarized");
        result.Should().Be("summarized");
    }

    [Fact]
    public void FormatGhOutput_NullVerbosity_CompactJsonEnv_UsesCompactJson() {
        using var env = EnvVarScope.Set("JCC_OUTPUT_FORMAT", "compact_json");
        var body = """{"id":1,"number":2,"title":"x","state":"open","name":"n"}""";
        var result = _handler.FormatGhOutput(body, null, null, _ => "summarized");
        result.Should().Contain("\"number\":2");
        result.Should().NotContain("summarized");
    }

    [Fact]
    public void FormatGhOutput_NullVerbosity_FullJsonEnv_UsesFullJson() {
        using var env = EnvVarScope.Set("JCC_OUTPUT_FORMAT", "full_json");
        var result = _handler.FormatGhOutput("raw_body", null, null, _ => "summarized");
        result.Should().Be("raw_body");
    }

    [Fact]
    public void FormatGhOutput_NullVerbosity_GhStyleEnv_UsesSummarizer() {
        using var env = EnvVarScope.Set("JCC_OUTPUT_FORMAT", "gh_style");
        var result = _handler.FormatGhOutput("body", null, null, _ => "summarized");
        result.Should().Be("summarized");
    }

    [Fact]
    public void FormatGhOutput_ExplicitVerbosity_OverridesEnv() {
        using var env = EnvVarScope.Set("JCC_OUTPUT_FORMAT", "full_json");
        var result = _handler.FormatGhOutput("body", 0, null, _ => "summarized");
        result.Should().Be("summarized");
    }

    /// <summary>
    /// BUG 修复: --jq/--json_fields 在 verbosity=2(full_json) 时必须生效，
    /// 而非被 full_json early return 跳过。
    /// 根因: ShouldOutputJson 默认返回 true → resolved.Json=true → bound["verbosity"]="2" → FormatGhOutput 直接 return body。
    /// 修复: jq/json_fields 优先级高于 full_json，显式过滤先处理。
    /// </summary>
    [Fact]
    public void FormatGhOutput_FullJson_WithJq_AppliesJqNotReturnBody() {
        var body = """{"number":569,"title":"refactor","state":"open"}""";
        var result = _handler.FormatGhOutput(body, 2, null, _ => "summarized", jq: ".number");
        result.Should().Be("569");
        result.Should().NotBe(body);
    }

    [Fact]
    public void FormatGhOutput_FullJson_WithJsonFields_AppliesFilterNotReturnBody() {
        var body = """{"number":569,"title":"refactor","state":"open","body":"long..."}""";
        var result = _handler.FormatGhOutput(body, 2, "number,title", _ => "summarized");
        result.Should().Contain("\"number\":569");
        result.Should().Contain("\"title\":\"refactor\"");
        result.Should().NotContain("long...");
        result.Should().NotBe(body);
    }

    [Fact]
    public void FormatGhOutput_FullJson_NoExplicitFilters_ReturnsBody() {
        var body = """{"number":569,"title":"refactor"}""";
        var result = _handler.FormatGhOutput(body, 2, null, _ => "summarized");
        result.Should().Be(body);
    }
}
