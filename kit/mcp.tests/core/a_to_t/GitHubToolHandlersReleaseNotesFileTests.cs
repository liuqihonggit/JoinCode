namespace Mcp.Tests;

public sealed partial class GitHubToolHandlersTests {
    [Fact]
    public async Task ReleaseCreate_WithNotesFile_ReadsFileContentAsNotes() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 201, Body = """{"id":1,"tag_name":"v1"}""" };
        var fs = new InMemoryFileSystem();
        await fs.WriteAllTextAsync("/tmp/notes.md", "## Changes\n- Feature A\n- Bug fix B");
        var handler = new GitHubToolHandlers(new FakeDownloader(), fs, new PersistencePipeline(new InMemoryFileSystem()), _api, null, NullLogger<GitHubToolHandlers>.Instance);

        await handler.GhReleaseCreateAsync("v1", notes_file: "/tmp/notes.md", repo: "owner/repo");

        _api.LastBody.Should().Contain("## Changes");
        _api.LastBody.Should().Contain("Feature A");
    }

    [Fact]
    public async Task ReleaseCreate_WithNotesFileNotFound_ReturnsError() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 201, Body = """{"id":1}""" };
        var handler = new GitHubToolHandlers(new FakeDownloader(), new InMemoryFileSystem(), new PersistencePipeline(new InMemoryFileSystem()), _api, null, NullLogger<GitHubToolHandlers>.Instance);

        var result = await handler.GhReleaseCreateAsync("v1", notes_file: "/nonexistent/notes.md", repo: "owner/repo");

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("notes_file 不存在");
    }

    [Fact]
    public async Task ReleaseCreate_WithMakeLatest_SerializesMakeLatest() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 201, Body = """{"id":1,"tag_name":"v1"}""" };

        await _handler.GhReleaseCreateAsync("v1", make_latest: "true", repo: "owner/repo");

        _api.LastBody.Should().Contain("\"make_latest\":\"true\"");
    }
}
