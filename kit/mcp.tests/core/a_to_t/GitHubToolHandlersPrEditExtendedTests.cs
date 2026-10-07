namespace Mcp.Tests;

public sealed partial class GitHubToolHandlersTests {
    [Fact]
    public async Task PrEdit_AddLabel_PostsToLabelsEndpoint() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = "[]" });

        await _handler.GhPrEditAsync("42", add_label: "bug,enhancement", repo: "owner/repo");

        _api.LastMethod.Should().Be(HttpMethod.Post);
        _api.LastPath.Should().Be("repos/owner/repo/issues/42/labels");
        _api.LastBody.Should().Contain("\"labels\":[\"bug\",\"enhancement\"]");
    }

    [Fact]
    public async Task PrEdit_RemoveLabel_DeletesFromLabelsEndpoint() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = "[]" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 204, Body = "" });

        await _handler.GhPrEditAsync("42", remove_label: "bug,enhancement", repo: "owner/repo");

        _api.LastMethod.Should().Be(HttpMethod.Delete);
        _api.LastPath.Should().Be("repos/owner/repo/issues/42/labels/enhancement");
    }

    [Fact]
    public async Task PrEdit_BodyFile_ReadsFileContentAsBody() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = "{}" });
        var fs = new InMemoryFileSystem();
        await fs.WriteAllTextAsync("/tmp/pr_body.md", "Updated PR body from file");
        var handler = new GitHubToolHandlers(new FakeDownloader(), fs, new PersistencePipeline(new InMemoryFileSystem()), _api, null, NullLogger<GitHubToolHandlers>.Instance);

        await handler.GhPrEditAsync("42", body_file: "/tmp/pr_body.md", repo: "owner/repo");

        _api.LastMethod.Should().Be(HttpMethod.Patch);
        _api.LastPath.Should().Be("repos/owner/repo/pulls/42");
        _api.LastBody.Should().Contain("Updated PR body from file");
    }

    [Fact]
    public async Task PrEdit_BodyFileNotFound_ReturnsError() {
        var result = await _handler.GhPrEditAsync("42", body_file: "/nonexistent/body.md", repo: "owner/repo");

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("body_file 不存在");
    }

    [Fact]
    public async Task PrEdit_Milestone_ResolvesNameToIdAndPatches() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """[{"title":"v1.0","number":5},{"title":"v2.0","number":8}]""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = "{}" });

        await _handler.GhPrEditAsync("42", milestone: "v1.0", repo: "owner/repo");

        _api.LastMethod.Should().Be(HttpMethod.Patch);
        _api.LastPath.Should().Be("repos/owner/repo/issues/42");
        _api.LastBody.Should().Contain("\"milestone\":5");
    }

    [Fact]
    public async Task PrEdit_MilestoneNotFound_ReturnsError() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """[{"title":"v1.0","number":5}]""" });

        var result = await _handler.GhPrEditAsync("42", milestone: "nonexistent", repo: "owner/repo");

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("未找到里程碑");
    }

    [Fact]
    public async Task PrEdit_RemoveMilestone_PatchesWithNullMilestone() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = "{}" });

        await _handler.GhPrEditAsync("42", remove_milestone: true, repo: "owner/repo");

        _api.LastMethod.Should().Be(HttpMethod.Patch);
        _api.LastPath.Should().Be("repos/owner/repo/issues/42");
        _api.LastBody.Should().Contain("\"milestone\":null");
    }

    [Fact]
    public async Task PrEdit_AddProject_ReturnsNotSupportedError() {
        var result = await _handler.GhPrEditAsync("42", add_project: "Roadmap", repo: "owner/repo");

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("add_project");
        result.GetFirstText().Should().Contain("暂未支持");
    }

    [Fact]
    public async Task PrEdit_RemoveProject_ReturnsNotSupportedError() {
        var result = await _handler.GhPrEditAsync("42", remove_project: "Roadmap", repo: "owner/repo");

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("remove_project");
        result.GetFirstText().Should().Contain("暂未支持");
    }

    [Fact]
    public async Task PrEdit_Attach_ReturnsNotSupportedError() {
        var result = await _handler.GhPrEditAsync("42", attach: "screenshot.png", repo: "owner/repo");

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("attach");
        result.GetFirstText().Should().Contain("暂未支持");
    }
}
