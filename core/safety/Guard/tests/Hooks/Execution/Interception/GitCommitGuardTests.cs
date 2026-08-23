namespace Guard.Tests.Hooks.Execution.Interception;

/// <summary>
/// GitCommitGuard 单元测试 — 验证 git commit 检测命中/不命中、Redirect 引导
/// </summary>
public sealed class GitCommitGuardTests
{
    private static readonly IReadOnlyDictionary<string, object> EmptyContext =
        FrozenDictionary<string, object>.Empty;

    // === CanHandle 命中 ===

    [Theory]
    [InlineData("git commit -m \"msg\"")]
    [InlineData("git commit")]
    [InlineData("git.exe commit -m x")]
    [InlineData("  git commit -m x")]
    [InlineData("git commit --amend")]
    [InlineData("\"git\" commit -m x")]
    [InlineData("\"C:\\Program Files\\Git\\bin\\git.exe\" commit -m x")]
    public void CanHandle_GitCommitCommand_ReturnsTrue(string command)
    {
        var guard = new GitCommitGuard();

        guard.CanHandle(command, EmptyContext).Should().BeTrue();
    }

    // === CanHandle 不命中 ===

    [Theory]
    [InlineData("git status")]
    [InlineData("git add -A")]
    [InlineData("git push")]
    [InlineData("git log --oneline")]
    [InlineData("git diff")]
    [InlineData("gh pr create")]
    [InlineData("dotnet build")]
    [InlineData("echo git commit")]
    [InlineData("gitcommit")]
    [InlineData("")]
    public void CanHandle_NonGitCommitCommand_ReturnsFalse(string command)
    {
        var guard = new GitCommitGuard();

        guard.CanHandle(command, EmptyContext).Should().BeFalse();
    }

    // === Evaluate 返回 Redirect ===

    [Fact]
    public void Evaluate_ReturnsRedirectToCommit()
    {
        var guard = new GitCommitGuard();

        var decision = guard.Evaluate("git commit -m x", EmptyContext);

        var redirect = decision.Should().BeOfType<CommandDecision.Redirect>().Subject;
        redirect.TargetTool.Should().Be("/commit");
        redirect.Hint.Should().Contain("/commit");
        redirect.Hint.Should().Contain("禁止");
    }

    [Fact]
    public void Evaluate_HintContainsSubtractionHonestyGuidance()
    {
        var guard = new GitCommitGuard();

        var decision = guard.Evaluate("git commit", EmptyContext);

        var redirect = decision.Should().BeOfType<CommandDecision.Redirect>().Subject;
        redirect.Hint.Should().Contain("减法诚实");
    }

    // === 优先级 ===

    [Fact]
    public void Priority_IsHighest()
    {
        var guard = new GitCommitGuard();

        guard.Priority.Should().Be(1000);
    }

    // === 名称 ===

    [Fact]
    public void Name_IsGitCommitGuard()
    {
        new GitCommitGuard().Name.Should().Be("GitCommitGuard");
    }
}
