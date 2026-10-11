namespace Mcp.Tests;

public sealed partial class GitHubToolHandlersTests {
    [Fact]
    public async Task RepoCreate_WithSource_InitsGitAndAddsRemote() {
        var api = new FakeGitHubApiClient { NextResponse = new GitHubApiResponse { Success = true, StatusCode = 201, Body = """{"full_name":"o/myrepo"}""" } };
        var git = new FakeGitCommandRunner();
        var handler = CreateHandlerWithGitAndApi(git, api);

        await handler.GhRepoCreateAsync("myrepo", source: "/some/path");

        git.ExecutedCommands.Should().Contain("init");
        git.ExecutedCommands.Should().Contain(c => c.Contains("remote add origin") && c.Contains("myrepo"));
    }

    [Fact]
    public async Task RepoCreate_WithSourceAndPush_PushesToRemote() {
        var api = new FakeGitHubApiClient { NextResponse = new GitHubApiResponse { Success = true, StatusCode = 201, Body = """{"full_name":"o/myrepo"}""" } };
        var git = new FakeGitCommandRunner();
        var handler = CreateHandlerWithGitAndApi(git, api);

        await handler.GhRepoCreateAsync("myrepo", source: "/some/path", push: true);

        git.ExecutedCommands.Should().Contain("init");
        git.ExecutedCommands.Should().Contain(c => c.Contains("remote add origin"));
        git.ExecutedCommands.Should().Contain("push -u origin HEAD");
    }

    [Fact]
    public async Task RepoCreate_WithSourceEmpty_UsesWorkingDir() {
        var api = new FakeGitHubApiClient { NextResponse = new GitHubApiResponse { Success = true, StatusCode = 201, Body = """{"full_name":"o/myrepo"}""" } };
        var git = new FakeGitCommandRunner();
        var handler = CreateHandlerWithGitAndApi(git, api);

        await handler.GhRepoCreateAsync("myrepo", source: "", common: new GitHubCommonOptions { WorkingDir = "/my/work" });

        git.ExecutedCommands.Should().Contain("init");
    }

    [Fact]
    public async Task RepoCreate_WithSource_NoPush_DoesNotPush() {
        var api = new FakeGitHubApiClient { NextResponse = new GitHubApiResponse { Success = true, StatusCode = 201, Body = """{"full_name":"o/myrepo"}""" } };
        var git = new FakeGitCommandRunner();
        var handler = CreateHandlerWithGitAndApi(git, api);

        await handler.GhRepoCreateAsync("myrepo", source: "/some/path");

        git.ExecutedCommands.Should().NotContain("push -u origin HEAD");
    }
}
