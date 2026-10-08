namespace Mcp.Tests;

public sealed partial class GitHubToolHandlersTests {
    [Fact]
    public async Task IssueCreate_WithBodyFile_ReadsFileContentAsBody() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 201, Body = """{"number":1}""" };
        var fs = new InMemoryFileSystem();
        await fs.WriteAllTextAsync("/tmp/body.md", "## Bug Report\nDescription here");
        var handler = new GitHubToolHandlers(new FakeDownloader(), fs, _api, null, NullLogger<GitHubToolHandlers>.Instance);

        await handler.GhIssueCreateAsync("Test Issue", body_file: "/tmp/body.md", repo: "owner/repo");

        _api.LastBody.Should().Contain("## Bug Report");
        _api.LastBody.Should().Contain("Description here");
    }

    [Fact]
    public async Task IssueCreate_WithBodyFileNotFound_ReturnsError() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 201, Body = """{"number":1}""" };
        var handler = new GitHubToolHandlers(new FakeDownloader(), new InMemoryFileSystem(), _api, null, NullLogger<GitHubToolHandlers>.Instance);

        var result = await handler.GhIssueCreateAsync("Test", body_file: "/nonexistent/body.md", repo: "owner/repo");

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("body_file 不存在");
    }

    [Fact]
    public async Task IssueCreate_WithAttach_ReturnsNotSupportedError() {
        var result = await _handler.GhIssueCreateAsync("Test", attach: "file.txt", repo: "owner/repo");

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("--attach 暂未支持");
    }

    [Fact]
    public async Task IssueCreate_WithBlockedBy_ReturnsNotSupportedError() {
        var result = await _handler.GhIssueCreateAsync("Test", blocked_by: "1,2", repo: "owner/repo");

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("--blocked_by 暂未支持");
    }

    [Fact]
    public async Task IssueCreate_WithBlocking_ReturnsNotSupportedError() {
        var result = await _handler.GhIssueCreateAsync("Test", blocking: "3,4", repo: "owner/repo");

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("--blocking 暂未支持");
    }

    [Fact]
    public async Task IssueCreate_WithParent_ReturnsNotSupportedError() {
        var result = await _handler.GhIssueCreateAsync("Test", parent: 10, repo: "owner/repo");

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("--parent 暂未支持");
    }

    [Fact]
    public async Task IssueCreate_WithType_ReturnsNotSupportedError() {
        var result = await _handler.GhIssueCreateAsync("Test", type: "Bug", repo: "owner/repo");

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("--type 暂未支持");
    }
}
