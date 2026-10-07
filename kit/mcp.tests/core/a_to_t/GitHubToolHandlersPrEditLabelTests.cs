namespace Mcp.Tests;

public sealed partial class GitHubToolHandlersTests {
    [Fact]
    public async Task PrEdit_WithLabel_PatchesIssuesEndpoint() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = "{}" });

        await _handler.GhPrEditAsync("42", label: "bug,enhancement", repo: "owner/repo");

        _api.LastMethod.Should().Be(HttpMethod.Patch);
        _api.LastPath.Should().Be("repos/owner/repo/issues/42");
        _api.LastBody.Should().Contain("\"labels\":[\"bug\",\"enhancement\"]");
    }

    [Fact]
    public async Task PrEdit_WithAssignee_PatchesIssuesEndpoint() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = "{}" });

        await _handler.GhPrEditAsync("42", assignee: "alice,bob", repo: "owner/repo");

        _api.LastMethod.Should().Be(HttpMethod.Patch);
        _api.LastPath.Should().Be("repos/owner/repo/issues/42");
        _api.LastBody.Should().Contain("\"assignees\":[\"alice\",\"bob\"]");
    }

    [Fact]
    public async Task PrEdit_WithTitleAndLabel_PatchesBothEndpoints() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = "{}" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = "{}" });

        await _handler.GhPrEditAsync("42", title: "new title", label: "bug", repo: "owner/repo");

        _api.LastPath.Should().Be("repos/owner/repo/issues/42");
    }
}
