namespace Mcp.Tests;

public sealed partial class GitHubToolHandlersTests {
    [Fact]
    public async Task PrCreate_AutoMergeTrue_CallsEnableAutoMergeAfterCreate() {
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true,
            StatusCode = 201,
            Body = """{"number":42,"node_id":"PR_kwDOTVZE0c8AAAABCsFVdw","title":"feat","state":"open"}""",
        });
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true,
            StatusCode = 200,
            Body = """{"data":{"enablePullRequestAutoMerge":{"pullRequest":{"number":42}}}}""",
        });

        var result = await _handler.GhPrCreateAsync("feat", "feature-branch", @base: "main", auto_merge: true, merge_method: "squash", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("auto-merge");
        _api.LastMethod.Should().Be(HttpMethod.Post);
        _api.LastPath.Should().Be("graphql");
        _api.LastBody.Should().Contain("enablePullRequestAutoMerge");
        _api.LastBody.Should().Contain("SQUASH");
        _api.LastBody.Should().Contain("PR_kwDOTVZE0c8AAAABCsFVdw");
    }

    [Fact]
    public async Task PrCreate_AutoMergeFalse_DoesNotCallGraphQL() {
        _api.NextResponse = new GitHubApiResponse {
            Success = true,
            StatusCode = 201,
            Body = """{"number":43,"node_id":"PR_xxx","title":"feat","state":"open"}""",
        };

        var result = await _handler.GhPrCreateAsync("feat", "feature-branch", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        _api.LastPath.Should().Be("repos/owner/repo/pulls");
    }

    [Fact]
    public async Task PrCreate_RequestBody_OmitsNullMaintainerCanModify() {
        _api.NextResponse = new GitHubApiResponse {
            Success = true,
            StatusCode = 201,
            Body = """{"number":44,"title":"t","state":"open"}""",
        };

        await _handler.GhPrCreateAsync("t", "feat", @base: "main", repo: "owner/repo");

        _api.LastBody.Should().NotContain("maintainer_can_modify");
    }

    [Fact]
    public async Task PrCreate_DryRun_DoesNotCallApi_ReturnsPreview() {
        var result = await _handler.GhPrCreateAsync("feat: preview", "feature-branch", @base: "main", body: "preview body", dry_run: true, repo: "owner/repo");

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("[dry-run]");
        result.GetFirstText().Should().Contain("feat: preview");
        result.GetFirstText().Should().Contain("feature-branch");
        _api.LastPath.Should().BeNull();
    }

    [Fact]
    public async Task PrCreate_NoMaintainerEdit_IncludesMaintainerCanModifyFalse() {
        _api.NextResponse = new GitHubApiResponse {
            Success = true,
            StatusCode = 201,
            Body = """{"number":45,"title":"t","state":"open"}""",
        };

        await _handler.GhPrCreateAsync("t", "feat", @base: "main", no_maintainer_edit: true, repo: "owner/repo");

        _api.LastBody.Should().Contain("\"maintainer_can_modify\":false");
    }

    [Fact]
    public async Task PrCreate_Recover_ReturnsNotSupportedError() {
        var result = await _handler.GhPrCreateAsync("t", "feat", recover: true, repo: "owner/repo");

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("--recover 暂未支持");
    }

    [Fact]
    public async Task PrCreate_Attach_ReturnsNotSupportedError() {
        var result = await _handler.GhPrCreateAsync("t", "feat", attach: "file.txt", repo: "owner/repo");

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("--attach 暂未支持");
    }

    [Fact]
    public async Task PrCreate_FillFirst_CallsGitRevListAndLog() {
        var git = new FakeGitCommandRunner { NextSuccess = true, NextOutput = "abc123\n" };
        var handler = CreateHandlerWithGitAndApi(git, _api);
        _api.NextResponse = new GitHubApiResponse {
            Success = true,
            StatusCode = 201,
            Body = """{"number":46,"title":"abc123","state":"open","html_url":"https://github.com/o/r/pull/46"}""",
        };

        var result = await handler.GhPrCreateAsync("", "feature-branch", @base: "main", fill_first: true, repo: "owner/repo");

        result.IsError.Should().BeFalse();
        git.ExecutedCommands.Should().ContainMatch("*rev-list --reverse main..feature-branch*");
        git.ExecutedCommands.Should().ContainMatch("*log -1 --format=*");
    }

    [Fact]
    public async Task PrCreate_FillVerbose_AppendsFillInfo() {
        var git = new FakeGitCommandRunner { NextSuccess = true, NextOutput = "feat: from commit\n\ndetails" };
        var handler = CreateHandlerWithGitAndApi(git, _api);
        _api.NextResponse = new GitHubApiResponse {
            Success = true,
            StatusCode = 201,
            Body = """{"number":47,"title":"feat: from commit","state":"open","html_url":"https://github.com/o/r/pull/47"}""",
        };

        var result = await handler.GhPrCreateAsync("", "feat", @base: "main", fill: true, fill_verbose: true, repo: "owner/repo");

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("[fill]");
    }

    [Fact]
    public async Task PrCreate_HeadNull_AutoInfersCurrentBranch() {
        var git = new FakeGitCommandRunner { NextSuccess = true, NextOutput = "w2\n" };
        var handler = CreateHandlerWithGitAndApi(git, _api);
        _api.NextResponse = new GitHubApiResponse {
            Success = true, StatusCode = 201,
            Body = """{"number":50,"title":"feat","state":"open","html_url":"https://github.com/o/r/pull/50"}""",
        };

        var result = await handler.GhPrCreateAsync("feat", head: null, @base: "main", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        git.ExecutedCommands.Should().ContainMatch("*rev-parse --abbrev-ref HEAD*");
        _api.LastBody.Should().Contain("\"head\":\"w2\"");
    }

    [Fact]
    public async Task PrCreate_HeadNull_GitNotConfigured_ReturnsErrorWithGuidance() {
        var result = await _handler.GhPrCreateAsync("feat", head: null, @base: "main", repo: "owner/repo");

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("--head");
        result.GetFirstText().Should().Contain("分支名");
    }

    [Fact]
    public async Task PrCreate_HeadNull_GitFails_ReturnsErrorWithGuidance() {
        var git = new FakeGitCommandRunner { NextSuccess = false, NextOutput = "" };
        var handler = CreateHandlerWithGitAndApi(git, _api);

        var result = await handler.GhPrCreateAsync("feat", head: null, @base: "main", repo: "owner/repo");

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("--head");
    }
}
