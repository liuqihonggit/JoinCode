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

    [Fact]
    public async Task AliasList_ReturnsAliases_WhenSectionExists() {
        var configPath = GetTestConfigPath();
        var fs = new InMemoryFileSystem();
        await fs.WriteAllTextAsync(configPath, "git_protocol: https\naliases:\n    co: pr checkout\n    il: issue list\n");
        var handler = new GitHubToolHandlers(new FakeDownloader(), fs, new PersistencePipeline(new InMemoryFileSystem()), new FakeGitHubApiClient(), null, NullLogger<GitHubToolHandlers>.Instance);

        var result = await handler.GhAliasListAsync();

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().Contain("co: pr checkout");
        text.Should().Contain("il: issue list");
    }

    [Fact]
    public async Task AliasList_ReturnsEmpty_WhenNoAliases() {
        var configPath = GetTestConfigPath();
        var fs = new InMemoryFileSystem();
        await fs.WriteAllTextAsync(configPath, "git_protocol: https\n");
        var handler = new GitHubToolHandlers(new FakeDownloader(), fs, new PersistencePipeline(new InMemoryFileSystem()), new FakeGitHubApiClient(), null, NullLogger<GitHubToolHandlers>.Instance);

        var result = await handler.GhAliasListAsync();

        result.IsError.Should().BeFalse();
    }

    [Fact]
    public async Task AliasSet_AddsAlias_ToSection() {
        var configPath = GetTestConfigPath();
        var fs = new InMemoryFileSystem();
        await fs.WriteAllTextAsync(configPath, "git_protocol: https\naliases:\n    co: pr checkout\n");
        var handler = new GitHubToolHandlers(new FakeDownloader(), fs, new PersistencePipeline(new InMemoryFileSystem()), new FakeGitHubApiClient(), null, NullLogger<GitHubToolHandlers>.Instance);

        await handler.GhAliasSetAsync("il", "issue list");

        var content = await fs.ReadAllTextAsync(configPath);
        content.Should().Contain("il: issue list");
    }

    [Fact]
    public async Task AliasSet_UpdatesExistingAlias() {
        var configPath = GetTestConfigPath();
        var fs = new InMemoryFileSystem();
        await fs.WriteAllTextAsync(configPath, "git_protocol: https\naliases:\n    co: pr checkout\n");
        var handler = new GitHubToolHandlers(new FakeDownloader(), fs, new PersistencePipeline(new InMemoryFileSystem()), new FakeGitHubApiClient(), null, NullLogger<GitHubToolHandlers>.Instance);

        await handler.GhAliasSetAsync("co", "pr checkout --draft");

        var content = await fs.ReadAllTextAsync(configPath);
        content.Should().Contain("co: pr checkout --draft");
        content.Should().NotContain("co: pr checkout\n");
    }

    [Fact]
    public async Task AliasDelete_RemovesAlias_FromSection() {
        var configPath = GetTestConfigPath();
        var fs = new InMemoryFileSystem();
        await fs.WriteAllTextAsync(configPath, "git_protocol: https\naliases:\n    co: pr checkout\n    il: issue list\n");
        var handler = new GitHubToolHandlers(new FakeDownloader(), fs, new PersistencePipeline(new InMemoryFileSystem()), new FakeGitHubApiClient(), null, NullLogger<GitHubToolHandlers>.Instance);

        await handler.GhAliasDeleteAsync("co");

        var content = await fs.ReadAllTextAsync(configPath);
        content.Should().NotContain("co: pr checkout");
        content.Should().Contain("il: issue list");
    }

    [Fact]
    public async Task AliasDelete_ReturnsFail_WhenAliasNotExists() {
        var configPath = GetTestConfigPath();
        var fs = new InMemoryFileSystem();
        await fs.WriteAllTextAsync(configPath, "git_protocol: https\naliases:\n    co: pr checkout\n");
        var handler = new GitHubToolHandlers(new FakeDownloader(), fs, new PersistencePipeline(new InMemoryFileSystem()), new FakeGitHubApiClient(), null, NullLogger<GitHubToolHandlers>.Instance);

        var result = await handler.GhAliasDeleteAsync("nonexistent");

        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task ExtensionList_ReturnsEmpty_WhenDirNotExists() {
        var fs = new InMemoryFileSystem();
        var handler = new GitHubToolHandlers(new FakeDownloader(), fs, new PersistencePipeline(new InMemoryFileSystem()), new FakeGitHubApiClient(), null, NullLogger<GitHubToolHandlers>.Instance);

        var result = await handler.GhExtensionListAsync();

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("无已安装扩展");
    }

    [Fact]
    public async Task ExtensionList_ReturnsExtensions_WhenDirHasExtensions() {
        var extDir = GitHubToolHandlers.GetGhExtensionsPath();
        var fs = new InMemoryFileSystem();
        fs.CreateDirectory(Path.Combine(extDir, "gh-jump"));
        fs.CreateDirectory(Path.Combine(extDir, "gh-learn"));
        var handler = new GitHubToolHandlers(new FakeDownloader(), fs, new PersistencePipeline(new InMemoryFileSystem()), new FakeGitHubApiClient(), null, NullLogger<GitHubToolHandlers>.Instance);

        var result = await handler.GhExtensionListAsync();

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().Contain("gh-jump");
        text.Should().Contain("gh-learn");
    }
}
