namespace Mcp.Tests;

public sealed partial class GitHubToolHandlersTests {
    [Fact]
    public async Task IssueCreate_WithProject_CallsAddProjectV2ItemById() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 201, Body = """{"number":42,"node_id":"I_kw123"}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"data":{"organization":{"projectV2":{"id":"PVT_kw456"}}}}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"data":{"addProjectV2ItemById":{"item":{"id":"PVTI_kw789"}}}}""" });

        await _handler.GhIssueCreateAsync("test issue", project: 1, repo: "owner/repo");

        _api.LastMethod.Should().Be(HttpMethod.Post);
        _api.LastPath.Should().Be("graphql");
        _api.LastBody.Should().Contain("addProjectV2ItemById");
        _api.LastBody.Should().Contain("PVT_kw456");
        _api.LastBody.Should().Contain("I_kw123");
    }

    [Fact]
    public async Task IssueCreate_WithoutProject_DoesNotCallGraphQL() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 201, Body = """{"number":42,"node_id":"I_kw123"}""" });

        await _handler.GhIssueCreateAsync("test issue", repo: "owner/repo");

        _api.LastPath.Should().Be("repos/owner/repo/issues");
    }
}
