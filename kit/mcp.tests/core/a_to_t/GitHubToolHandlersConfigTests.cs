namespace Mcp.Tests;

public sealed partial class GitHubToolHandlersTests {
    private static string GetTestConfigPath() => GitHubToolHandlers.GetGhConfigPath(false);

    [Fact]
    public async Task ConfigGet_ReturnsValue_WhenKeyExists() {
        var configPath = GetTestConfigPath();
        var fs = new InMemoryFileSystem();
        await fs.WriteAllTextAsync(configPath, "git_protocol: https\neditor:\nprompt: enabled\n");
        var handler = new GitHubToolHandlers(new FakeDownloader(), fs, new PersistencePipeline(new InMemoryFileSystem()), new FakeGitHubApiClient(), null, NullLogger<GitHubToolHandlers>.Instance);

        var result = await handler.GhConfigGetAsync("git_protocol");

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Be("https");
    }

    [Fact]
    public async Task ConfigGet_ReturnsEmpty_WhenValueIsEmpty() {
        var configPath = GetTestConfigPath();
        var fs = new InMemoryFileSystem();
        await fs.WriteAllTextAsync(configPath, "git_protocol: https\neditor:\n");
        var handler = new GitHubToolHandlers(new FakeDownloader(), fs, new PersistencePipeline(new InMemoryFileSystem()), new FakeGitHubApiClient(), null, NullLogger<GitHubToolHandlers>.Instance);

        var result = await handler.GhConfigGetAsync("editor");

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task ConfigGet_ReturnsFail_WhenKeyNotExists() {
        var configPath = GetTestConfigPath();
        var fs = new InMemoryFileSystem();
        await fs.WriteAllTextAsync(configPath, "git_protocol: https\n");
        var handler = new GitHubToolHandlers(new FakeDownloader(), fs, new PersistencePipeline(new InMemoryFileSystem()), new FakeGitHubApiClient(), null, NullLogger<GitHubToolHandlers>.Instance);

        var result = await handler.GhConfigGetAsync("nonexistent");

        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task ConfigGet_ReturnsFail_WhenFileNotExists() {
        var fs = new InMemoryFileSystem();
        var handler = new GitHubToolHandlers(new FakeDownloader(), fs, new PersistencePipeline(new InMemoryFileSystem()), new FakeGitHubApiClient(), null, NullLogger<GitHubToolHandlers>.Instance);

        var result = await handler.GhConfigGetAsync("git_protocol");

        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task ConfigSet_UpdatesValue_WhenKeyExists() {
        var configPath = GetTestConfigPath();
        var fs = new InMemoryFileSystem();
        await fs.WriteAllTextAsync(configPath, "git_protocol: https\neditor:\n");
        var handler = new GitHubToolHandlers(new FakeDownloader(), fs, new PersistencePipeline(new InMemoryFileSystem()), new FakeGitHubApiClient(), null, NullLogger<GitHubToolHandlers>.Instance);

        await handler.GhConfigSetAsync("git_protocol", "ssh");

        var content = await fs.ReadAllTextAsync(configPath);
        content.Should().Contain("git_protocol: ssh");
        content.Should().NotContain("git_protocol: https");
    }

    [Fact]
    public async Task ConfigSet_AppendsValue_WhenKeyNotExists() {
        var configPath = GetTestConfigPath();
        var fs = new InMemoryFileSystem();
        await fs.WriteAllTextAsync(configPath, "git_protocol: https\n");
        var handler = new GitHubToolHandlers(new FakeDownloader(), fs, new PersistencePipeline(new InMemoryFileSystem()), new FakeGitHubApiClient(), null, NullLogger<GitHubToolHandlers>.Instance);

        await handler.GhConfigSetAsync("editor", "vim");

        var content = await fs.ReadAllTextAsync(configPath);
        content.Should().Contain("editor: vim");
    }
}
