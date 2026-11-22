namespace Composition.Tests.Commands;

/// <summary>
/// GitHubRepoNameValidator 单元测试 — 白名单正则拒绝命令注入串
/// <para>B3 修复: InstallGitHubAppCommand git remote URL 命令注入, repoName 提取后必须白名单校验</para>
/// </summary>
public sealed class GitHubRepoNameValidatorTests {
    [Theory]
    [InlineData("owner/repo")]
    [InlineData("a-b/c_d")]
    [InlineData("a.b/c-d")]
    [InlineData("o123/r456")]
    [InlineData("JoinCode/JoinCode")]
    public void Validate_LegalRepoName_ShouldReturnTrue(string repoName) {
        var result = GitHubRepoNameValidator.TryValidate(repoName, out var validated, out var error);

        result.Should().BeTrue($"合法 repoName '{repoName}' 应通过白名单校验");
        validated.Should().Be(repoName);
        error.Should().BeNull();
    }

    [Theory]
    [InlineData("foo && start calc")]
    [InlineData("foo;rm -rf /")]
    [InlineData("$(evil)")]
    [InlineData("foo|bar")]
    [InlineData("owner/repo;malicious")]
    [InlineData("owner/repo$(cmd)")]
    [InlineData("owner/repo`whoami`")]
    [InlineData("owner/repo\nmalicious")]
    [InlineData("owner/repo>etc")]
    [InlineData("owner/repo<etc")]
    [InlineData("owner/repo&calc")]
    [InlineData(" owner/repo")]
    [InlineData("owner/repo ")]
    [InlineData("")]
    [InlineData("noSlash")]
    [InlineData("owner/")]
    [InlineData("/repo")]
    [InlineData("a/b/c")]
    public void Validate_InjectionPayload_ShouldReturnFalse(string repoName) {
        var result = GitHubRepoNameValidator.TryValidate(repoName, out var validated, out var error);

        result.Should().BeFalse($"注入串 '{repoName}' 应被白名单拒绝");
        validated.Should().BeNull();
        error.Should().NotBeNullOrEmpty("拒绝时必须给出引导文案(四要素: 为什么拒绝 + 触发参数 + 哪个守卫 + 正确写法)");
    }

    [Fact]
    public void Validate_NormalizesDotGitSuffix() {
        var result = GitHubRepoNameValidator.TryValidate("owner/repo.git", out var validated, out _);

        result.Should().BeTrue();
        validated.Should().Be("owner/repo", ".git 后缀应被规范化移除");
    }

    [Fact]
    public void Error_ContainsRustStyleGuidance() {
        GitHubRepoNameValidator.TryValidate("foo && start calc", out _, out var error);

        error.Should().NotBeNull();
        error.Should().Contain("foo && start calc", "报错必须包含触发参数");
        error.Should().Contain("GitHubRepoNameValidator", "报错必须指明拦截守卫层名");
        error.Should().Contain("owner/repo", "报错必须给出正确写法示例");
    }
}
