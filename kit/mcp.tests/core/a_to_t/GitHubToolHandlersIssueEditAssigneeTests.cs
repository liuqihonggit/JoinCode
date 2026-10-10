namespace Mcp.Tests;

public sealed partial class GitHubToolHandlersTests {
    [Fact]
    public async Task IssueEdit_AddAssignee_PostsToAssignees() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = "{}" });

        await _handler.GhIssueEditAsync("42", add_assignee: "alice,bob", common: new GitHubCommonOptions { Repo = "owner/repo" });

        _api.LastMethod.Should().Be(HttpMethod.Post);
        _api.LastPath.Should().Be("repos/owner/repo/issues/42/assignees");
        _api.LastBody.Should().Contain("\"assignees\":[\"alice\",\"bob\"]");
    }

    [Fact]
    public async Task IssueEdit_RemoveAssignee_DeletesFromAssignees() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = "{}" });

        await _handler.GhIssueEditAsync("42", remove_assignee: "charlie", common: new GitHubCommonOptions { Repo = "owner/repo" });

        _api.LastMethod.Should().Be(HttpMethod.Delete);
        _api.LastPath.Should().Be("repos/owner/repo/issues/42/assignees");
        _api.LastBody.Should().Contain("\"assignees\":[\"charlie\"]");
    }
}
