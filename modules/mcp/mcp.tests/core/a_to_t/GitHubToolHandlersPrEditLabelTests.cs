namespace Mcp.Tests;

public sealed partial class GitHubToolHandlersTests {
    [Fact]
    public async Task PrEdit_WithLabel_PatchesIssuesEndpoint() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = "{}" });

        await _handler.GhPrEditAsync("42", label: "bug,enhancement", common: new GitHubCommonOptions { Repo = "owner/repo" });

        _api.LastMethod.Should().Be(HttpMethod.Patch);
        _api.LastPath.Should().Be("repos/owner/repo/issues/42");
        _api.LastBody.Should().Contain("\"labels\":[\"bug\",\"enhancement\"]");
    }

    [Fact]
    public async Task PrEdit_WithAssignee_PatchesIssuesEndpoint() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = "{}" });

        await _handler.GhPrEditAsync("42", assignee: "alice,bob", common: new GitHubCommonOptions { Repo = "owner/repo" });

        _api.LastMethod.Should().Be(HttpMethod.Patch);
        _api.LastPath.Should().Be("repos/owner/repo/issues/42");
        _api.LastBody.Should().Contain("\"assignees\":[\"alice\",\"bob\"]");
    }

    [Fact]
    public async Task PrEdit_WithTitleAndLabel_PatchesBothEndpoints() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = "{}" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = "{}" });

        await _handler.GhPrEditAsync("42", title: "new title", label: "bug", common: new GitHubCommonOptions { Repo = "owner/repo" });

        _api.LastPath.Should().Be("repos/owner/repo/issues/42");
    }

    [Fact]
    public async Task PrEdit_AddReviewer_PostsToRequestedReviewers() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = "{}" });

        await _handler.GhPrEditAsync("42", add_reviewer: "alice,bob", common: new GitHubCommonOptions { Repo = "owner/repo" });

        _api.LastMethod.Should().Be(HttpMethod.Post);
        _api.LastPath.Should().Be("repos/owner/repo/pulls/42/requested_reviewers");
        _api.LastBody.Should().Contain("\"reviewers\":[\"alice\",\"bob\"]");
    }

    [Fact]
    public async Task PrEdit_RemoveReviewer_DeletesFromRequestedReviewers() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = "{}" });

        await _handler.GhPrEditAsync("42", remove_reviewer: "charlie", common: new GitHubCommonOptions { Repo = "owner/repo" });

        _api.LastMethod.Should().Be(HttpMethod.Delete);
        _api.LastPath.Should().Be("repos/owner/repo/pulls/42/requested_reviewers");
        _api.LastBody.Should().Contain("\"reviewers\":[\"charlie\"]");
    }

    [Fact]
    public async Task PrEdit_AddAssignee_PostsToAssignees() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = "{}" });

        await _handler.GhPrEditAsync("42", add_assignee: "dave", common: new GitHubCommonOptions { Repo = "owner/repo" });

        _api.LastMethod.Should().Be(HttpMethod.Post);
        _api.LastPath.Should().Be("repos/owner/repo/issues/42/assignees");
        _api.LastBody.Should().Contain("\"assignees\":[\"dave\"]");
    }

    [Fact]
    public async Task PrEdit_RemoveAssignee_DeletesFromAssignees() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = "{}" });

        await _handler.GhPrEditAsync("42", remove_assignee: "eve", common: new GitHubCommonOptions { Repo = "owner/repo" });

        _api.LastMethod.Should().Be(HttpMethod.Delete);
        _api.LastPath.Should().Be("repos/owner/repo/issues/42/assignees");
        _api.LastBody.Should().Contain("\"assignees\":[\"eve\"]");
    }
}
