namespace Mcp.Tests;

public sealed partial class GitHubToolHandlersTests {
    [Fact]
    public async Task IssueEdit_AddLabel_PostsToLabelsEndpoint() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = "[]" });

        await _handler.GhIssueEditAsync("42", add_label: "bug,enhancement", repo: "owner/repo");

        _api.LastMethod.Should().Be(HttpMethod.Post);
        _api.LastPath.Should().Be("repos/owner/repo/issues/42/labels");
        _api.LastBody.Should().Contain("\"labels\":[\"bug\",\"enhancement\"]");
    }

    [Fact]
    public async Task IssueEdit_RemoveLabel_DeletesFromLabelsEndpoint() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 204, Body = "" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 204, Body = "" });

        await _handler.GhIssueEditAsync("42", remove_label: "bug,enhancement", repo: "owner/repo");

        _api.LastMethod.Should().Be(HttpMethod.Delete);
        _api.LastPath.Should().Be("repos/owner/repo/issues/42/labels/enhancement");
    }

    [Fact]
    public async Task IssueEdit_BodyFile_ReadsFileContentAsBody() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = "{}" });
        var fs = new InMemoryFileSystem();
        await fs.WriteAllTextAsync("/tmp/issue_body.md", "Updated issue body from file");
        var handler = new GitHubToolHandlers(new FakeDownloader(), fs, _api, null, NullLogger<GitHubToolHandlers>.Instance);

        await handler.GhIssueEditAsync("42", body_file: "/tmp/issue_body.md", repo: "owner/repo");

        _api.LastMethod.Should().Be(HttpMethod.Patch);
        _api.LastPath.Should().Be("repos/owner/repo/issues/42");
        _api.LastBody.Should().Contain("Updated issue body from file");
    }

    [Fact]
    public async Task IssueEdit_BodyFileNotFound_ReturnsError() {
        var result = await _handler.GhIssueEditAsync("42", body_file: "/nonexistent/body.md", repo: "owner/repo");

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("body_file 不存在");
    }

    [Fact]
    public async Task IssueEdit_Milestone_ResolvesNameToIdAndPatches() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """[{"title":"v1.0","number":5},{"title":"v2.0","number":8}]""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = "{}" });

        await _handler.GhIssueEditAsync("42", milestone: "v2.0", repo: "owner/repo");

        _api.LastMethod.Should().Be(HttpMethod.Patch);
        _api.LastPath.Should().Be("repos/owner/repo/issues/42");
        _api.LastBody.Should().Contain("\"milestone\":8");
    }

    [Fact]
    public async Task IssueEdit_MilestoneNotFound_ReturnsError() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """[{"title":"v1.0","number":5}]""" });

        var result = await _handler.GhIssueEditAsync("42", milestone: "nonexistent", repo: "owner/repo");

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("未找到里程碑");
    }

    [Fact]
    public async Task IssueEdit_RemoveMilestone_PatchesWithNullMilestone() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = "{}" });

        await _handler.GhIssueEditAsync("42", remove_milestone: true, repo: "owner/repo");

        _api.LastMethod.Should().Be(HttpMethod.Patch);
        _api.LastPath.Should().Be("repos/owner/repo/issues/42");
        _api.LastBody.Should().Contain("\"milestone\":null");
    }

    [Fact]
    public async Task IssueEdit_AddProject_CallsGraphQLAddProjectV2ItemById() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"node_id":"I_kw123"}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"data":{"organization":{"projectsV2":{"nodes":[{"id":"PVT_1","title":"Roadmap"}]}}}}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"data":{"addProjectV2ItemById":{"item":{"id":"PVTI_1"}}}}""" });

        await _handler.GhIssueEditAsync("42", add_project: "Roadmap", repo: "owner/repo");

        _api.LastPath.Should().Be("graphql");
        _api.LastBody.Should().Contain("addProjectV2ItemById");
    }

    [Fact]
    public async Task IssueEdit_RemoveProject_CallsGraphQLDeleteProjectV2Item() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"node_id":"I_kw123"}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"data":{"organization":{"projectsV2":{"nodes":[{"id":"PVT_1","title":"Roadmap"}]}}}}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"data":{"node":{"items":{"nodes":[{"id":"PVTI_1","content":{"id":"I_kw123"}}]}}}}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"data":{"deleteProjectV2Item":{"clientMutationId":null}}}""" });

        await _handler.GhIssueEditAsync("42", remove_project: "Roadmap", repo: "owner/repo");

        _api.LastBody.Should().Contain("deleteProjectV2Item");
    }

    [Fact]
    public async Task IssueEdit_Attach_UploadsFileAndUpdatesBody() {
        var fs = new InMemoryFileSystem();
        await fs.WriteAllTextAsync("/tmp/screenshot.png", "fake image content");
        var handler = new GitHubToolHandlers(new FakeDownloader(), fs, _api, null, NullLogger<GitHubToolHandlers>.Instance);
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"node_id":"I_kw123"}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"id":12345}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 201, Body = """{"url":"https://github.com/user-attachments/assets/abc123"}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = "{}" });

        var result = await handler.GhIssueEditAsync("42", attach: "/tmp/screenshot.png", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        _api.LastBody.Should().Contain("https://github.com/user-attachments/assets/abc123");
    }

    [Fact]
    public async Task IssueEdit_AddSubIssue_CallsGraphQLAddSubIssue() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"node_id":"I_parent"}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"node_id":"I_child"}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"data":{"addSubIssue":{"issue":{"id":"I_parent"}}}}""" });

        await _handler.GhIssueEditAsync("42", add_sub_issue: "100", repo: "owner/repo");

        _api.LastBody.Should().Contain("addSubIssue");
    }

    [Fact]
    public async Task IssueEdit_RemoveSubIssue_CallsGraphQLRemoveSubIssue() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"node_id":"I_parent"}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"node_id":"I_child"}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"data":{"removeSubIssue":{"issue":{"id":"I_parent"}}}}""" });

        await _handler.GhIssueEditAsync("42", remove_sub_issue: "100", repo: "owner/repo");

        _api.LastBody.Should().Contain("removeSubIssue");
    }

    [Fact]
    public async Task IssueEdit_Parent_CallsGraphQLAddSubIssue() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"node_id":"I_child"}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"node_id":"I_parent"}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"data":{"addSubIssue":{"issue":{"id":"I_parent"}}}}""" });

        await _handler.GhIssueEditAsync("42", parent: "100", repo: "owner/repo");

        _api.LastBody.Should().Contain("addSubIssue");
    }

    [Fact]
    public async Task IssueEdit_RemoveParent_CallsGraphQLRemoveSubIssue() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"node_id":"I_child"}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"data":{"repository":{"issue":{"parent":{"id":"I_parent"}}}}}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"data":{"removeSubIssue":{"issue":{"id":"I_parent"}}}}""" });

        await _handler.GhIssueEditAsync("42", remove_parent: true, repo: "owner/repo");

        _api.LastBody.Should().Contain("removeSubIssue");
    }

    [Fact]
    public async Task IssueEdit_AddBlockedBy_CallsGraphQLAddBlockedBy() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"node_id":"I_42"}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"node_id":"I_200"}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"data":{"addBlockedBy":{"issue":{"id":"I_42"}}}}""" });

        await _handler.GhIssueEditAsync("42", add_blocked_by: "200", repo: "owner/repo");

        _api.LastBody.Should().Contain("addBlockedBy");
    }

    [Fact]
    public async Task IssueEdit_RemoveBlockedBy_CallsGraphQLRemoveBlockedBy() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"node_id":"I_42"}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"node_id":"I_200"}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"data":{"removeBlockedBy":{"issue":{"id":"I_42"}}}}""" });

        await _handler.GhIssueEditAsync("42", remove_blocked_by: "200", repo: "owner/repo");

        _api.LastBody.Should().Contain("removeBlockedBy");
    }

    [Fact]
    public async Task IssueEdit_AddBlocking_CallsGraphQLAddBlockedByReversed() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"node_id":"I_42"}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"node_id":"I_300"}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"data":{"addBlockedBy":{"issue":{"id":"I_300"}}}}""" });

        await _handler.GhIssueEditAsync("42", add_blocking: "300", repo: "owner/repo");

        _api.LastBody.Should().Contain("addBlockedBy");
    }

    [Fact]
    public async Task IssueEdit_RemoveBlocking_CallsGraphQLRemoveBlockedByReversed() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"node_id":"I_42"}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"node_id":"I_300"}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"data":{"removeBlockedBy":{"issue":{"id":"I_300"}}}}""" });

        await _handler.GhIssueEditAsync("42", remove_blocking: "300", repo: "owner/repo");

        _api.LastBody.Should().Contain("removeBlockedBy");
    }

    [Fact]
    public async Task IssueEdit_Type_CallsGraphQLUpdateIssueIssueType() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"node_id":"I_42"}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"data":{"repository":{"issueTypes":{"nodes":[{"id":"IT_1","name":"Bug"}]}}}}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"data":{"updateIssueIssueType":{"issue":{"id":"I_42"}}}}""" });

        await _handler.GhIssueEditAsync("42", type: "Bug", repo: "owner/repo");

        _api.LastBody.Should().Contain("updateIssueIssueType");
    }

    [Fact]
    public async Task IssueEdit_RemoveType_CallsGraphQLUpdateIssueIssueTypeWithNull() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"node_id":"I_42"}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"data":{"updateIssueIssueType":{"issue":{"id":"I_42"}}}}""" });

        await _handler.GhIssueEditAsync("42", remove_type: true, repo: "owner/repo");

        _api.LastBody.Should().Contain("updateIssueIssueType");
        _api.LastBody.Should().Contain("issueTypeId:null");
    }
}
