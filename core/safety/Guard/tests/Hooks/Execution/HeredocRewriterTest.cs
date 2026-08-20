namespace Guard.Tests.Hooks.Execution;

/// <summary>
/// HeredocRewriter 单元测试 — 验证 HEREDOC 检测和双引号字符串转换
/// </summary>
public sealed class HeredocRewriterTest
{
    private readonly HeredocRewriter _rewriter = new();

    // === CanRewrite ===

    [Fact]
    public void CanRewrite_CommandSubstitutionHeredoc_ReturnsTrue()
    {
        var command = "gh pr create --body \"$(cat <<'EOF'\n## Summary\n- change 1\nEOF\n)\"";

        _rewriter.CanRewrite(command).Should().BeTrue();
    }

    [Fact]
    public void CanRewrite_StandaloneHeredoc_ReturnsTrue()
    {
        var command = "cat <<'EOF'\ncontent\nEOF";

        _rewriter.CanRewrite(command).Should().BeTrue();
    }

    [Fact]
    public void CanRewrite_NoHeredoc_ReturnsFalse()
    {
        var command = "gh pr create --title \"feat: add feature\" --body \"normal body\"";

        _rewriter.CanRewrite(command).Should().BeFalse();
    }

    [Fact]
    public void CanRewrite_UnquotedDelimiter_ReturnsTrue()
    {
        var command = "$(cat <<EOF\ncontent\nEOF)";

        _rewriter.CanRewrite(command).Should().BeTrue();
    }

    [Fact]
    public void CanRewrite_DoubleQuotedDelimiter_ReturnsTrue()
    {
        var command = "$(cat <<\"EOF\"\ncontent\nEOF)";

        _rewriter.CanRewrite(command).Should().BeTrue();
    }

    // === Rewrite ===

    [Fact]
    public void Rewrite_CommandSubstitution_ConvertsToDoubleQuotedString()
    {
        var command = "gh pr create --body \"$(cat <<'EOF'\n## Summary\n- change 1\nEOF\n)\"";

        var result = _rewriter.Rewrite(command, FrozenDictionary<string, object>.Empty);

        result.Should().Contain("## Summary\n- change 1");
        result.Should().NotContain("<<'EOF'");
        result.Should().NotContain("$(cat");
    }

    [Fact]
    public void Rewrite_StandaloneHeredoc_ConvertsToDoubleQuotedString()
    {
        var command = "cat <<'EOF'\nline1\nline2\nEOF";

        var result = _rewriter.Rewrite(command, FrozenDictionary<string, object>.Empty);

        result.Should().Contain("\"line1\nline2\"");
        result.Should().NotContain("<<'EOF'");
    }

    [Fact]
    public void Rewrite_NoHeredoc_ReturnsOriginal()
    {
        var command = "gh pr create --title \"feat\" --body \"normal\"";

        var result = _rewriter.Rewrite(command, FrozenDictionary<string, object>.Empty);

        result.Should().Be(command);
    }

    [Fact]
    public void Rewrite_ContentWithDoubleQuotes_EscapesCorrectly()
    {
        var command = "$(cat <<'EOF'\nsay \"hello\"\nEOF)";

        var result = _rewriter.Rewrite(command, FrozenDictionary<string, object>.Empty);

        result.Should().Contain("\\\"hello\\\"");
    }

    [Fact]
    public void Rewrite_ContentWithBackslash_EscapesCorrectly()
    {
        var command = "$(cat <<'EOF'\npath\\to\\file\nEOF)";

        var result = _rewriter.Rewrite(command, FrozenDictionary<string, object>.Empty);

        result.Should().Contain("\\\\to\\\\file");
    }

    [Fact]
    public void Rewrite_MultilineContent_PreservesNewlines()
    {
        var command = "$(cat <<'EOF'\nline1\nline2\nline3\nEOF)";

        var result = _rewriter.Rewrite(command, FrozenDictionary<string, object>.Empty);

        result.Should().Contain("line1\nline2\nline3");
    }

    [Fact]
    public void Rewrite_CustomDelimiter_WorksWithAnyDelimiter()
    {
        var command = "$(cat <<'END'\ncontent\nEND)";

        var result = _rewriter.Rewrite(command, FrozenDictionary<string, object>.Empty);

        result.Should().Contain("content");
        result.Should().NotContain("<<'END'");
    }

    [Fact]
    public void Rewrite_GhPrCreateWithHeredocBody_ConvertsBody()
    {
        var command = "gh pr create --title \"feat: add X\" --body \"$(cat <<'EOF'\n## Summary\n- Added X feature\n- Updated tests\nEOF\n)\"";

        var result = _rewriter.Rewrite(command, FrozenDictionary<string, object>.Empty);

        result.Should().StartWith("gh pr create --title");
        result.Should().Contain("\"## Summary\n- Added X feature\n- Updated tests\"");
        result.Should().NotContain("$(cat");
    }

    // === 优先级和名称 ===

    [Fact]
    public void Priority_IsHighest_EnsuresHeredocProcessedFirst()
    {
        _rewriter.Priority.Should().Be(200);
    }

    [Fact]
    public void Name_IsHeredocRewriter()
    {
        _rewriter.Name.Should().Be("HeredocRewriter");
    }
}
