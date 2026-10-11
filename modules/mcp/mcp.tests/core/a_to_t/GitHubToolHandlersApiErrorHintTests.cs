namespace Mcp.Tests;

public sealed partial class GitHubToolHandlersTests {
    [Fact]
    public async Task Api_AutoMergeEndpoint_404_ContainsAllowAutoMergeHint() {
        _api.NextResponse = new GitHubApiResponse { Success = false, StatusCode = 404, Error = "Not Found" };

        var result = await _handler.GhApiAsync("repos/owner/repo/pulls/42/auto-merge", method: "PUT", body: "{\"merge_method\":\"squash\"}");

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("Allow auto-merge");
        result.GetFirstText().Should().Contain("404");
    }

    [Fact]
    public async Task Api_ReposEndpoint_404_ContainsRepoNotFoundHint() {
        _api.NextResponse = new GitHubApiResponse { Success = false, StatusCode = 404, Error = "Not Found" };

        var result = await _handler.GhApiAsync("repos/nonexistent/repo", method: "GET");

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("仓库不存在");
    }

    [Fact]
    public async Task Api_MergeEndpoint_403_ContainsBranchProtectionHint() {
        _api.NextResponse = new GitHubApiResponse { Success = false, StatusCode = 403, Error = "Forbidden" };

        var result = await _handler.GhApiAsync("repos/owner/repo/pulls/42/merge", method: "PUT", body: "{}");

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("分支保护");
    }
}
