namespace Mcp.Tests;

public sealed partial class GitHubToolHandlersTests {
    [Fact]
    public async Task PrCreate_AutoMergeTrue_CallsEnableAutoMergeAfterCreate() {
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true,
            StatusCode = 201,
            Body = """{"number":42,"node_id":"PR_kwDOTVZE0c8AAAABCsFVdw","title":"feat","state":"open"}""",
        });
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true,
            StatusCode = 200,
            Body = """{"data":{"enablePullRequestAutoMerge":{"pullRequest":{"number":42}}}}""",
        });

        var result = await _handler.GhPrCreateAsync("feat", "feature-branch", @base: "main", auto_merge: true, merge_method: "squash", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("auto-merge");
        _api.LastMethod.Should().Be(HttpMethod.Post);
        _api.LastPath.Should().Be("graphql");
        _api.LastBody.Should().Contain("enablePullRequestAutoMerge");
        _api.LastBody.Should().Contain("SQUASH");
        _api.LastBody.Should().Contain("PR_kwDOTVZE0c8AAAABCsFVdw");
    }

    [Fact]
    public async Task PrCreate_AutoMergeFalse_DoesNotCallGraphQL() {
        _api.NextResponse = new GitHubApiResponse {
            Success = true,
            StatusCode = 201,
            Body = """{"number":43,"node_id":"PR_xxx","title":"feat","state":"open"}""",
        };

        var result = await _handler.GhPrCreateAsync("feat", "feature-branch", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        _api.LastPath.Should().Be("repos/owner/repo/pulls");
    }

    [Fact]
    public async Task PrCreate_RequestBody_OmitsNullMaintainerCanModify() {
        _api.NextResponse = new GitHubApiResponse {
            Success = true,
            StatusCode = 201,
            Body = """{"number":44,"title":"t","state":"open"}""",
        };

        await _handler.GhPrCreateAsync("t", "feat", @base: "main", repo: "owner/repo");

        _api.LastBody.Should().NotContain("maintainer_can_modify");
    }
}
