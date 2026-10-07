namespace Mcp.Tests;

public sealed partial class GitHubToolHandlersTests {
    private readonly FakeGitHubApiClient _api = new();
    private readonly GitHubToolHandlers _handler;

    public GitHubToolHandlersTests() {
        MemoryCache.Default.Trim(100);
        _handler = new GitHubToolHandlers(
            new FakeDownloader(),
            new InMemoryFileSystem(),
            new PersistencePipeline(new InMemoryFileSystem()),
            _api,
            null,
            NullLogger<GitHubToolHandlers>.Instance);
    }

    private static GitHubToolHandlers CreateHandlerWithGit(IGitCommandRunner git)
        => new(new FakeDownloader(), new InMemoryFileSystem(), new PersistencePipeline(new InMemoryFileSystem()), new FakeGitHubApiClient(), git, NullLogger<GitHubToolHandlers>.Instance);

    private static GitHubToolHandlers CreateHandlerWithGitAndApi(IGitCommandRunner git, FakeGitHubApiClient api)
        => new(new FakeDownloader(), new InMemoryFileSystem(), new PersistencePipeline(new InMemoryFileSystem()), api, git, NullLogger<GitHubToolHandlers>.Instance);

    [Fact]
    public async Task PrView_Success_ReturnsOutput() {
        _api.NextResponse = new GitHubApiResponse {
            Success = true,
            StatusCode = 200,
            Body = """{"number":123,"title":"feat: add","state":"open","url":"https://github.com/o/r/pull/123"}""",
        };

        var result = await _handler.GhPrViewAsync("123", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("123");
        _api.LastMethod.Should().Be(HttpMethod.Get);
        _api.LastPath.Should().Be("repos/owner/repo/pulls/123");
    }

    [Fact]
    public async Task PrView_Failure_ReturnsError() {
        _api.NextResponse = new GitHubApiResponse {
            Success = false,
            StatusCode = 404,
            Error = "could not find pr",
        };

        var result = await _handler.GhPrViewAsync("999", repo: "owner/repo");

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("could not find pr");
    }

    [Fact]
    public async Task PrCreate_Success_ReturnsCreatedPr() {
        _api.NextResponse = new GitHubApiResponse {
            Success = true,
            StatusCode = 201,
            Body = """{"number":42,"title":"feat: new","state":"open","html_url":"https://github.com/o/r/pull/42"}""",
        };

        var result = await _handler.GhPrCreateAsync("feat: new", "feature-branch", @base: "main", body: "test body", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("42");
        _api.LastMethod.Should().Be(HttpMethod.Post);
        _api.LastPath.Should().Be("repos/owner/repo/pulls");
        _api.LastBody.Should().Contain("\"title\":\"feat: new\"");
        _api.LastBody.Should().Contain("\"head\":\"feature-branch\"");
        _api.LastBody.Should().Contain("\"base\":\"main\"");
        _api.LastBody.Should().Contain("\"body\":\"test body\"");
    }

    [Fact]
    public async Task PrCreate_DraftTrue_IncludesDraftField() {
        _api.NextResponse = new GitHubApiResponse {
            Success = true,
            StatusCode = 201,
            Body = """{"number":43,"title":"draft","state":"open","draft":true}""",
        };

        var result = await _handler.GhPrCreateAsync("draft", "branch", draft: true, repo: "owner/repo");

        result.IsError.Should().BeFalse();
        _api.LastBody.Should().Contain("\"draft\":true");
    }

    [Fact]
    public async Task PrCreate_WithBaseAndBody_ProducesValidJsonBody() {
        _api.NextResponse = new GitHubApiResponse {
            Success = true,
            StatusCode = 201,
            Body = """{"number":44,"title":"t","state":"open"}""",
        };

        await _handler.GhPrCreateAsync("t", "feat", @base: "main", body: "b", repo: "owner/repo");

        _api.LastBody.Should().NotBeNullOrEmpty();
        using var doc = System.Text.Json.JsonDocument.Parse(_api.LastBody!);
        doc.RootElement.GetProperty("title").GetString().Should().Be("t");
        doc.RootElement.GetProperty("head").GetString().Should().Be("feat");
        doc.RootElement.GetProperty("base").GetString().Should().Be("main");
        doc.RootElement.GetProperty("body").GetString().Should().Be("b");
    }

    [Fact]
    public async Task PrCreate_WithBaseOnly_ProducesValidJsonBody() {
        _api.NextResponse = new GitHubApiResponse {
            Success = true,
            StatusCode = 201,
            Body = """{"number":45,"title":"t","state":"open"}""",
        };

        await _handler.GhPrCreateAsync("t", "feat", @base: "main", repo: "owner/repo");

        _api.LastBody.Should().NotBeNullOrEmpty();
        using var doc = System.Text.Json.JsonDocument.Parse(_api.LastBody!);
        doc.RootElement.GetProperty("title").GetString().Should().Be("t");
        doc.RootElement.GetProperty("head").GetString().Should().Be("feat");
        doc.RootElement.GetProperty("base").GetString().Should().Be("main");
    }

    [Fact]
    public async Task PrMerge_AutoMergeTrue_CallsGraphQLEnableAutomerge() {
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true,
            StatusCode = 200,
            Body = """{"number":206,"node_id":"PR_kwDOTVZE0c8AAAABCsFVdw"}""",
        });
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true,
            StatusCode = 200,
            Body = """{"data":{"enablePullRequestAutoMerge":{"pullRequest":{"number":206}}}}""",
        });

        var result = await _handler.GhPrMergeAsync("206", merge_method: "squash", auto_merge: true, repo: "owner/repo");

        result.IsError.Should().BeFalse();
        _api.LastMethod.Should().Be(HttpMethod.Post);
        _api.LastPath.Should().Be("graphql");
        _api.LastBody.Should().Contain("enablePullRequestAutoMerge");
        _api.LastBody.Should().Contain("SQUASH");
        _api.LastBody.Should().Contain("PR_kwDOTVZE0c8AAAABCsFVdw");
    }

    [Fact]
    public async Task PrMerge_AutoMergeFalse_CallsMergeEndpoint() {
        _api.NextResponse = new GitHubApiResponse {
            Success = true,
            StatusCode = 200,
            Body = "{}",
        };

        var result = await _handler.GhPrMergeAsync("42", merge_method: "squash", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        _api.LastMethod.Should().Be(HttpMethod.Put);
        _api.LastPath.Should().Be("repos/owner/repo/pulls/42/merge");
    }

    [Fact]
    public async Task PrReopen_WithComment_PostsCommentBeforeReopen() {
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 201,
            Body = """{"id":1}""",
        });
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"number":42,"state":"open"}""",
        });

        var result = await _handler.GhPrReopenAsync("42", comment: "重开此 PR", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        _api.LastMethod.Should().Be(HttpMethod.Patch);
        _api.LastPath.Should().Be("repos/owner/repo/pulls/42");
    }

    [Fact]
    public async Task PrReopen_WithoutComment_OnlyReopens() {
        _api.NextResponse = new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"number":42,"state":"open"}""",
        };

        var result = await _handler.GhPrReopenAsync("42", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        _api.LastMethod.Should().Be(HttpMethod.Patch);
        _api.LastPath.Should().Be("repos/owner/repo/pulls/42");
    }

    [Fact]
    public async Task PrClose_WithDeleteBranch_DeletesBranchAfterClose() {
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"number":42,"state":"closed"}""",
        });
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"head":{"ref":"feature-branch"}}""",
        });
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 204,
            Body = "{}",
        });

        var result = await _handler.GhPrCloseAsync("42", delete_branch: true, repo: "owner/repo");

        result.IsError.Should().BeFalse();
        _api.LastMethod.Should().Be(HttpMethod.Delete);
        _api.LastPath.Should().Be("repos/owner/repo/git/refs/heads/feature-branch");
    }

    [Fact]
    public async Task PrClose_WithoutDeleteBranch_OnlyCloses() {
        _api.NextResponse = new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"number":42,"state":"closed"}""",
        };

        var result = await _handler.GhPrCloseAsync("42", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        _api.LastMethod.Should().Be(HttpMethod.Patch);
        _api.LastPath.Should().Be("repos/owner/repo/pulls/42");
    }

    [Fact]
    public async Task PrList_WithBase_PassesBaseQuery() {
        _api.NextResponse = new GitHubApiResponse {
            Success = true, StatusCode = 200, Body = "[]",
        };
        await _handler.GhPrListAsync(@base: "develop", repo: "owner/repo");
        _api.LastPath.Should().Be("repos/owner/repo/pulls");
        _api.LastQuery.Should().ContainKey("base").WhoseValue.Should().Be("develop");
    }

    [Fact]
    public async Task PrList_WithHead_PassesHeadQuery() {
        _api.NextResponse = new GitHubApiResponse {
            Success = true, StatusCode = 200, Body = "[]",
        };
        await _handler.GhPrListAsync(head: "feature", repo: "owner/repo");
        _api.LastQuery.Should().ContainKey("head").WhoseValue.Should().Be("feature");
    }

    [Fact]
    public async Task PrList_WithLabel_UsesSearchApi() {
        _api.NextResponse = new GitHubApiResponse {
            Success = true, StatusCode = 200, Body = """{"total_count":0,"items":[]}""",
        };
        await _handler.GhPrListAsync(label: "bug", repo: "owner/repo");
        _api.LastPath.Should().Be("search/issues");
        _api.LastQuery.Should().ContainKey("q").WhoseValue.Should().Contain("is:pr").And.Contain("label:bug");
    }

    [Fact]
    public async Task PrList_WithDraft_UsesSearchApi() {
        _api.NextResponse = new GitHubApiResponse {
            Success = true, StatusCode = 200, Body = """{"total_count":0,"items":[]}""",
        };
        await _handler.GhPrListAsync(draft: true, repo: "owner/repo");
        _api.LastPath.Should().Be("search/issues");
        _api.LastQuery.Should().ContainKey("q").WhoseValue.Should().Contain("draft:true");
    }

    [Fact]
    public async Task PrList_WithAssignee_UsesSearchApi() {
        _api.NextResponse = new GitHubApiResponse {
            Success = true, StatusCode = 200, Body = """{"total_count":0,"items":[]}""",
        };
        await _handler.GhPrListAsync(assignee: "alice", repo: "owner/repo");
        _api.LastPath.Should().Be("search/issues");
        _api.LastQuery.Should().ContainKey("q").WhoseValue.Should().Contain("assignee:alice");
    }

    [Fact]
    public async Task PrList_WithSearch_UsesSearchApi() {
        _api.NextResponse = new GitHubApiResponse {
            Success = true, StatusCode = 200, Body = """{"total_count":0,"items":[]}""",
        };
        await _handler.GhPrListAsync(search: "review:required", repo: "owner/repo");
        _api.LastPath.Should().Be("search/issues");
        _api.LastQuery.Should().ContainKey("q").WhoseValue.Should().Contain("review:required");
    }

    [Fact]
    public async Task PrDiff_WithNameOnly_ReturnsOnlyFileNames() {
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"diff_url":"https://example.com/diff"}""",
        });
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = "diff --git a/file1.txt b/file1.txt\nindex 123..456\n--- a/file1.txt\n+++ b/file1.txt\n@@ -1 +1 @@\n-old\n+new\ndiff --git a/file2.cs b/file2.cs\nindex 123..456\n--- a/file2.cs\n+++ b/file2.cs\n@@ -1 +1 @@\n-old\n+new\n",
        });

        var result = await _handler.GhPrDiffAsync("42", name_only: true, repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().Contain("file1.txt");
        text.Should().Contain("file2.cs");
        text.Should().NotContain("diff --git");
        text.Should().NotContain("--- a/");
    }

    [Fact]
    public async Task PrDiff_WithExclude_FiltersMatchingFiles() {
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"diff_url":"https://example.com/diff"}""",
        });
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = "diff --git a/file1.txt b/file1.txt\nindex 123..456\n--- a/file1.txt\n+++ b/file1.txt\n@@ -1 +1 @@\n-old\n+new\ndiff --git a/file2.cs b/file2.cs\nindex 123..456\n--- a/file2.cs\n+++ b/file2.cs\n@@ -1 +1 @@\n-old\n+new\n",
        });

        var result = await _handler.GhPrDiffAsync("42", exclude: "*.txt", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().NotContain("file1.txt");
        text.Should().Contain("file2.cs");
    }

    [Fact]
    public async Task PrCheckout_WithCustomBranch_UsesCustomBranchName() {
        var git = new FakeGitCommandRunner();
        var handler = CreateHandlerWithGit(git);

        await handler.GhPrCheckoutAsync("42", branch: "custom-branch");

        git.ExecutedCommands.Should().Contain(c => c.Contains("pull/42/head:custom-branch"));
        git.ExecutedCommands.Should().Contain("checkout custom-branch");
    }

    [Fact]
    public async Task PrCheckout_WithForce_AddsForceToFetch() {
        var git = new FakeGitCommandRunner();
        var handler = CreateHandlerWithGit(git);

        await handler.GhPrCheckoutAsync("42", force: true);

        git.ExecutedCommands[0].Should().Contain("--force");
    }

    [Fact]
    public async Task PrCheckout_WithDetach_UsesDetachedHead() {
        var git = new FakeGitCommandRunner();
        var handler = CreateHandlerWithGit(git);

        await handler.GhPrCheckoutAsync("42", detach: true);

        git.ExecutedCommands.Should().Contain(c => c.Contains("--detach"));
    }

    [Fact]
    public async Task PrMerge_WithSubjectAndBody_PassesCommitTitleAndMessage() {
        _api.NextResponse = new GitHubApiResponse {
            Success = true, StatusCode = 200, Body = "{}",
        };

        await _handler.GhPrMergeAsync("42", merge_method: "squash", subject: "自定义标题", body: "自定义正文", repo: "owner/repo");

        _api.LastMethod.Should().Be(HttpMethod.Put);
        _api.LastBody.Should().Contain("\"commit_title\":\"自定义标题\"");
        _api.LastBody.Should().Contain("\"commit_message\":\"自定义正文\"");
    }

    [Fact]
    public async Task PrMerge_WithDisableAuto_CallsGraphQLDisableAutoMerge() {
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200, Body = """{"number":42,"node_id":"PR_123"}""",
        });
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200, Body = """{"data":{"disablePullRequestAutoMerge":{"pullRequest":{"number":42}}}}""",
        });

        var result = await _handler.GhPrMergeAsync("42", disable_auto: true, repo: "owner/repo");

        result.IsError.Should().BeFalse();
        _api.LastPath.Should().Be("graphql");
        _api.LastBody.Should().Contain("disablePullRequestAutoMerge");
    }

    [Fact]
    public async Task PrChecks_WithRequired_FiltersByRequiredChecks() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"head":{"sha":"abc123","ref":"main"}}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"check_runs":[{"name":"build","conclusion":"success"},{"name":"lint","conclusion":"success"},{"name":"optional-check","conclusion":"success"}]}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"contexts":["build","lint"]}""" });

        var result = await _handler.GhPrChecksAsync("42", required: true, repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().Contain("build");
        text.Should().Contain("lint");
        text.Should().NotContain("optional-check");
    }

    [Fact]
    public async Task PrChecks_WithFailFast_MarksFailure() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"head":{"sha":"abc123"}}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"check_runs":[{"name":"build","conclusion":"success"},{"name":"test","conclusion":"failure"}]}""" });

        var result = await _handler.GhPrChecksAsync("42", fail_fast: true, repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().Contain("fail-fast");
    }

    [Fact]
    public async Task PrCreate_WithAssignee_AddsAssigneeAfterCreate() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 201, Body = """{"number":42}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = "{}" });

        var result = await _handler.GhPrCreateAsync("title", "head", assignee: "alice", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        _api.LastPath.Should().Be("repos/owner/repo/issues/42/assignees");
        _api.LastBody.Should().Contain("alice");
    }

    [Fact]
    public async Task PrCreate_WithLabel_AddsLabelAfterCreate() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 201, Body = """{"number":42}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = "{}" });

        var result = await _handler.GhPrCreateAsync("title", "head", label: "bug", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        _api.LastPath.Should().Be("repos/owner/repo/issues/42/labels");
        _api.LastBody.Should().Contain("bug");
    }

    [Fact]
    public async Task PrCreate_WithReviewer_AddsReviewerAfterCreate() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 201, Body = """{"number":42}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = "{}" });

        var result = await _handler.GhPrCreateAsync("title", "head", reviewer: "bob", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        _api.LastPath.Should().Be("repos/owner/repo/pulls/42/requested_reviewers");
        _api.LastBody.Should().Contain("bob");
    }

    [Fact]
    public async Task PrView_WithComments_IncludesComments() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"number":42,"title":"feat","state":"open","url":"https://github.com/o/r/pull/42"}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """[{"body":"评论1","user":{"login":"alice"}}]""" });

        var result = await _handler.GhPrViewAsync("42", comments: true, repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().Contain("评论1");
    }

    [Fact]
    public async Task PrView_WithWeb_ReturnsUrl() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"number":42,"title":"feat","state":"open","html_url":"https://github.com/o/r/pull/42"}""" };

        var result = await _handler.GhPrViewAsync("42", web: true, repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().Contain("https://github.com/o/r/pull/42");
    }

    [Fact]
    public async Task PrCreate_Failure_ReturnsError() {
        _api.NextResponse = new GitHubApiResponse {
            Success = false,
            StatusCode = 422,
            Error = "Validation failed",
        };

        var result = await _handler.GhPrCreateAsync("title", "branch", repo: "owner/repo");

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("Validation failed");
    }

    [Fact]
    public async Task PrChecks_Skipping_NotCountedAsFail() {
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true,
            StatusCode = 200,
            Body = """{"head":{"sha":"abc123"}}""",
        });
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true,
            StatusCode = 200,
            Body = """{"check_runs":[{"name":"build","conclusion":"success"},{"name":"lint","conclusion":"skipped"},{"name":"test","conclusion":"failure"}]}""",
        });

        var result = await _handler.GhPrChecksAsync("1", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().Contain("1 通过");
        text.Should().Contain("1 失败");
        text.Should().Contain("1 跳过(依赖链跳过,非失败)");
    }

    /// <summary>check-runs 请求应带 per_page=100 并启用分页,避免大量 check 时默认 30 条截断(缺陷6)</summary>
    [Fact]
    public async Task PrChecks_CheckRunsRequest_IncludesPerPage100_AndPaginate() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"head":{"sha":"abc123"}}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"check_runs":[]}""" });

        await _handler.GhPrChecksAsync("42", repo: "owner/repo");

        _api.LastQuery.Should().ContainKey("per_page", "check-runs 应带 per_page=100 避免默认 30 条截断");
        _api.LastQuery["per_page"].Should().Be("100");
    }

    [Fact]
    public async Task RunList_WithEvent_PassesEventQuery() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"workflow_runs":[]}""" };

        await _handler.GhRunListAsync(event_type: "push", repo: "owner/repo");

        _api.LastQuery.Should().ContainKey("event").WhoseValue.Should().Be("push");
    }

    [Fact]
    public async Task RunList_WithWorkflow_PassesWorkflowToApi() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"workflow_runs":[]}""" };

        await _handler.GhRunListAsync(workflow: "ci.yml", repo: "owner/repo");

        _api.LastPath.Should().Contain("actions/workflows/ci.yml/runs");
    }

    [Fact]
    public async Task RunList_WithUser_PassesActorQuery() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"workflow_runs":[]}""" };

        await _handler.GhRunListAsync(user: "alice", repo: "owner/repo");

        _api.LastQuery.Should().ContainKey("actor").WhoseValue.Should().Be("alice");
    }

    [Fact]
    public async Task RunList_WithCommit_PassesHeadShaQuery() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"workflow_runs":[]}""" };

        await _handler.GhRunListAsync(commit: "abc123", repo: "owner/repo");

        _api.LastQuery.Should().ContainKey("head_sha").WhoseValue.Should().Be("abc123");
    }

    [Fact]
    public async Task RunList_WithCreated_PassesCreatedQuery() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"workflow_runs":[]}""" };

        await _handler.GhRunListAsync(created: ">2026-01-01", repo: "owner/repo");

        _api.LastQuery.Should().ContainKey("created").WhoseValue.Should().Be(">2026-01-01");
    }

    [Fact]
    public async Task RunView_WithWeb_ReturnsUrl() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"id":42,"html_url":"https://github.com/o/r/actions/runs/42"}""" };

        var result = await _handler.GhRunViewAsync("42", web: true, repo: "owner/repo");

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("https://github.com/o/r/actions/runs/42");
    }

    [Fact]
    public async Task RunView_WithAttempt_UsesAttemptApiPath() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"id":42,"conclusion":"failure"}""" };

        await _handler.GhRunViewAsync("42", attempt: 2, repo: "owner/repo");

        _api.LastPath.Should().Contain("/attempts/2");
    }

    [Fact]
    public async Task RunRerun_WithJob_UsesRerunJobsEndpoint() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 201, Body = "{}" };

        await _handler.GhRunRerunAsync("42", job: "123,456", repo: "owner/repo");

        _api.LastPath.Should().Contain("/rerun-jobs");
        _api.LastBody.Should().Contain("123");
        _api.LastBody.Should().Contain("456");
    }

    [Fact]
    public async Task RunRerun_WithDebug_EnablesDebugLogging() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 201, Body = "{}" };

        await _handler.GhRunRerunAsync("42", debug: true, repo: "owner/repo");

        _api.LastBody.Should().Contain("enable_debug_logging");
    }

    [Fact]
    public async Task RunView_Log_TruncatesToMaxLines() {
        var lines = Enumerable.Range(0, 300).Select(i => $"line {i}").ToArray();
        _api.NextLogLines = lines;

        var result = await _handler.GhRunViewAsync("42", log: true, max_lines: 50, repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().Contain("line 0");
        text.Should().Contain("line 49");
        text.Should().Contain("skip_lines=50");
    }

    [Fact]
    public async Task RunView_NoLog_ReturnsFullDetail() {
        _api.NextResponse = new GitHubApiResponse {
            Success = true,
            StatusCode = 200,
            Body = """{"id":42,"run_number":752,"status":"completed","conclusion":"success","display_title":"CI build","event":"push","head_branch":"main","head_sha":"abc123def456","html_url":"https://github.com/o/r/actions/runs/42","created_at":"2026-01-01T00:00:00Z","updated_at":"2026-01-01T00:01:00Z"}""",
        };

        var result = await _handler.GhRunViewAsync("42", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().Contain("42");
        text.Should().Contain("Status: completed");
        text.Should().Contain("Conclusion: success");
    }

    [Fact]
    public async Task RunView_LogWithErrorFilter_ReturnsOnlyErrorLines() {
        _api.NextLogLines = "##[group]Run tests\n##[command]dotnet test\n##[error]Test failed: assert\n##[warning]deprecated\n##[error]Another error\nnormal line".Split('\n');

        var result = await _handler.GhRunViewAsync("42", log: true, filter: "error", max_lines: 10, repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().Contain("过滤:error");
        text.Should().Contain("##[error]Test failed: assert");
        text.Should().Contain("##[error]Another error");
        text.Should().NotContain("##[warning]");
        text.Should().NotContain("##[command]");
        text.Should().NotContain("normal line");
    }

    [Fact]
    public async Task RunView_LogWithWarningFilter_ReturnsErrorAndWarningLines() {
        _api.NextLogLines = "##[error]err\n##[warning]warn\n##[command]cmd\nnormal".Split('\n');

        var result = await _handler.GhRunViewAsync("42", log: true, filter: "warning", max_lines: 10, repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().Contain("##[error]err");
        text.Should().Contain("##[warning]warn");
        text.Should().NotContain("##[command]");
        text.Should().NotContain("normal");
    }

    [Fact]
    public async Task RunView_LogWithErrorFilter_NoMatch_ReturnsEmptyMessage() {
        _api.NextLogLines = "##[warning]just a warning\nnormal line\n##[command]dotnet build".Split('\n');

        var result = await _handler.GhRunViewAsync("42", log: true, filter: "error", max_lines: 10, repo: "owner/repo");

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("未匹配到任何日志行");
    }

    [Fact]
    public async Task RunView_LogFailed_PullsFailedJobLogs() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"jobs":[{"id":1,"conclusion":"failure","name":"test"},{"id":2,"conclusion":"success","name":"build"}]}""" });
        _api.NextLogLines = "##[error]Test failed: assert\nnormal line\n##[error]Another error".Split('\n');

        var result = await _handler.GhRunViewAsync("42", log_failed: true, max_lines: 10, repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().Contain("失败步骤");
    }

    [Fact]
    public async Task RunView_ExpandSteps_ReturnsStepListFromCache() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"updated_at":"2026-01-01T00:00:00Z"}""" });
        _api.NextLogLines = "Job\tSet up job\t2026-01-01T00:00:00Z line1\nJob\tCheckout\t2026-01-01T00:00:01Z line2\nJob\tTest - Brain\t2026-01-01T00:00:02Z ##[error]failed".Split('\n');

        var result = await _handler.GhRunViewAsync("100", expand: "steps", job_id: "1", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().Contain("步骤列表");
        text.Should().Contain("Set up job");
        text.Should().Contain("Checkout");
        text.Should().Contain("Test - Brain");
    }

    [Fact]
    public async Task RunView_ExpandSteps_RestApiLogFormat_ExtractsStepNamesFromEntryName() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"updated_at":"2026-01-01T00:00:00Z"}""" });
        _api.NextLogLines = "[0_Set up job.txt] 2026-01-01T00:00:00Z line1\n[1_Checkout.txt] 2026-01-01T00:00:01Z line2\n[2_Test.txt] 2026-01-01T00:00:02Z ##[error]failed".Split('\n');

        var result = await _handler.GhRunViewAsync("100", expand: "steps", job_id: "1", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().Contain("步骤列表");
        text.Should().Contain("Set up job");
        text.Should().Contain("Checkout");
        text.Should().Contain("Test");
        text.Should().NotContain("[0_");
        text.Should().NotContain(".txt]");
    }

    [Fact]
    public async Task RunView_ExpandSteps_ParallelDownload_RestApiLogFormat_ExtractsStepNames() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"updated_at":"2026-01-01T00:00:00Z"}""" });
        _api.NextLogLines = "[0_Checkout.txt] 2026-01-01T00:00:00Z line1\n[1_Build.txt] 2026-01-01T00:00:01Z line2".Split('\n');

        var result = await _handler.GhRunViewAsync("200", expand: "steps", job_id: "1,2", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().Contain("步骤列表");
        text.Should().Contain("Checkout");
        text.Should().Contain("Build");
    }

    [Fact]
    public async Task RunView_ExpandStepName_ReturnsSectionSummaryForThatStepOnly() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"jobs":[]}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"updated_at":"2026-01-01T00:00:00Z"}""" });
        _api.NextLogLines = "Job\tSet up job\t2026-01-01T00:00:00Z setup line\nJob\tTest - Brain\t2026-01-01T00:00:01Z ##[error]failed\nJob\tTest - Brain\t2026-01-01T00:00:02Z test output".Split('\n');

        var result = await _handler.GhRunViewAsync("104", expand: "step:Test - Brain", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().Contain("步骤:Test - Brain");
        text.Should().Contain("error");
        text.Should().Contain("normal");
        text.Should().NotContain("setup line");
    }

    [Fact]
    public async Task RunView_ExpandStepName_ReturnsSectionSummary() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"jobs":[]}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"updated_at":"2026-01-01T00:00:00Z"}""" });
        _api.NextLogLines = "Job\tTest - Brain\t2026-01-01T00:00:00Z ##[error]err line\nJob\tTest - Brain\t2026-01-01T00:00:01Z normal line\nJob\tTest - Brain\t2026-01-01T00:00:02Z ##[warning]warn line".Split('\n');

        var result = await _handler.GhRunViewAsync("101", expand: "step:Test - Brain", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().Contain("sections");
        text.Should().Contain("error");
        text.Should().Contain("warning");
        text.Should().Contain("normal");
    }

    [Fact]
    public async Task RunView_ExpandStepSection_ReturnsSectionContent() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"jobs":[]}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"updated_at":"2026-01-01T00:00:00Z"}""" });
        _api.NextLogLines = "Job\tTest - Brain\t2026-01-01T00:00:00Z ##[error]err line\nJob\tTest - Brain\t2026-01-01T00:00:01Z normal line\nJob\tTest - Brain\t2026-01-01T00:00:02Z ##[warning]warn line".Split('\n');

        var result = await _handler.GhRunViewAsync("102", expand: "step:Test - Brain/section:error", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().Contain("##[error]err line");
        text.Should().NotContain("normal line");
        text.Should().NotContain("##[warning]");
    }

    [Fact]
    public async Task RunView_LogWithSkipLines_ReturnsLinesAfterSkip() {
        var lines = Enumerable.Range(0, 100).Select(i => $"line {i}").ToArray();
        _api.NextLogLines = lines;

        var result = await _handler.GhRunViewAsync("42", log: true, max_lines: 10, skip_lines: 50, repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().Contain("line 50");
        text.Should().Contain("line 59");
        text.Should().NotContain("line 49");
    }

    [Fact]
    public async Task RunView_SkipLinesExceedsTotal_ReturnsNoMoreMessage() {
        _api.NextLogLines = "line 0\nline 1\nline 2".Split('\n');

        var result = await _handler.GhRunViewAsync("42", log: true, max_lines: 10, skip_lines: 100, repo: "owner/repo");

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("未匹配到更多日志行");
    }

    [Fact]
    public async Task RunView_ExpandStepSectionWithSkipLines_ReturnsLinesAfterSkipInSection() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"jobs":[]}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"updated_at":"2026-01-01T00:00:00Z"}""" });
        _api.NextLogLines = Enumerable.Range(0, 50).Select(i => $"Job\tTest\t2026-01-01T00:00:00Z line {i}").ToArray();

        var result = await _handler.GhRunViewAsync("103", expand: "step:Test/section:normal", max_lines: 10, skip_lines: 20, repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().Contain("line 20");
        text.Should().Contain("line 29");
        text.Should().NotContain("line 19");
    }

    [Fact]
    public async Task IssueCreate_QuotesTitleWithSpaces() {
        _api.NextResponse = new GitHubApiResponse {
            Success = true,
            StatusCode = 201,
            Body = """{"number":1,"html_url":"https://github.com/o/r/issues/1"}""",
        };

        await _handler.GhIssueCreateAsync("fix: bug in parser", body: "details here", repo: "owner/repo");

        _api.LastMethod.Should().Be(HttpMethod.Post);
        _api.LastPath.Should().Be("repos/owner/repo/issues");
        _api.LastBody.Should().Contain("\"title\":\"fix: bug in parser\"");
        _api.LastBody.Should().Contain("\"body\":\"details here\"");
    }

    [Fact]
    public async Task IssueList_WithAuthor_GoesThroughIssuesApi() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = "[]" };

        await _handler.GhIssueListAsync(author: "alice", repo: "owner/repo");

        _api.LastPath.Should().Be("repos/owner/repo/issues");
        _api.LastQuery.Should().ContainKey("creator").WhoseValue.Should().Be("alice");
    }

    [Fact]
    public async Task IssueList_WithMention_GoesThroughIssuesApi() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = "[]" };

        await _handler.GhIssueListAsync(mention: "bob", repo: "owner/repo");

        _api.LastPath.Should().Be("repos/owner/repo/issues");
        _api.LastQuery.Should().ContainKey("mentioned").WhoseValue.Should().Be("bob");
    }

    [Fact]
    public async Task IssueList_WithMilestone_GoesThroughIssuesApi() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = "[]" };

        await _handler.GhIssueListAsync(milestone: "5", repo: "owner/repo");

        _api.LastPath.Should().Be("repos/owner/repo/issues");
        _api.LastQuery.Should().ContainKey("milestone").WhoseValue.Should().Be("5");
    }

    [Fact]
    public async Task IssueList_WithSearch_GoesThroughSearchApi() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"items":[]}""" };

        await _handler.GhIssueListAsync(search: "bug", repo: "owner/repo");

        _api.LastPath.Should().Be("search/issues");
        _api.LastQuery.Should().ContainKey("q").WhoseValue.Should().Contain("bug");
    }

    [Fact]
    public async Task IssueList_WithTypePr_GoesThroughSearchApi() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"items":[]}""" };

        await _handler.GhIssueListAsync(type: "pr", repo: "owner/repo");

        _api.LastPath.Should().Be("search/issues");
        _api.LastQuery.Should().ContainKey("q").WhoseValue.Should().Contain("is:pr");
    }

    [Fact]
    public async Task IssueView_WithComments_IncludesComments() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"number":42,"title":"bug","state":"open"}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """[{"body":"好建议","user":{"login":"carol"}}]""" });

        var result = await _handler.GhIssueViewAsync("42", comments: true, repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().Contain("好建议");
    }

    [Fact]
    public async Task IssueView_WithWeb_ReturnsUrl() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"number":42,"html_url":"https://github.com/o/r/issues/42"}""" };

        var result = await _handler.GhIssueViewAsync("42", web: true, repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().Contain("https://github.com/o/r/issues/42");
    }

    [Fact]
    public async Task IssueCreate_WithMilestone_IncludesMilestoneField() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 201, Body = """{"number":1,"html_url":"u"}""" };

        await _handler.GhIssueCreateAsync("title", milestone: 5, repo: "owner/repo");

        _api.LastBody.Should().Contain("\"milestone\":5");
    }

    [Fact]
    public async Task IssueClose_WithReason_IncludesStateReason() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"number":1,"state":"closed"}""" };

        await _handler.GhIssueCloseAsync("1", reason: "not_planned", repo: "owner/repo");

        _api.LastBody.Should().Contain("\"state_reason\":\"not_planned\"");
    }

    [Fact]
    public async Task IssueClose_WithDuplicateOf_CommentsAndCloses() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 201, Body = "{}" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"number":1,"state":"closed"}""" });

        await _handler.GhIssueCloseAsync("1", duplicate_of: 42, repo: "owner/repo");

        _api.LastBody.Should().Contain("\"state_reason\":\"not_planned\"");
    }

    [Fact]
    public async Task PrMerge_DefaultSquash_AppendsAutoWhenRequested() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"number":5,"node_id":"PR_test123"}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"data":{"enablePullRequestAutoMerge":{"pullRequest":{"number":5}}}}""" });

        await _handler.GhPrMergeAsync("5", auto_merge: true, repo: "owner/repo");

        _api.LastMethod.Should().Be(HttpMethod.Post);
        _api.LastPath.Should().Be("graphql");
        _api.LastBody.Should().Contain("enablePullRequestAutoMerge");
        _api.LastBody.Should().Contain("SQUASH");
    }

    [Fact]
    public async Task Api_Get_DisablesJq_PassesMethod() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"id":1,"name":"repo"}""" };

        var result = await _handler.GhApiAsync("repos/owner/repo", method: "GET");

        result.IsError.Should().BeFalse();
        _api.LastMethod.Should().Be(HttpMethod.Get);
        _api.LastPath.Should().Be("repos/owner/repo");
    }

    [Fact]
    public async Task ReleaseDownload_NoMatchingAsset_ReturnsError() {
        _api.NextResponse = new GitHubApiResponse {
            Success = true,
            StatusCode = 200,
            Body = """{"assets":[{"name":"file.zip","browser_download_url":"https://x/file.zip"}]}""",
        };

        var result = await _handler.GhReleaseDownloadAsync("v1.0", "/tmp", pattern: "*.tar.gz", repo: "owner/repo");

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("没有匹配的 asset");
    }

    [Fact]
    public async Task ReleaseDownload_Success_DownloadsAllAssets() {
        _api.NextResponse = new GitHubApiResponse {
            Success = true,
            StatusCode = 200,
            Body = """{"assets":[{"name":"a.zip","browser_download_url":"https://x/a.zip"},{"name":"b.tar.gz","browser_download_url":"https://x/b.tar.gz"}]}""",
        };
        var fakeDownloader = new FakeDownloader();
        var handler = new GitHubToolHandlers(fakeDownloader, new InMemoryFileSystem(), new PersistencePipeline(new InMemoryFileSystem()), _api, null, NullLogger<GitHubToolHandlers>.Instance);

        var result = await handler.GhReleaseDownloadAsync("v1.0", "/tmp", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().Contain("2 成功");
        text.Should().Contain("0 失败");
        fakeDownloader.StartCallCount.Should().Be(2);
    }

    [Fact]
    public async Task ReleaseDownload_ViewFails_PropagatesError() {
        _api.NextResponse = new GitHubApiResponse {
            Success = false,
            StatusCode = 404,
            Error = "release not found",
        };

        var result = await _handler.GhReleaseDownloadAsync("v9.9", "/tmp", repo: "owner/repo");

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("release not found");
    }

    [Fact]
    public async Task ReleaseList_Success_ReturnsSummarizedJson() {
        _api.NextResponse = new GitHubApiResponse {
            Success = true,
            StatusCode = 200,
            Body = """[{"id":123,"tag_name":"v1.0","name":"Release v1.0","draft":false,"prerelease":false,"created_at":"2026-09-01T00:00:00Z","published_at":"2026-09-01T00:00:00Z","body":"notes","url":"https://api.github.com/repos/o/r/releases/123","assets_url":"https://api.github.com/repos/o/r/releases/123/assets","upload_url":"https://uploads.github.com/repos/o/r/releases/123/assets{?name,label}","html_url":"https://github.com/o/r/releases/tag/v1.0","author":{"login":"user","url":"https://api.github.com/users/user","avatar_url":"https://avatars.githubusercontent.com/u/1?v=4"},"assets":[{"name":"file.zip","size":1024,"browser_download_url":"https://github.com/o/r/releases/download/v1.0/file.zip"}]}]""",
        };

        var result = await _handler.GhReleaseListAsync(repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().Contain("\"tag_name\":\"v1.0\"");
        text.Should().Contain("\"name\":\"Release v1.0\"");
        text.Should().Contain("\"draft\":false");
        text.Should().NotContain("assets_url");
        text.Should().NotContain("upload_url");
        text.Should().NotContain("avatar_url");
        text.Should().NotContain("browser_download_url");
    }

    [Fact]
    public async Task ReleaseList_WithExcludeDrafts_FiltersOutDrafts() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """[{"id":1,"tag_name":"v1","name":"v1","draft":false,"prerelease":false,"assets":[]},{"id":2,"tag_name":"v2","name":"v2","draft":true,"prerelease":false,"assets":[]}]""" };

        var result = await _handler.GhReleaseListAsync(exclude_drafts: true, repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().Contain("\"tag_name\":\"v1\"");
        text.Should().NotContain("\"tag_name\":\"v2\"");
    }

    [Fact]
    public async Task ReleaseList_WithExcludePrereleases_FiltersOutPrereleases() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """[{"id":1,"tag_name":"v1","name":"v1","draft":false,"prerelease":false,"assets":[]},{"id":2,"tag_name":"v2","name":"v2","draft":false,"prerelease":true,"assets":[]}]""" };

        var result = await _handler.GhReleaseListAsync(exclude_prereleases: true, repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().Contain("\"tag_name\":\"v1\"");
        text.Should().NotContain("\"tag_name\":\"v2\"");
    }

    [Fact]
    public async Task ReleaseView_WithWeb_ReturnsUrl() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"id":1,"tag_name":"v1","html_url":"https://github.com/o/r/releases/tag/v1"}""" };

        var result = await _handler.GhReleaseViewAsync("v1", web: true, repo: "owner/repo");

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("https://github.com/o/r/releases/tag/v1");
    }

    [Fact]
    public async Task ReleaseCreate_WithGenerateNotes_RequestsAutoNotes() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 201, Body = """{"id":1,"tag_name":"v1"}""" };

        await _handler.GhReleaseCreateAsync("v1", generate_notes: true, repo: "owner/repo");

        _api.LastBody.Should().Contain("\"generate_release_notes\":true");
    }

    [Fact]
    public async Task ReleaseDelete_WithCleanupTag_DeletesTagAfterRelease() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"id":123,"tag_name":"v1"}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 204, Body = "" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 204, Body = "" });

        var result = await _handler.GhReleaseDeleteAsync("v1", cleanup_tag: true, repo: "owner/repo");

        result.IsError.Should().BeFalse();
        _api.LastMethod.Should().Be(HttpMethod.Delete);
        _api.LastPath.Should().Contain("git/refs/tags/v1");
    }

    [Fact]
    public async Task ReleaseDownload_WithSkipExisting_SkipsExistingFiles() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"assets":[{"name":"a.zip","browser_download_url":"https://x/a.zip"},{"name":"b.zip","browser_download_url":"https://x/b.zip"}]}""" };
        var fs = new InMemoryFileSystem();
        fs.CreateDirectory("/tmp");
        await fs.WriteAllText("/tmp/a.zip", "existing");
        var fakeDownloader = new FakeDownloader();
        var handler = new GitHubToolHandlers(fakeDownloader, fs, new PersistencePipeline(new InMemoryFileSystem()), _api, null, NullLogger<GitHubToolHandlers>.Instance);

        var result = await handler.GhReleaseDownloadAsync("v1.0", "/tmp", skip_existing: true, repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().Contain("[SKIP] a.zip");
        text.Should().Contain("1 跳过");
        fakeDownloader.StartCallCount.Should().Be(1);
    }

    [Fact]
    public async Task ReleaseDownload_WithoutClobber_FailsOnExistingFile() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"assets":[{"name":"a.zip","browser_download_url":"https://x/a.zip"}]}""" };
        var fs = new InMemoryFileSystem();
        fs.CreateDirectory("/tmp");
        await fs.WriteAllText("/tmp/a.zip", "existing");
        var fakeDownloader = new FakeDownloader();
        var handler = new GitHubToolHandlers(fakeDownloader, fs, new PersistencePipeline(new InMemoryFileSystem()), _api, null, NullLogger<GitHubToolHandlers>.Instance);

        var result = await handler.GhReleaseDownloadAsync("v1.0", "/tmp", repo: "owner/repo");

        var text = result.GetFirstText();
        text.Should().Contain("[FAIL] a.zip");
        text.Should().Contain("文件已存在");
        fakeDownloader.StartCallCount.Should().Be(0);
    }

    [Fact]
    public async Task ReleaseUpload_WithClobber_DeletesExistingAssetFirst() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"id":123,"assets":[{"name":"file.zip","id":456}]}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 204, Body = "" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 201, Body = "{}" });
        var fs = new InMemoryFileSystem();
        await fs.WriteAllText("/data/file.zip", "content");
        var handler = new GitHubToolHandlers(new FakeDownloader(), fs, new PersistencePipeline(new InMemoryFileSystem()), _api, null, NullLogger<GitHubToolHandlers>.Instance);

        var result = await handler.GhReleaseUploadAsync("v1", "/data/file.zip", clobber: true, repo: "owner/repo");

        result.IsError.Should().BeFalse();
    }

    [Fact]
    public async Task BranchSyncProtection_YmlNotFound_ReturnsError() {
        var fs = new InMemoryFileSystem();
        var handler = new GitHubToolHandlers(
            new FakeDownloader(), fs, new PersistencePipeline(fs),
            _api, null, NullLogger<GitHubToolHandlers>.Instance);

        var result = await handler.GhBranchSyncProtectionAsync(
            branch: "main", yml_path: "nonexistent.yml", repo: "owner/repo");

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("不存在");
    }

    [Fact]
    public async Task BranchSyncProtection_Consistent_ReturnsNoSyncNeeded() {
        var fs = new InMemoryFileSystem();
        var ymlPath = Path.Combine(Environment.CurrentDirectory, ".github/workflows/ci-unit-tests.yml");
        await fs.WriteAllTextAsync(ymlPath, """
            jobs:
              unit-tests:
                name: Unit - ${{ matrix.name }}
                strategy:
                  matrix:
                    include:
                      - name: Abs
                        csproj: lib/abs.tests/Abs.Tests.csproj
            """);
        var handler = new GitHubToolHandlers(
            new FakeDownloader(), fs, new PersistencePipeline(fs),
            _api, null, NullLogger<GitHubToolHandlers>.Instance);

        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"strict":false,"contexts":["unit-tests / Unit - Abs"]}""",
        });

        var result = await handler.GhBranchSyncProtectionAsync(
            branch: "main", yml_path: ".github/workflows/ci-unit-tests.yml", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("完全一致");
    }

    [Fact]
    public async Task RepoView_WithWeb_ReturnsUrl() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"full_name":"o/r","html_url":"https://github.com/o/r"}""" };

        var result = await _handler.GhRepoViewAsync(repo: "owner/repo", web: true);

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("https://github.com/o/r");
    }

    [Fact]
    public async Task RepoList_WithLanguage_PassesLanguageQuery() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = "[]" };

        await _handler.GhRepoListAsync(language: "C#", repo: "owner/repo");

        _api.LastQuery.Should().ContainKey("language").WhoseValue.Should().Be("C#");
    }

    [Fact]
    public async Task RepoList_WithVisibility_PassesVisibilityQuery() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = "[]" };

        await _handler.GhRepoListAsync(visibility: "private", repo: "owner/repo");

        _api.LastQuery.Should().ContainKey("visibility").WhoseValue.Should().Be("private");
    }

    [Fact]
    public async Task RepoList_WithSource_FiltersNonForks() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """[{"name":"r1","fork":false},{"name":"r2","fork":true}]""" };

        var result = await _handler.GhRepoListAsync(source: true, repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().Contain("\"name\":\"r1\"");
        text.Should().NotContain("\"name\":\"r2\"");
    }

    [Fact]
    public async Task RepoList_WithFork_OnlyForks() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """[{"name":"r1","fork":false},{"name":"r2","fork":true}]""" };

        var result = await _handler.GhRepoListAsync(fork: true, repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().NotContain("\"name\":\"r1\"");
        text.Should().Contain("\"name\":\"r2\"");
    }

    [Fact]
    public async Task RepoCreate_WithHomepage_IncludesHomepageField() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 201, Body = """{"full_name":"o/r"}""" };

        await _handler.GhRepoCreateAsync("myrepo", homepage: "https://example.com");

        _api.LastBody.Should().Contain("\"homepage\":\"https://example.com\"");
    }

    [Fact]
    public async Task RepoCreate_WithGitignore_IncludesGitignoreTemplate() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 201, Body = """{"full_name":"o/r"}""" };

        await _handler.GhRepoCreateAsync("myrepo", gitignore: "VisualStudio");

        _api.LastBody.Should().Contain("\"gitignore_template\":\"VisualStudio\"");
    }

    [Fact]
    public async Task RepoCreate_WithLicense_IncludesLicenseTemplate() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 201, Body = """{"full_name":"o/r"}""" };

        await _handler.GhRepoCreateAsync("myrepo", license: "mit");

        _api.LastBody.Should().Contain("\"license_template\":\"mit\"");
    }

    [Fact]
    public async Task RepoFork_WithOrg_ForksToSpecifiedOrg() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 202, Body = """{"full_name":"org/repo"}""" };

        await _handler.GhRepoForkAsync("owner/repo", org: "myorg");

        _api.LastBody.Should().Contain("\"organization\":\"myorg\"");
    }

    [Fact]
    public async Task RepoEdit_WithDescription_PatchesRepo() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"full_name":"o/r"}""" };

        await _handler.GhRepoEditAsync(repo: "owner/repo", description: "new desc");

        _api.LastMethod.Should().Be(HttpMethod.Patch);
        _api.LastPath.Should().Be("repos/owner/repo");
        _api.LastBody.Should().Contain("\"description\":\"new desc\"");
    }

    [Fact]
    public async Task RepoDelete_WithYes_DeletesRepo() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 204, Body = "" };

        var result = await _handler.GhRepoDeleteAsync("owner/repo", yes: true);

        result.IsError.Should().BeFalse();
        _api.LastMethod.Should().Be(HttpMethod.Delete);
        _api.LastPath.Should().Be("repos/owner/repo");
    }

    [Fact]
    public async Task RepoDelete_WithoutYes_ReturnsError() {
        var result = await _handler.GhRepoDeleteAsync("owner/repo");

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("yes=true");
    }

    [Fact]
    public async Task RepoArchive_SendsArchivedTrue() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = "{}" };

        await _handler.GhRepoArchiveAsync(repo: "owner/repo");

        _api.LastMethod.Should().Be(HttpMethod.Patch);
        _api.LastBody.Should().Contain("\"archived\":true");
    }

    [Fact]
    public async Task RepoUnarchive_SendsArchivedFalse() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = "{}" };

        await _handler.GhRepoUnarchiveAsync(repo: "owner/repo");

        _api.LastMethod.Should().Be(HttpMethod.Patch);
        _api.LastBody.Should().Contain("\"archived\":false");
    }

    [Fact]
    public async Task ReleaseDeleteAsset_Success_DeletesAsset() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"id":123,"assets":[{"name":"file.zip","id":456}]}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 204, Body = "" });

        var result = await _handler.GhReleaseDeleteAssetAsync("v1", "file.zip", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        _api.LastMethod.Should().Be(HttpMethod.Delete);
        _api.LastPath.Should().Contain("releases/123/assets/456");
    }

    [Fact]
    public async Task ReleaseEdit_WithNewTag_PatchesRelease() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"id":123,"tag_name":"v1"}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"id":123,"tag_name":"v2"}""" });

        await _handler.GhReleaseEditAsync("v1", new_tag: "v2", repo: "owner/repo");

        _api.LastMethod.Should().Be(HttpMethod.Patch);
        _api.LastBody.Should().Contain("\"tag_name\":\"v2\"");
    }

    [Fact]
    public async Task PrComment_PostsToIssuesCommentsEndpoint() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 201, Body = "{}" };

        await _handler.GhPrCommentAsync("42", "good PR", repo: "owner/repo");

        _api.LastMethod.Should().Be(HttpMethod.Post);
        _api.LastPath.Should().Be("repos/owner/repo/issues/42/comments");
        _api.LastBody.Should().Contain("good PR");
    }

    [Fact]
    public async Task PrEdit_WithTitle_PatchesPr() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = "{}" };

        await _handler.GhPrEditAsync("42", title: "new title", repo: "owner/repo");

        _api.LastMethod.Should().Be(HttpMethod.Patch);
        _api.LastPath.Should().Be("repos/owner/repo/pulls/42");
        _api.LastBody.Should().Contain("\"title\":\"new title\"");
    }

    [Fact]
    public async Task PrReview_Approve_PostsReviewEvent() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = "{}" };

        await _handler.GhPrReviewAsync("42", action: "approve", repo: "owner/repo");

        _api.LastMethod.Should().Be(HttpMethod.Post);
        _api.LastPath.Should().Be("repos/owner/repo/pulls/42/reviews");
        _api.LastBody.Should().Contain("\"event\":\"APPROVE\"");
    }

    [Fact]
    public async Task IssueReopen_PatchesStateOpen() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = "{}" };

        await _handler.GhIssueReopenAsync("42", repo: "owner/repo");

        _api.LastMethod.Should().Be(HttpMethod.Patch);
        _api.LastBody.Should().Contain("\"state\":\"open\"");
    }

    [Fact]
    public async Task IssueEdit_WithTitle_PatchesIssue() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = "{}" };

        await _handler.GhIssueEditAsync("42", title: "updated", repo: "owner/repo");

        _api.LastMethod.Should().Be(HttpMethod.Patch);
        _api.LastPath.Should().Be("repos/owner/repo/issues/42");
        _api.LastBody.Should().Contain("\"title\":\"updated\"");
    }

    [Fact]
    public async Task IssueDelete_WithYes_UsesGraphQL() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"number":42,"node_id":"I_kw123"}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = "{}" });

        var result = await _handler.GhIssueDeleteAsync("42", yes: true, repo: "owner/repo");

        result.IsError.Should().BeFalse();
        _api.LastPath.Should().Be("graphql");
        _api.LastBody.Should().Contain("deleteIssue");
    }

    [Fact]
    public async Task RunDownload_Success_DownloadsArtifacts() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"artifacts":[{"name":"artifact1","id":123}]}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = "" });

        var result = await _handler.GhRunDownloadAsync("42", "/tmp", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().Contain("1 成功");
    }

    [Fact]
    public async Task RepoRename_PostsToRenameEndpoint() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = "{}" };

        await _handler.GhRepoRenameAsync("new-name", repo: "owner/repo");

        _api.LastMethod.Should().Be(HttpMethod.Post);
        _api.LastPath.Should().Be("repos/owner/repo/rename");
        _api.LastBody.Should().Contain("\"new_name\":\"new-name\"");
    }

    [Fact]
    public async Task RepoSync_PostsToMergeUpstream() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = "{}" };

        await _handler.GhRepoSyncAsync(branch: "main", repo: "owner/repo");

        _api.LastMethod.Should().Be(HttpMethod.Post);
        _api.LastPath.Should().Be("repos/owner/repo/merge-upstream");
        _api.LastBody.Should().Contain("\"branch\":\"main\"");
    }

    [Fact]
    public async Task RepoSetDefault_PatchesDefaultBranch() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = "{}" };

        await _handler.GhRepoSetDefaultAsync("develop", repo: "owner/repo");

        _api.LastMethod.Should().Be(HttpMethod.Patch);
        _api.LastPath.Should().Be("repos/owner/repo");
        _api.LastBody.Should().Contain("\"default_branch\":\"develop\"");
    }

    [Fact]
    public async Task PrLock_PutsToLockEndpoint() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 204, Body = "" };

        await _handler.GhPrLockAsync("42", reason: "spam", repo: "owner/repo");

        _api.LastMethod.Should().Be(HttpMethod.Put);
        _api.LastPath.Should().Be("repos/owner/repo/issues/42/lock");
        _api.LastBody.Should().Contain("\"lock_reason\":\"spam\"");
    }

    [Fact]
    public async Task PrUnlock_DeletesLockEndpoint() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 204, Body = "" };

        await _handler.GhPrUnlockAsync("42", repo: "owner/repo");

        _api.LastMethod.Should().Be(HttpMethod.Delete);
        _api.LastPath.Should().Be("repos/owner/repo/issues/42/lock");
    }

    [Fact]
    public async Task IssueLock_PutsToLockEndpoint() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 204, Body = "" };

        await _handler.GhIssueLockAsync("42", repo: "owner/repo");

        _api.LastMethod.Should().Be(HttpMethod.Put);
        _api.LastPath.Should().Be("repos/owner/repo/issues/42/lock");
    }

    [Fact]
    public async Task IssueUnlock_DeletesLockEndpoint() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 204, Body = "" };

        await _handler.GhIssueUnlockAsync("42", repo: "owner/repo");

        _api.LastMethod.Should().Be(HttpMethod.Delete);
        _api.LastPath.Should().Be("repos/owner/repo/issues/42/lock");
    }

    [Fact]
    public async Task RunDelete_DeletesRun() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 204, Body = "" };

        await _handler.GhRunDeleteAsync("42", repo: "owner/repo");

        _api.LastMethod.Should().Be(HttpMethod.Delete);
        _api.LastPath.Should().Be("repos/owner/repo/actions/runs/42");
    }

    [Fact]
    public async Task PrStatus_ListsOpenPrs() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """[{"number":1,"title":"feat","state":"open","user":{"login":"alice"},"head":{"ref":"dev"},"draft":false,"mergeable":true}]""" };

        var result = await _handler.GhPrStatusAsync(repo: "owner/repo");

        result.IsError.Should().BeFalse();
        _api.LastPath.Should().Be("repos/owner/repo/pulls");
        result.GetFirstText().Should().Contain("alice");
    }

    [Fact]
    public async Task PrReady_PatchesDraftFalse() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = "{}" };

        await _handler.GhPrReadyAsync("42", repo: "owner/repo");

        _api.LastMethod.Should().Be(HttpMethod.Patch);
        _api.LastPath.Should().Be("repos/owner/repo/pulls/42");
        _api.LastBody.Should().Contain("\"draft\":false");
    }

    [Fact]
    public async Task PrReady_Undo_PatchesDraftTrue() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = "{}" };

        await _handler.GhPrReadyAsync("42", undo: true, repo: "owner/repo");

        _api.LastBody.Should().Contain("\"draft\":true");
    }

    [Fact]
    public async Task PrUpdateBranch_PutsUpdateBranch() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 202, Body = "{}" };

        await _handler.GhPrUpdateBranchAsync("42", repo: "owner/repo");

        _api.LastMethod.Should().Be(HttpMethod.Put);
        _api.LastPath.Should().Be("repos/owner/repo/pulls/42/update-branch");
        _api.LastBody.Should().Contain("\"update_method\":\"merge\"");
    }

    [Fact]
    public async Task PrUpdateBranch_Rebase_UsesRebaseMethod() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 202, Body = "{}" };

        await _handler.GhPrUpdateBranchAsync("42", rebase: true, repo: "owner/repo");

        _api.LastBody.Should().Contain("\"update_method\":\"rebase\"");
    }

    [Fact]
    public async Task IssueStatus_ListsOpenIssues() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """[{"number":1,"title":"bug","state":"open","user":{"login":"bob"}}]""" };

        var result = await _handler.GhIssueStatusAsync(repo: "owner/repo");

        result.IsError.Should().BeFalse();
        _api.LastPath.Should().Be("repos/owner/repo/issues");
        result.GetFirstText().Should().Contain("bob");
    }

    [Fact]
    public async Task IssuePin_UsesGraphQL() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"node_id":"I_kw123"}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = "{}" });

        var result = await _handler.GhIssuePinAsync("42", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        _api.LastPath.Should().Be("graphql");
        _api.LastBody.Should().Contain("pinIssue");
    }

    [Fact]
    public async Task IssueUnpin_UsesGraphQL() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"node_id":"I_kw123"}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = "{}" });

        var result = await _handler.GhIssueUnpinAsync("42", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        _api.LastBody.Should().Contain("unpinIssue");
    }

    [Fact]
    public async Task IssueTransfer_UsesGraphQLWithDestNodeId() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"node_id":"I_kw123"}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"node_id":"R_kw456"}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = "{}" });

        var result = await _handler.GhIssueTransferAsync("42", "dest/repo", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        _api.LastBody.Should().Contain("transferIssue");
        _api.LastBody.Should().Contain("R_kw456");
    }

    [Fact]
    public async Task RunWatch_PollsUntilCompleted() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"status":"in_progress","display_title":"build"}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"status":"completed","conclusion":"success","display_title":"build"}""" });

        var result = await _handler.GhRunWatchAsync("42", interval: 1, repo: "owner/repo");

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("success");
    }

    [Fact]
    public async Task ReleaseVerify_ReturnsAssetMetadata() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"tag_name":"v1.0","assets":[{"name":"bin","digest":"sha256:abc"}]}""" };

        var result = await _handler.GhReleaseVerifyAsync("v1.0", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("v1.0");
        result.GetFirstText().Should().Contain("cosign");
    }

    [Fact]
    public async Task RepoAutolinkList_ListsAutolinks() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """[{"id":1,"key_prefix":"TICKET-","url_template":"https://example.com/<num>"}]""" };

        var result = await _handler.GhRepoAutolinkListAsync(repo: "owner/repo");

        result.IsError.Should().BeFalse();
        _api.LastPath.Should().Be("repos/owner/repo/keys/autolinks");
        result.GetFirstText().Should().Contain("TICKET-");
    }

    [Fact]
    public async Task RepoAutolinkCreate_PostsToAutolinksEndpoint() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 201, Body = "{}" };

        await _handler.GhRepoAutolinkCreateAsync("TICKET-", "https://example.com/<num>", repo: "owner/repo");

        _api.LastMethod.Should().Be(HttpMethod.Post);
        _api.LastPath.Should().Be("repos/owner/repo/keys/autolinks");
        _api.LastBody.Should().Contain("\"key_prefix\":\"TICKET-\"");
    }

    [Fact]
    public async Task RepoAutolinkDelete_DeletesAutolink() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 204, Body = "" };

        await _handler.GhRepoAutolinkDeleteAsync(1, repo: "owner/repo");

        _api.LastMethod.Should().Be(HttpMethod.Delete);
        _api.LastPath.Should().Be("repos/owner/repo/keys/autolinks/1");
    }

    [Fact]
    public async Task RepoDeployKeyList_ListsKeys() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """[{"id":1,"title":"ci","read_only":true,"created_at":"2026-01-01"}]""" };

        var result = await _handler.GhRepoDeployKeyListAsync(repo: "owner/repo");

        result.IsError.Should().BeFalse();
        _api.LastPath.Should().Be("repos/owner/repo/keys");
        result.GetFirstText().Should().Contain("ci");
    }

    [Fact]
    public async Task RepoDeployKeyAdd_PostsToKeysEndpoint() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 201, Body = "{}" };

        await _handler.GhRepoDeployKeyAddAsync("ci", "ssh-rsa AAA...", read_only: true, repo: "owner/repo");

        _api.LastMethod.Should().Be(HttpMethod.Post);
        _api.LastPath.Should().Be("repos/owner/repo/keys");
        _api.LastBody.Should().Contain("\"title\":\"ci\"");
        _api.LastBody.Should().Contain("\"read_only\":true");
    }

    [Fact]
    public async Task RepoDeployKeyDelete_DeletesKey() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 204, Body = "" };

        await _handler.GhRepoDeployKeyDeleteAsync(1, repo: "owner/repo");

        _api.LastMethod.Should().Be(HttpMethod.Delete);
        _api.LastPath.Should().Be("repos/owner/repo/keys/1");
    }

    [Fact]
    public async Task RepoGitignoreList_ListsTemplates() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"names":["Java","Python","Node"]}""" };

        var result = await _handler.GhRepoGitignoreListAsync();

        result.IsError.Should().BeFalse();
        _api.LastPath.Should().Be("gitignore/templates");
        result.GetFirstText().Should().Contain("Java");
    }

    [Fact]
    public async Task RepoGitignoreView_ReturnsTemplateSource() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"source":"*.class"}""" };

        var result = await _handler.GhRepoGitignoreViewAsync("Java");

        result.IsError.Should().BeFalse();
        _api.LastPath.Should().Be("gitignore/templates/Java");
        result.GetFirstText().Should().Contain("*.class");
    }

    [Fact]
    public async Task RepoLicenseList_ListsLicenses() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """[{"key":"mit","name":"MIT License","spdx_id":"MIT"}]""" };

        var result = await _handler.GhRepoLicenseListAsync();

        result.IsError.Should().BeFalse();
        _api.LastPath.Should().Be("licenses");
        result.GetFirstText().Should().Contain("mit");
    }

    [Fact]
    public async Task RepoLicenseView_ReturnsLicenseDetails() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"key":"mit","body":"MIT License text"}""" };

        var result = await _handler.GhRepoLicenseViewAsync("mit");

        result.IsError.Should().BeFalse();
        _api.LastPath.Should().Be("licenses/mit");
    }

    [Fact]
    public async Task LabelList_ListsLabels() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """[{"name":"bug","color":"d73a4a","description":"Bug fix"}]""" };

        var result = await _handler.GhLabelListAsync(repo: "owner/repo");

        result.IsError.Should().BeFalse();
        _api.LastPath.Should().Be("repos/owner/repo/labels");
        result.GetFirstText().Should().Contain("bug");
    }

    [Fact]
    public async Task LabelCreate_PostsToLabelsEndpoint() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 201, Body = "{}" };

        await _handler.GhLabelCreateAsync("enhancement", color: "a2eeef", description: "New feature", repo: "owner/repo");

        _api.LastMethod.Should().Be(HttpMethod.Post);
        _api.LastPath.Should().Be("repos/owner/repo/labels");
        _api.LastBody.Should().Contain("\"name\":\"enhancement\"");
        _api.LastBody.Should().Contain("\"color\":\"a2eeef\"");
    }

    [Fact]
    public async Task LabelDelete_DeletesLabel() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 204, Body = "" };

        await _handler.GhLabelDeleteAsync("bug", yes: true, repo: "owner/repo");

        _api.LastMethod.Should().Be(HttpMethod.Delete);
        _api.LastPath.Should().Be("repos/owner/repo/labels/bug");
    }

    [Fact]
    public async Task SearchRepos_SearchesRepositories() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"total_count":1,"items":[{"full_name":"owner/repo","stargazers_count":100,"description":"test"}]}""" };

        var result = await _handler.GhSearchReposAsync("stars:>50");

        result.IsError.Should().BeFalse();
        _api.LastPath.Should().Be("search/repositories");
        _api.LastQuery["q"].Should().Be("stars:>50");
        result.GetFirstText().Should().Contain("owner/repo");
    }

    [Fact]
    public async Task SearchIssues_AddsIsIssueQualifier() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"total_count":0,"items":[]}""" };

        await _handler.GhSearchIssuesAsync("repo:owner/repo");

        _api.LastPath.Should().Be("search/issues");
        _api.LastQuery["q"].Should().Contain("is:issue");
        _api.LastQuery["q"].Should().Contain("repo:owner/repo");
    }

    [Fact]
    public async Task SearchPrs_AddsIsPrQualifier() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"total_count":0,"items":[]}""" };

        await _handler.GhSearchPrsAsync("repo:owner/repo");

        _api.LastQuery["q"].Should().Contain("is:pr");
    }

    [Fact]
    public async Task WorkflowList_ListsWorkflows() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"workflows":[{"id":123,"name":"CI","state":"active","path":".github/workflows/ci.yml"}]}""" };

        var result = await _handler.GhWorkflowListAsync(repo: "owner/repo");

        result.IsError.Should().BeFalse();
        _api.LastPath.Should().Be("repos/owner/repo/actions/workflows");
        result.GetFirstText().Should().Contain("CI");
    }

    [Fact]
    public async Task WorkflowRun_PostsDispatches() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 204, Body = "" };

        await _handler.GhWorkflowRunAsync("ci.yml", @ref: "develop", repo: "owner/repo");

        _api.LastMethod.Should().Be(HttpMethod.Post);
        _api.LastPath.Should().Be("repos/owner/repo/actions/workflows/ci.yml/dispatches");
        _api.LastBody.Should().Contain("\"ref\":\"develop\"");
    }

    [Fact]
    public async Task WorkflowEnable_PutsEnable() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 204, Body = "" };

        await _handler.GhWorkflowEnableAsync("123", repo: "owner/repo");

        _api.LastMethod.Should().Be(HttpMethod.Put);
        _api.LastPath.Should().Be("repos/owner/repo/actions/workflows/123/enable");
    }

    [Fact]
    public async Task WorkflowDisable_PutsDisable() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 204, Body = "" };

        await _handler.GhWorkflowDisableAsync("123", repo: "owner/repo");

        _api.LastMethod.Should().Be(HttpMethod.Put);
        _api.LastPath.Should().Be("repos/owner/repo/actions/workflows/123/disable");
    }

    [Fact]
    public async Task AuthStatus_WithValidToken_ReturnsLogin() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"login":"testuser","name":"Test User"}""" };

        var result = await _handler.GhAuthStatusAsync();

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("testuser");
    }
}

internal sealed class FakeGitHubApiClient : IGitHubApiClient {
    private readonly Queue<GitHubApiResponse> _responses = new();
    public GitHubApiResponse NextResponse {
        get => _responses.Count > 0 ? _responses.Peek() : _default;
        set { _responses.Clear(); _responses.Enqueue(value); }
    }
    private readonly GitHubApiResponse _default = new() { Success = true, StatusCode = 200, Body = "[]" };
    public string? LastPath { get; private set; }
    public HttpMethod? LastMethod { get; private set; }
    public string? LastBody { get; private set; }
    public IReadOnlyDictionary<string, string> LastQuery { get; private set; } = new Dictionary<string, string>();
    public void EnqueueResponse(GitHubApiResponse response) => _responses.Enqueue(response);
    public IEnumerable<string> NextLogLines { get; set; } = Array.Empty<string>();

    public Task<GitHubApiResponse> SendAsync(HttpMethod method, string path, string? body = null, IReadOnlyDictionary<string, string>? query = null, bool paginate = false, CancellationToken ct = default) {
        LastMethod = method;
        LastPath = path;
        LastBody = body;
        LastQuery = query ?? new Dictionary<string, string>();
        var response = _responses.Count > 0 ? _responses.Dequeue() : _default;
        return Task.FromResult(response);
    }

    public async IAsyncEnumerable<string> GetRunLogsAsync(string owner, string repo, long runId, [EnumeratorCancellation] CancellationToken ct = default) {
        foreach (var line in NextLogLines) {
            ct.ThrowIfCancellationRequested();
            yield return line;
        }
    }

    public async IAsyncEnumerable<string> GetJobLogsAsync(string owner, string repo, long jobId, [EnumeratorCancellation] CancellationToken ct = default) {
        foreach (var line in NextLogLines) {
            ct.ThrowIfCancellationRequested();
            yield return line;
        }
    }

    public Task<GitHubApiResponse> UploadAssetAsync(string owner, string repo, long releaseId, string fileName, Stream fileStream, CancellationToken ct = default) {
        LastMethod = HttpMethod.Post;
        LastPath = $"repos/{owner}/{repo}/releases/{releaseId}/assets";
        return Task.FromResult(NextResponse);
    }

    public Task<GitHubApiResponse> UploadAttachmentAsync(long repositoryId, string fileName, Stream fileStream, CancellationToken ct = default) {
        LastMethod = HttpMethod.Post;
        LastPath = "user-attachments/assets";
        var response = _responses.Count > 0 ? _responses.Dequeue() : _default;
        return Task.FromResult(response);
    }

    public Task<GitHubApiResponse> DownloadArtifactAsync(string owner, string repo, long artifactId, string filePath, CancellationToken ct = default) {
        LastMethod = HttpMethod.Get;
        LastPath = $"repos/{owner}/{repo}/actions/artifacts/{artifactId}/zip";
        return Task.FromResult(NextResponse);
    }
}

internal sealed class FakeDownloader : IDownloader {
    public DownloadResult NextResult { get; set; } = new(true, "", 100, 100, TimeSpan.Zero, DownloadState.Completed);
    public int StartCallCount { get; private set; }
    public IDownloadSession StartDownload(string url, string filePath, DownloadOptions? options = null, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default) {
        StartCallCount++;
        return new FakeDownloadSession { Result = NextResult with { FilePath = filePath } };
    }
}

internal sealed class FakeGitCommandRunner : IGitCommandRunner {
    public List<string> ExecutedCommands { get; } = new();
    public bool NextSuccess { get; set; } = true;
    public string NextOutput { get; set; } = "";
    public Task<GitCommandResult> ExecuteAsync(string arguments, string? workingDirectory = null, CancellationToken ct = default) {
        ExecutedCommands.Add(arguments);
        return Task.FromResult(new GitCommandResult { Success = NextSuccess, Output = NextOutput, ExitCode = NextSuccess ? 0 : 1 });
    }
    public Task<MergeConflictResult> DetectMergeConflictAsync(string branch1, string branch2, string? workingDirectory = null, CancellationToken ct = default)
        => Task.FromResult(new MergeConflictResult { HasConflict = false });
    public Task<StaleConflictMarkerResult> DetectStaleConflictMarkersAsync(string? workingDirectory = null, CancellationToken ct = default)
        => Task.FromResult(new StaleConflictMarkerResult { HasStaleMarkers = false });
}

internal sealed class FakeDownloadSession : IDownloadSession {
    public DownloadResult Result { get; set; } = new(true, "", 0, 0, TimeSpan.Zero, DownloadState.Completed);
    public DownloadState State => Result.FinalState;
    public Task PauseAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task ResumeAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task CancelAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task<DownloadResult> WaitForCompletionAsync(CancellationToken ct = default) => Task.FromResult(Result);
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}