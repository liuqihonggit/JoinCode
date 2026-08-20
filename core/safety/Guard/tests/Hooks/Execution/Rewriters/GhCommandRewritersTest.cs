namespace Guard.Tests.Hooks.Execution.Rewriters;

/// <summary>
/// GhPrBodyRewriter 单元测试 — 验证 CanRewrite 匹配 gh pr create / Rewrite 自动添加 --body / HasBodyParameter 检测
/// </summary>
public sealed class GhPrBodyRewriterTest
{
    private readonly GhPrBodyRewriter _rewriter = new();

    // === 元数据 ===

    [Fact]
    public void Name_ReturnsExpectedName()
    {
        _rewriter.Name.Should().Be("GhPrBodyRewriter");
    }

    [Fact]
    public void Priority_Returns100()
    {
        _rewriter.Priority.Should().Be(100);
    }

    // === CanRewrite ===

    [Theory]
    [InlineData("gh pr create --title foo")]
    [InlineData("gh pr create")]
    [InlineData("  gh pr create --title foo")]
    [InlineData("GH PR CREATE --title foo")]
    [InlineData("Gh Pr Create --title foo")]
    [InlineData("gh.exe pr create --title foo")]
    public void CanRewrite_MatchingCommands_ReturnsTrue(string command)
    {
        _rewriter.CanRewrite(command).Should().BeTrue();
    }

    [Theory]
    [InlineData("gh pr list")]
    [InlineData("gh issue create")]
    [InlineData("git commit")]
    [InlineData("echo gh pr create")]
    [InlineData("gh pr")]
    [InlineData("gh")]
    public void CanRewrite_NonMatchingCommands_ReturnsFalse(string command)
    {
        _rewriter.CanRewrite(command).Should().BeFalse();
    }

    // === Rewrite — body 注入 ===

    [Fact]
    public void Rewrite_NoBodyParameter_AddsBodyFromContext()
    {
        var context = new Dictionary<string, object>
        {
            ["pr_body"] = "My PR body"
        };

        var result = _rewriter.Rewrite("gh pr create --title foo", context);

        result.Should().Contain("--body");
        result.Should().Contain("My PR body");
        result.Should().StartWith("gh pr create --title foo --body");
    }

    [Fact]
    public void Rewrite_NoBodyInContext_UsesDefaultTemplate()
    {
        var context = new Dictionary<string, object>
        {
            ["pr_title"] = "My Title",
            ["head_branch"] = "feature"
        };

        var result = _rewriter.Rewrite("gh pr create --title foo", context);

        result.Should().Contain("--body");
        result.Should().Contain("My Title");
    }

    [Fact]
    public void Rewrite_EmptyContext_UsesDefaultTemplateWithDefaultTitle()
    {
        var context = new Dictionary<string, object>();

        var result = _rewriter.Rewrite("gh pr create --title foo", context);

        result.Should().Contain("--body");
        result.Should().Contain("变更内容");
    }

    [Fact]
    public void Rewrite_BodyFromGenericContextKey()
    {
        var context = new Dictionary<string, object>
        {
            ["body"] = "Generic body content"
        };

        var result = _rewriter.Rewrite("gh pr create --title foo", context);

        result.Should().Contain("Generic body content");
    }

    [Fact]
    public void Rewrite_PrBodyTakesPrecedenceOverGenericBody()
    {
        var context = new Dictionary<string, object>
        {
            ["pr_body"] = "Specific PR body",
            ["body"] = "Generic body"
        };

        var result = _rewriter.Rewrite("gh pr create --title foo", context);

        result.Should().Contain("Specific PR body");
        result.Should().NotContain("Generic body");
    }

    // === Rewrite — HasBodyParameter 检测（间接） ===

    [Fact]
    public void Rewrite_AlreadyHasLongBodyParameter_ReturnsUnchanged()
    {
        var command = "gh pr create --title foo --body existing";

        var result = _rewriter.Rewrite(command, new Dictionary<string, object>());

        result.Should().Be(command);
    }

    [Fact]
    public void Rewrite_AlreadyHasShortBodyParameter_ReturnsUnchanged()
    {
        var command = "gh pr create -b existing";

        var result = _rewriter.Rewrite(command, new Dictionary<string, object>());

        result.Should().Be(command);
    }

    [Fact]
    public void Rewrite_HasBodyWithDifferentCase_ReturnsUnchanged()
    {
        var command = "gh pr create --BODY existing";

        var result = _rewriter.Rewrite(command, new Dictionary<string, object>());

        result.Should().Be(command);
    }

    // === Rewrite — 转义 ===

    [Fact]
    public void Rewrite_BodyWithNewlines_EscapesNewlines()
    {
        var context = new Dictionary<string, object>
        {
            ["pr_body"] = "line1\nline2"
        };

        var result = _rewriter.Rewrite("gh pr create --title foo", context);

        result.Should().Contain("\\n");
    }

    [Fact]
    public void Rewrite_BodyWithDoubleQuotes_EscapesQuotes()
    {
        var context = new Dictionary<string, object>
        {
            ["pr_body"] = "say \"hello\""
        };

        var result = _rewriter.Rewrite("gh pr create --title foo", context);

        result.Should().Contain("\\\"");
    }

    [Fact]
    public void Rewrite_BodyWithBackslash_EscapesBackslash()
    {
        var context = new Dictionary<string, object>
        {
            ["pr_body"] = "path\\to\\file"
        };

        var result = _rewriter.Rewrite("gh pr create --title foo", context);

        result.Should().Contain("\\\\");
    }

    [Fact]
    public void Rewrite_BodyWithCarriageReturn_EscapesCarriageReturn()
    {
        var context = new Dictionary<string, object>
        {
            ["pr_body"] = "line1\rline2"
        };

        var result = _rewriter.Rewrite("gh pr create --title foo", context);

        result.Should().Contain("\\r");
    }
}

/// <summary>
/// GhTimeoutRewriter 单元测试 — 验证 gh 命令匹配与不改写行为
/// </summary>
public sealed class GhTimeoutRewriterTest
{
    private readonly GhTimeoutRewriter _rewriter = new();

    [Fact]
    public void Name_ReturnsExpectedName()
    {
        _rewriter.Name.Should().Be("GhTimeoutRewriter");
    }

    [Fact]
    public void Priority_Returns50()
    {
        _rewriter.Priority.Should().Be(50);
    }

    [Theory]
    [InlineData("gh pr list")]
    [InlineData("gh repo view")]
    [InlineData("  gh pr list")]
    [InlineData("GH pr list")]
    [InlineData("gh.exe pr list")]
    public void CanRewrite_GhCommands_ReturnsTrue(string command)
    {
        _rewriter.CanRewrite(command).Should().BeTrue();
    }

    [Theory]
    [InlineData("git status")]
    [InlineData("echo gh")]
    [InlineData("gh")]
    public void CanRewrite_NonGhCommands_ReturnsFalse(string command)
    {
        _rewriter.CanRewrite(command).Should().BeFalse();
    }

    [Fact]
    public void Rewrite_ReturnsCommandUnchanged()
    {
        var result = _rewriter.Rewrite("gh pr list", new Dictionary<string, object>());

        result.Should().Be("gh pr list");
    }
}
