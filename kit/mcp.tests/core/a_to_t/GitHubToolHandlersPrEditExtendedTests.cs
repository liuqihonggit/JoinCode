namespace Mcp.Tests;

public sealed partial class GitHubToolHandlersTests {
    [Fact]
    public async Task PrEdit_AddLabel_PostsToLabelsEndpoint() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = "[]" });

        await _handler.GhPrEditAsync("42", add_label: "bug,enhancement", common: new GitHubCommonOptions { Repo = "owner/repo" });

        _api.LastMethod.Should().Be(HttpMethod.Post);
        _api.LastPath.Should().Be("repos/owner/repo/issues/42/labels");
        _api.LastBody.Should().Contain("\"labels\":[\"bug\",\"enhancement\"]");
    }

    [Fact]
    public async Task PrEdit_RemoveLabel_DeletesFromLabelsEndpoint() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = "[]" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 204, Body = "" });

        await _handler.GhPrEditAsync("42", remove_label: "bug,enhancement", common: new GitHubCommonOptions { Repo = "owner/repo" });

        _api.LastMethod.Should().Be(HttpMethod.Delete);
        _api.LastPath.Should().Be("repos/owner/repo/issues/42/labels/enhancement");
    }

    [Fact]
    public async Task PrEdit_BodyFile_ReadsFileContentAsBody() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = "{}" });
        var fs = new InMemoryFileSystem();
        await fs.WriteAllTextAsync("/tmp/pr_body.md", "Updated PR body from file");
        var handler = new GitHubToolHandlers(new FakeDownloader(), fs, _api, null, NullLogger<GitHubToolHandlers>.Instance);

        await handler.GhPrEditAsync("42", body_file: "/tmp/pr_body.md", common: new GitHubCommonOptions { Repo = "owner/repo" });

        _api.LastMethod.Should().Be(HttpMethod.Patch);
        _api.LastPath.Should().Be("repos/owner/repo/pulls/42");
        _api.LastBody.Should().Contain("Updated PR body from file");
    }

    [Fact]
    public async Task PrEdit_BodyFileNotFound_ReturnsError() {
        var result = await _handler.GhPrEditAsync("42", body_file: "/nonexistent/body.md", common: new GitHubCommonOptions { Repo = "owner/repo" });

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("body_file 不存在");
    }

    [Fact]
    public async Task PrEdit_Milestone_ResolvesNameToIdAndPatches() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """[{"title":"v1.0","number":5},{"title":"v2.0","number":8}]""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = "{}" });

        await _handler.GhPrEditAsync("42", milestone: "v1.0", common: new GitHubCommonOptions { Repo = "owner/repo" });

        _api.LastMethod.Should().Be(HttpMethod.Patch);
        _api.LastPath.Should().Be("repos/owner/repo/issues/42");
        _api.LastBody.Should().Contain("\"milestone\":5");
    }

    [Fact]
    public async Task PrEdit_MilestoneNotFound_ReturnsError() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """[{"title":"v1.0","number":5}]""" });

        var result = await _handler.GhPrEditAsync("42", milestone: "nonexistent", common: new GitHubCommonOptions { Repo = "owner/repo" });

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("未找到里程碑");
    }

    [Fact]
    public async Task PrEdit_RemoveMilestone_PatchesWithNullMilestone() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = "{}" });

        await _handler.GhPrEditAsync("42", remove_milestone: true, common: new GitHubCommonOptions { Repo = "owner/repo" });

        _api.LastMethod.Should().Be(HttpMethod.Patch);
        _api.LastPath.Should().Be("repos/owner/repo/issues/42");
        _api.LastBody.Should().Contain("\"milestone\":null");
    }

    [Fact]
    public async Task PrEdit_AddProject_CallsGraphQLAddProjectV2ItemById() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"node_id":"PR_kw123"}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"data":{"organization":{"projectsV2":{"nodes":[{"id":"PVT_1","title":"Roadmap"}]}}}}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"data":{"addProjectV2ItemById":{"item":{"id":"PVTI_1"}}}}""" });

        await _handler.GhPrEditAsync("42", add_project: "Roadmap", common: new GitHubCommonOptions { Repo = "owner/repo" });

        _api.LastMethod.Should().Be(HttpMethod.Post);
        _api.LastPath.Should().Be("graphql");
        _api.LastBody.Should().Contain("addProjectV2ItemById");
    }

    [Fact]
    public async Task PrEdit_RemoveProject_CallsGraphQLDeleteProjectV2Item() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"node_id":"PR_kw123"}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"data":{"organization":{"projectsV2":{"nodes":[{"id":"PVT_1","title":"Roadmap"}]}}}}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"data":{"node":{"items":{"nodes":[{"id":"PVTI_1","content":{"id":"PR_kw123"}}]}}}}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"data":{"deleteProjectV2Item":{"clientMutationId":null}}}""" });

        await _handler.GhPrEditAsync("42", remove_project: "Roadmap", common: new GitHubCommonOptions { Repo = "owner/repo" });

        _api.LastMethod.Should().Be(HttpMethod.Post);
        _api.LastPath.Should().Be("graphql");
        _api.LastBody.Should().Contain("deleteProjectV2Item");
    }

    [Fact]
    public async Task PrEdit_Attach_UploadsFileAndUpdatesBody() {
        var fs = new InMemoryFileSystem();
        await fs.WriteAllTextAsync("/tmp/screenshot.png", "fake image content");
        var handler = new GitHubToolHandlers(new FakeDownloader(), fs, _api, null, NullLogger<GitHubToolHandlers>.Instance);
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"node_id":"PR_kw123"}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"id":12345}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 201, Body = """{"url":"https://github.com/user-attachments/assets/abc123"}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = "{}" });

        var result = await handler.GhPrEditAsync("42", attach: "/tmp/screenshot.png", common: new GitHubCommonOptions { Repo = "owner/repo" });

        result.IsError.Should().BeFalse();
        _api.LastMethod.Should().Be(HttpMethod.Patch);
        _api.LastBody.Should().Contain("https://github.com/user-attachments/assets/abc123");
    }
}
