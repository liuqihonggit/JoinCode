namespace Sync.Tests.ToolHandlers;

/// <summary>
/// SubmoduleParallelDownloader 单元测试 — URL 解析等纯逻辑
/// </summary>
public sealed class SubmoduleParallelDownloaderTests {
    [Theory]
    [InlineData("git@github.com:owner/repo.git", "owner", "repo")]
    [InlineData("https://github.com/owner/repo.git", "owner", "repo")]
    [InlineData("git@github.com:liuqihonggit/Editor.git", "liuqihonggit", "Editor")]
    [InlineData("https://github.com/liuqihonggit/Terminal.Gui.git", "liuqihonggit", "Terminal.Gui")]
    [InlineData("git@github.com:liuqihonggit/Dock.git", "liuqihonggit", "Dock")]
    public void ParseGitHubUrl_ValidUrls_ReturnsOwnerRepo(string url, string expectedOwner, string expectedRepo) {
        if (SubmoduleParallelDownloader.ParseGitHubUrl(url) is (string owner, string repo)) {
            owner.Should().Be(expectedOwner);
            repo.Should().Be(expectedRepo);
        } else {
            Assert.Fail($"ParseGitHubUrl 返回 null: {url}");
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-url")]
    [InlineData("git@github.com:only-owner")]
    public void ParseGitHubUrl_InvalidUrls_ReturnsNull(string url) {
        SubmoduleParallelDownloader.ParseGitHubUrl(url).Should().BeNull();
    }

    [Fact]
    public void ParseGitHubUrl_SshWithPort_ReturnsOwnerRepo() {
        if (SubmoduleParallelDownloader.ParseGitHubUrl("ssh://git@ssh.github.com:443/liuqihonggit/JoinCode.git") is (string owner, string repo)) {
            owner.Should().Be("liuqihonggit");
            repo.Should().Be("JoinCode");
        } else {
            Assert.Fail("ParseGitHubUrl 返回 null for SSH URL with port");
        }
    }
}
