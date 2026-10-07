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

    [Fact]
    public async Task ReleaseCreate_WithDiscussionCategory_SerializesDiscussionCategoryName() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 201, Body = """{"id":1,"tag_name":"v1"}""" };

        await _handler.GhReleaseCreateAsync("v1", discussion_category: "Releases", repo: "owner/repo");

        _api.LastBody.Should().Contain("\"discussion_category_name\":\"Releases\"");
    }

    [Fact]
    public async Task ReleaseCreate_WithNotesStartTag_SerializesPreviousTagName() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 201, Body = """{"id":1,"tag_name":"v2"}""" };

        await _handler.GhReleaseCreateAsync("v2", generate_notes: true, notes_start_tag: "v1", repo: "owner/repo");

        _api.LastBody.Should().Contain("\"previous_tag_name\":\"v1\"");
    }

    [Fact]
    public async Task ReleaseCreate_WithNotesFromTag_UsesTagAnnotationAsNotes() {
        var git = new FakeGitCommandRunner { NextSuccess = true, NextOutput = "v1          Release v1 notes from tag annotation" };
        var api = new FakeGitHubApiClient();
        api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 201, Body = """{"id":1,"tag_name":"v1"}""" };
        var handler = CreateHandlerWithGitAndApi(git, api);

        await handler.GhReleaseCreateAsync("v1", notes_from_tag: true, repo: "owner/repo");

        git.ExecutedCommands.Should().ContainMatch("*tag -n -l v1*");
        api.LastBody.Should().Contain("Release v1 notes from tag annotation");
    }

    [Fact]
    public async Task ReleaseCreate_WithVerifyTag_FailsWhenSignatureInvalid() {
        var git = new FakeGitCommandRunner { NextSuccess = false, NextOutput = "error: no signature found" };
        var api = new FakeGitHubApiClient();
        var handler = CreateHandlerWithGitAndApi(git, api);

        var result = await handler.GhReleaseCreateAsync("v1", verify_tag: true, repo: "owner/repo");

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("GPG 签名验证失败");
        git.ExecutedCommands.Should().ContainMatch("*tag -v v1*");
    }

    [Fact]
    public async Task ReleaseCreate_WithVerifyTag_SucceedsWhenSignatureValid() {
        var git = new FakeGitCommandRunner { NextSuccess = true, NextOutput = "good signature" };
        var api = new FakeGitHubApiClient();
        api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 201, Body = """{"id":1,"tag_name":"v1"}""" };
        var handler = CreateHandlerWithGitAndApi(git, api);

        var result = await handler.GhReleaseCreateAsync("v1", verify_tag: true, repo: "owner/repo");

        result.IsError.Should().BeFalse();
        git.ExecutedCommands.Should().ContainMatch("*tag -v v1*");
    }

    [Fact]
    public async Task ReleaseCreate_WithFailOnNoCommits_FailsWhenNoCommits() {
        var git = new FakeGitCommandRunner { NextSuccess = true, NextOutput = "" };
        var api = new FakeGitHubApiClient();
        api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"tag_name":"v0.9"}""" });
        var handler = CreateHandlerWithGitAndApi(git, api);

        var result = await handler.GhReleaseCreateAsync("v1", fail_on_no_commits: true, notes_start_tag: "v0.9", repo: "owner/repo");

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("没有新 commit");
        git.ExecutedCommands.Should().ContainMatch("*log v0.9..HEAD*");
    }
}
