namespace Mcp.Tests;

public sealed partial class GitHubToolHandlersTests {
    [Fact]
    public async Task IssueCreate_WithBodyFile_ReadsFileContentAsBody() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 201, Body = """{"number":1}""" };
        var fs = new InMemoryFileSystem();
        await fs.WriteAllTextAsync("/tmp/body.md", "## Bug Report\nDescription here");
        var handler = new GitHubToolHandlers(new FakeDownloader(), fs, new PersistencePipeline(new InMemoryFileSystem()), _api, null, NullLogger<GitHubToolHandlers>.Instance);

        await handler.GhIssueCreateAsync("Test Issue", body_file: "/tmp/body.md", repo: "owner/repo");

        _api.LastBody.Should().Contain("## Bug Report");
        _api.LastBody.Should().Contain("Description here");
    }

    [Fact]
    public async Task IssueCreate_WithBodyFileNotFound_ReturnsError() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 201, Body = """{"number":1}""" };
        var handler = new GitHubToolHandlers(new FakeDownloader(), new InMemoryFileSystem(), new PersistencePipeline(new InMemoryFileSystem()), _api, null, NullLogger<GitHubToolHandlers>.Instance);

        var result = await handler.GhIssueCreateAsync("Test", body_file: "/nonexistent/body.md", repo: "owner/repo");

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("body_file 不存在");
    }
}
