namespace Mcp.Tests;

public sealed partial class GitHubToolHandlersTests {
    [Theory]
    [InlineData("ssh://git@ssh.github.com:443/liuqihonggit/JoinCode.git", "liuqihonggit", "JoinCode")]
    [InlineData("ssh://git@github.com/liuqihonggit/JoinCode.git", "liuqihonggit", "JoinCode")]
    [InlineData("https://github.com/liuqihonggit/JoinCode.git", "liuqihonggit", "JoinCode")]
    [InlineData("https://github.com/liuqihonggit/JoinCode", "liuqihonggit", "JoinCode")]
    [InlineData("git@github.com:liuqihonggit/JoinCode.git", "liuqihonggit", "JoinCode")]
    [InlineData("git@github.com:liuqihonggit/JoinCode", "liuqihonggit", "JoinCode")]
    public void ParseGitHubRemoteUrl_ValidUrls_ReturnsOwnerRepo(string url, string expectedOwner, string expectedRepo) {
        var result = GitHubToolHandlers.ParseGitHubRemoteUrl(url);
        result.Should().NotBeNull();
        if (result is not { } resolved) return;
        resolved.owner.Should().Be(expectedOwner);
        resolved.repo.Should().Be(expectedRepo);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-url")]
    [InlineData("ftp://example.com/foo/bar.git")]
    public void ParseGitHubRemoteUrl_InvalidUrls_ReturnsNull(string url) {
        var result = GitHubToolHandlers.ParseGitHubRemoteUrl(url);
        result.Should().BeNull();
    }
}
