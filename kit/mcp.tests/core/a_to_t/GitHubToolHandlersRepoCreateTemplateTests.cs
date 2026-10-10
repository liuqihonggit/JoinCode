namespace Mcp.Tests;

public sealed partial class GitHubToolHandlersTests {
    [Fact]
    public async Task RepoCreate_WithTemplateAndIncludeAllBranches_SerializesIncludeAllBranches() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 201, Body = """{"full_name":"o/r","clone_url":"https://x"}""" };

        await _handler.GhRepoCreateAsync("myrepo", template: "template/repo", include_all_branches: true);

        _api.LastPath.Should().Contain("template/repo/generate");
        _api.LastBody.Should().Contain("\"include_all_branches\":true");
    }

    [Fact]
    public async Task RepoSync_WithSourceParameter_RoutesToMergeUpstream() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = "{}" };

        var result = await _handler.GhRepoSyncAsync(branch: "main", source: "upstream/repo", common: new GitHubCommonOptions { Repo = "owner/repo" });

        result.IsError.Should().BeFalse();
        _api.LastPath.Should().Be("repos/owner/repo/merge-upstream");
    }
}
