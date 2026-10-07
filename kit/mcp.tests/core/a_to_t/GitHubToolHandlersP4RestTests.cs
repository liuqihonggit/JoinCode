namespace Mcp.Tests;

/// <summary>
/// P4 命令测试 — browse/cache/ruleset/status/codespace/discussion/project/alias/extension/licenses
/// <para>补充 P4.cs 中除 gist/org/ssh-key/gpg-key/secret/variable 外的 28 个命令测试</para>
/// </summary>
public sealed partial class GitHubToolHandlersTests {

    // === Browse ===

    [Fact]
    public async Task Browse_WithRepo_ReturnsGitHubUrl() {
        var result = await _handler.GhBrowseAsync(repo: "owner/repo", no_browser: true);

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("https://github.com/owner/repo");
    }

    [Fact]
    public async Task Browse_WithIssueNumber_ReturnsIssueUrl() {
        var result = await _handler.GhBrowseAsync(target: "42", repo: "owner/repo", no_browser: true);

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("https://github.com/owner/repo/issues/42");
    }

    [Fact]
    public async Task Browse_WithBranch_ReturnsTreeUrl() {
        var result = await _handler.GhBrowseAsync(branch: "develop", repo: "owner/repo", no_browser: true);

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("https://github.com/owner/repo/tree/develop");
    }

    [Fact]
    public async Task Browse_WithActions_ReturnsActionsUrl() {
        var result = await _handler.GhBrowseAsync(actions: true, repo: "owner/repo", no_browser: true);

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("https://github.com/owner/repo/actions");
    }

    // === Cache ===

    [Fact]
    public async Task CacheList_ListsActionsCaches() {
        _api.NextResponse = new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"total_count":1,"actions_caches":[{"id":123,"key":"ci-cache","ref":"refs/heads/main","size_in_bytes":1048576,"last_used_at":"2026-01-01T00:00:00Z"}]}""",
        };

        var result = await _handler.GhCacheListAsync(repo: "owner/repo");

        result.IsError.Should().BeFalse();
        _api.LastMethod.Should().Be(HttpMethod.Get);
        _api.LastPath.Should().Be("repos/owner/repo/actions/caches");
        result.GetFirstText().Should().Contain("123");
        result.GetFirstText().Should().Contain("ci-cache");
    }

    [Fact]
    public async Task CacheList_WithBranchFilter_PassesRefQuery() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"total_count":0,"actions_caches":[]}""" };

        await _handler.GhCacheListAsync(branch: "develop", repo: "owner/repo");

        _api.LastQuery.Should().ContainKey("ref").WhoseValue.Should().Be("refs/heads/develop");
    }

    [Fact]
    public async Task CacheList_WithKeyFilter_PassesKeyQuery() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"total_count":0,"actions_caches":[]}""" };

        await _handler.GhCacheListAsync(key: "linux", repo: "owner/repo");

        _api.LastQuery.Should().ContainKey("key").WhoseValue.Should().Be("linux");
    }

    [Fact]
    public async Task CacheDelete_ById_CallsDeleteEndpoint() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 204, Body = "" };

        var result = await _handler.GhCacheDeleteAsync(cache_id: 123, repo: "owner/repo");

        result.IsError.Should().BeFalse();
        _api.LastMethod.Should().Be(HttpMethod.Delete);
        _api.LastPath.Should().Be("repos/owner/repo/actions/caches/123");
    }

    [Fact]
    public async Task CacheDeleteAll_ListsThenDeletesEachCache() {
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"total_count":2,"actions_caches":[{"id":111,"key":"a"},{"id":222,"key":"b"}]}""",
        });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 204, Body = "" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 204, Body = "" });

        var result = await _handler.GhCacheDeleteAsync(all: true, repo: "owner/repo");

        result.IsError.Should().BeFalse();
        _api.LastMethod.Should().Be(HttpMethod.Delete);
        result.GetFirstText().Should().Contain("已删除 2");
    }

    [Fact]
    public async Task CacheDelete_NoIdAndNoAll_ReturnsError() {
        var result = await _handler.GhCacheDeleteAsync(repo: "owner/repo");

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("cache_id");
    }

    // === Ruleset ===

    [Fact]
    public async Task RulesetList_ListsRepoRulesets() {
        _api.NextResponse = new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """[{"id":1,"name":"branch-rules","target":"branch","enforcement":"active"}]""",
        };

        var result = await _handler.GhRulesetListAsync(repo: "owner/repo");

        result.IsError.Should().BeFalse();
        _api.LastMethod.Should().Be(HttpMethod.Get);
        _api.LastPath.Should().Be("repos/owner/repo/rulesets");
        result.GetFirstText().Should().Contain("branch-rules");
    }

    [Fact]
    public async Task RulesetList_WithOrg_ListsOrgRulesets() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = "[]" };

        await _handler.GhRulesetListAsync(org: "myorg", repo: "owner/repo");

        _api.LastPath.Should().Be("orgs/myorg/rulesets");
    }

    [Fact]
    public async Task RulesetView_ReturnsRulesetDetail() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"id":42,"name":"rules"}""" };

        var result = await _handler.GhRulesetViewAsync(42, repo: "owner/repo");

        result.IsError.Should().BeFalse();
        _api.LastMethod.Should().Be(HttpMethod.Get);
        _api.LastPath.Should().Be("repos/owner/repo/rulesets/42");
    }

    [Fact]
    public async Task RulesetView_WithOrg_UsesOrgPath() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"id":42}""" };

        await _handler.GhRulesetViewAsync(42, org: "myorg", repo: "owner/repo");

        _api.LastPath.Should().Be("orgs/myorg/rulesets/42");
    }

    [Fact]
    public async Task RulesetCheck_ChecksBranchRules() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """[{"id":1,"name":"r"}]""" };

        var result = await _handler.GhRulesetCheckAsync("main", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        _api.LastMethod.Should().Be(HttpMethod.Get);
        _api.LastPath.Should().Be("repos/owner/repo/rulesets-rs/main");
    }

    // === Status ===

    [Fact]
    public async Task Status_AggregatesAssignedIssuesPrsReviewsMentions() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"total_count":0,"items":[]}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"total_count":0,"items":[]}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"total_count":0,"items":[]}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"total_count":0,"items":[]}""" });

        var result = await _handler.GhStatusAsync();

        result.IsError.Should().BeFalse();
        _api.LastPath.Should().Be("search/issues");
        var text = result.GetFirstText();
        text.Should().Contain("指派的 Issue");
        text.Should().Contain("指派的 PR");
        text.Should().Contain("审查请求");
        text.Should().Contain("提及");
    }

    [Fact]
    public async Task Status_WithOrg_PassesOrgFilter() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"total_count":0,"items":[]}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"total_count":0,"items":[]}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"total_count":0,"items":[]}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"total_count":0,"items":[]}""" });

        await _handler.GhStatusAsync(org: "myorg");

        _api.LastQuery.Should().ContainKey("q").WhoseValue.Should().Contain("org:myorg");
    }

    // === Codespace ===

    [Fact]
    public async Task CodespaceList_ListsUserCodespaces() {
        _api.NextResponse = new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"codespaces":[{"name":"cs1","display_name":"test","repository":{"full_name":"owner/repo"},"state":"Available","git_status":{"ref":"main"}}]}""",
        };

        var result = await _handler.GhCodespaceListAsync();

        result.IsError.Should().BeFalse();
        _api.LastMethod.Should().Be(HttpMethod.Get);
        _api.LastPath.Should().Be("user/codespaces");
        result.GetFirstText().Should().Contain("cs1");
    }

    [Fact]
    public async Task CodespaceList_WithRepo_ListsRepoCodespaces() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"codespaces":[]}""" };

        await _handler.GhCodespaceListAsync(repo: "owner/repo");

        _api.LastPath.Should().Be("repos/owner/repo/codespaces");
    }

    [Fact]
    public async Task CodespaceCreate_GetsRepoIdThenPostsCodespace() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"id":12345,"full_name":"owner/repo"}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 201, Body = """{"name":"cs-new","html_url":"https://github.com/codespaces/cs-new"}""" });

        var result = await _handler.GhCodespaceCreateAsync("owner/repo", branch: "main");

        result.IsError.Should().BeFalse();
        _api.LastMethod.Should().Be(HttpMethod.Post);
        _api.LastPath.Should().Be("user/codespaces");
        _api.LastBody.Should().Contain("repository_id");
        _api.LastBody.Should().Contain("12345");
    }

    [Fact]
    public async Task CodespaceCreate_WithMachine_IncludesMachineInBody() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"id":99}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 201, Body = """{"name":"cs"}""" });

        await _handler.GhCodespaceCreateAsync("owner/repo", machine: "basicLinux32gb");

        _api.LastBody.Should().Contain("basicLinux32gb");
    }

    [Fact]
    public async Task CodespaceDelete_DeletesCodespaceByName() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 204, Body = "" };

        var result = await _handler.GhCodespaceDeleteAsync("my-codespace");

        result.IsError.Should().BeFalse();
        _api.LastMethod.Should().Be(HttpMethod.Delete);
        _api.LastPath.Should().Be("user/codespaces/my-codespace");
    }

    [Fact]
    public async Task CodespaceCode_ReturnsPromptToUseGhCli() {
        var result = await _handler.GhCodespaceCodeAsync("my-codespace");

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("gh cs code");
        result.GetFirstText().Should().Contain("my-codespace");
    }

    [Fact]
    public async Task CodespaceCode_WithoutName_ReturnsPromptWithoutName() {
        var result = await _handler.GhCodespaceCodeAsync();

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("gh cs code");
    }

    [Fact]
    public async Task CodespaceSsh_ReturnsPromptToUseGhCli() {
        var result = await _handler.GhCodespaceSshAsync("my-codespace");

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("gh cs ssh");
        result.GetFirstText().Should().Contain("my-codespace");
    }

    // === Discussion (GraphQL) ===

    [Fact]
    public async Task DiscussionList_PostsGraphQLQuery() {
        _api.NextResponse = new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"data":{"repository":{"discussions":{"nodes":[{"number":1,"title":"test","author":{"login":"alice"},"category":{"name":"General"},"createdAt":"2026-01-01T00:00:00Z"}]}}}}""",
        };

        var result = await _handler.GhDiscussionListAsync(repo: "owner/repo");

        result.IsError.Should().BeFalse();
        _api.LastMethod.Should().Be(HttpMethod.Post);
        _api.LastPath.Should().Be("graphql");
        _api.LastBody.Should().Contain("discussions");
        _api.LastBody.Should().Contain("repository");
        result.GetFirstText().Should().Contain("test");
    }

    [Fact]
    public async Task DiscussionView_PostsGraphQLQuery() {
        _api.NextResponse = new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"data":{"repository":{"discussion":{"number":1,"title":"test","body":"content","author":{"login":"alice"},"category":{"name":"General"},"createdAt":"2026-01-01","url":"https://github.com/owner/repo/discussions/1"}}}}""",
        };

        var result = await _handler.GhDiscussionViewAsync(1, repo: "owner/repo");

        result.IsError.Should().BeFalse();
        _api.LastPath.Should().Be("graphql");
        _api.LastBody.Should().Contain("discussion(number:1)");
    }

    [Fact]
    public async Task DiscussionCreate_MultiStepGraphQL_PostsMutation() {
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"data":{"repository":{"discussionCategories":{"nodes":[{"id":"CAT_1","name":"General"}]}}}}""",
        });
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"data":{"repository":{"id":"REPO_1"}}}""",
        });
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"data":{"createDiscussion":{"discussion":{"number":10,"url":"https://github.com/owner/repo/discussions/10"}}}}""",
        });

        var result = await _handler.GhDiscussionCreateAsync("新讨论", "正文内容", "General", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        _api.LastPath.Should().Be("graphql");
        _api.LastBody.Should().Contain("createDiscussion");
        _api.LastBody.Should().Contain("REPO_1");
        _api.LastBody.Should().Contain("CAT_1");
    }

    [Fact]
    public async Task DiscussionEdit_MultiStepGraphQL_PostsUpdateMutation() {
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"data":{"repository":{"discussion":{"id":"DISC_1"}}}}""",
        });
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"data":{"updateDiscussion":{"discussion":{"number":5,"url":"https://github.com/owner/repo/discussions/5"}}}}""",
        });

        var result = await _handler.GhDiscussionEditAsync(5, title: "新标题", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        _api.LastPath.Should().Be("graphql");
        _api.LastBody.Should().Contain("updateDiscussion");
        _api.LastBody.Should().Contain("DISC_1");
    }

    [Fact]
    public async Task DiscussionComment_MultiStepGraphQL_PostsCommentMutation() {
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"data":{"repository":{"discussion":{"id":"DISC_2"}}}}""",
        });
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"data":{"addDiscussionComment":{"comment":{"id":"C_1"}}}}""",
        });

        var result = await _handler.GhDiscussionCommentAsync(7, "评论内容", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        _api.LastPath.Should().Be("graphql");
        _api.LastBody.Should().Contain("addDiscussionComment");
        _api.LastBody.Should().Contain("DISC_2");
    }

    // === Project (GraphQL v2) ===

    [Fact]
    public async Task ProjectList_PostsGraphQLQuery() {
        _api.NextResponse = new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"data":{"viewer":{"projectsV2":{"nodes":[{"number":1,"title":"My Project","state":"open","url":"https://github.com/users/me/projects/1"}]}}}}""",
        };

        var result = await _handler.GhProjectListAsync();

        result.IsError.Should().BeFalse();
        _api.LastMethod.Should().Be(HttpMethod.Post);
        _api.LastPath.Should().Be("graphql");
        _api.LastBody.Should().Contain("projectsV2");
        _api.LastBody.Should().Contain("viewer");
        result.GetFirstText().Should().Contain("My Project");
    }

    [Fact]
    public async Task ProjectList_WithOrg_UsesOrganizationQuery() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"data":{"organization":{"projectsV2":{"nodes":[]}}}}""" };

        await _handler.GhProjectListAsync(org: "myorg");

        _api.LastBody.Should().Contain("organization");
        _api.LastBody.Should().Contain("myorg");
    }

    [Fact]
    public async Task ProjectView_PostsGraphQLQuery() {
        _api.NextResponse = new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"data":{"viewer":{"projectV2":{"title":"P","url":"https://github.com/users/me/projects/1","closed":false,"state":"open","items":{"nodes":[]}}}}}""",
        };

        var result = await _handler.GhProjectViewAsync(1);

        result.IsError.Should().BeFalse();
        _api.LastPath.Should().Be("graphql");
        _api.LastBody.Should().Contain("projectV2(number:1)");
    }

    [Fact]
    public async Task ProjectView_WithOrg_UsesOrganizationQuery() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"data":{"organization":{"projectV2":{"title":"P","url":"","closed":false,"state":"open","items":{"nodes":[]}}}}}""" };

        await _handler.GhProjectViewAsync(1, org: "myorg");

        _api.LastBody.Should().Contain("organization");
    }

    [Fact]
    public async Task ProjectCreate_WithOrg_PostsMutationDirectly() {
        _api.NextResponse = new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"data":{"createProjectV2":{"projectV2":{"number":2,"url":"https://github.com/orgs/myorg/projects/2"}}}}""",
        };

        var result = await _handler.GhProjectCreateAsync("新项目", org: "myorg");

        result.IsError.Should().BeFalse();
        _api.LastPath.Should().Be("graphql");
        _api.LastBody.Should().Contain("createProjectV2");
        _api.LastBody.Should().Contain("myorg");
    }

    [Fact]
    public async Task ProjectCreate_ForUser_GetsViewerIdThenPostsMutation() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"data":{"viewer":{"id":"USER_1"}}}""" });
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"data":{"createProjectV2":{"projectV2":{"number":3,"url":"https://github.com/users/me/projects/3"}}}}""",
        });

        var result = await _handler.GhProjectCreateAsync("用户项目");

        result.IsError.Should().BeFalse();
        _api.LastPath.Should().Be("graphql");
        _api.LastBody.Should().Contain("createProjectV2");
        _api.LastBody.Should().Contain("USER_1");
    }

    [Fact]
    public async Task ProjectDelete_MultiStepGraphQL_PostsDeleteMutation() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"data":{"viewer":{"projectV2":{"id":"PROJ_1"}}}}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"data":{"deleteProjectV2":{"projectV2":{"number":4}}}}""" });

        var result = await _handler.GhProjectDeleteAsync(4);

        result.IsError.Should().BeFalse();
        _api.LastPath.Should().Be("graphql");
        _api.LastBody.Should().Contain("deleteProjectV2");
        _api.LastBody.Should().Contain("PROJ_1");
    }

    [Fact]
    public async Task ProjectEdit_MultiStepGraphQL_PostsUpdateMutation() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"data":{"viewer":{"projectV2":{"id":"PROJ_2"}}}}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"data":{"updateProjectV2":{"projectV2":{"number":5,"url":"https://github.com/users/me/projects/5"}}}}""" });

        var result = await _handler.GhProjectEditAsync(5, title: "更新标题");

        result.IsError.Should().BeFalse();
        _api.LastPath.Should().Be("graphql");
        _api.LastBody.Should().Contain("updateProjectV2");
        _api.LastBody.Should().Contain("PROJ_2");
    }

    [Fact]
    public async Task ProjectClose_MultiStepGraphQL_PostsCloseMutation() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"data":{"viewer":{"projectV2":{"id":"PROJ_3"}}}}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"data":{"updateProjectV2":{"projectV2":{"number":6,"url":"https://github.com/users/me/projects/6"}}}}""" });

        var result = await _handler.GhProjectCloseAsync(6);

        result.IsError.Should().BeFalse();
        _api.LastPath.Should().Be("graphql");
        _api.LastBody.Should().Contain("updateProjectV2");
        _api.LastBody.Should().Contain("closed:true");
        _api.LastBody.Should().Contain("PROJ_3");
    }

    // === Alias (真实读写 config.yml，测试在 GitHubToolHandlersConfigTests.cs) ===

    // === Extension (纯提示) ===

    [Fact]
    public async Task ExtensionList_ReturnsPromptToUseGhCli() {
        var result = await _handler.GhExtensionListAsync();

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("gh extension list");
    }

    [Fact]
    public async Task ExtensionInstall_ReturnsPromptWithExtensionName() {
        var result = await _handler.GhExtensionInstallAsync("owner/gh-ext");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().Contain("gh extension install");
        text.Should().Contain("owner/gh-ext");
    }

    [Fact]
    public async Task ExtensionUpgrade_WithoutName_IncludesAllFlag() {
        var result = await _handler.GhExtensionUpgradeAsync();

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().Contain("gh extension upgrade");
        text.Should().Contain("--all");
    }

    [Fact]
    public async Task ExtensionUpgrade_WithName_IncludesExtensionName() {
        var result = await _handler.GhExtensionUpgradeAsync("owner/gh-ext");

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("owner/gh-ext");
    }

    [Fact]
    public async Task ExtensionRemove_ReturnsPromptWithExtensionName() {
        var result = await _handler.GhExtensionRemoveAsync("owner/gh-ext");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().Contain("gh extension remove");
        text.Should().Contain("owner/gh-ext");
    }

    // === Licenses (纯提示) ===

    [Fact]
    public async Task Licenses_ReturnsPromptToUseGhCli() {
        var result = await _handler.GhLicensesAsync();

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("gh licenses");
    }

    // === Browse 边缘场景 ===

    [Fact]
    public async Task Browse_WithBlameAndTarget_ReturnsBlameUrl() {
        var result = await _handler.GhBrowseAsync(target: "src/file.cs", blame: true, repo: "owner/repo", no_browser: true);

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("/blame/HEAD/src/file.cs");
    }

    [Fact]
    public async Task Browse_WithCommitSha_ReturnsCommitUrl() {
        var sha = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2";
        var result = await _handler.GhBrowseAsync(target: sha, repo: "owner/repo", no_browser: true);

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain($"/commit/{sha}");
    }

    [Fact]
    public async Task Browse_WithProjects_ReturnsProjectsUrl() {
        var result = await _handler.GhBrowseAsync(projects: true, repo: "owner/repo", no_browser: true);

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("/projects");
    }

    // === Cache 边缘场景 ===

    [Fact]
    public async Task CacheDelete_AllWithEmptyList_ReturnsNoCacheMessage() {
        _api.NextResponse = new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"total_count":0}""",
        };

        var result = await _handler.GhCacheDeleteAsync(all: true, repo: "owner/repo");

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("无缓存可删除");
    }

    [Fact]
    public async Task CacheDelete_WithZeroId_ReturnsError() {
        var result = await _handler.GhCacheDeleteAsync(cache_id: 0, repo: "owner/repo");

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("cache_id");
    }

    // === Discussion 边缘场景 ===

    [Fact]
    public async Task DiscussionCreate_CategoryNotFound_ReturnsError() {
        _api.NextResponse = new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"data":{"repository":{"discussionCategories":{"nodes":[{"id":"CAT_1","name":"General"}]}}}}""",
        };

        var result = await _handler.GhDiscussionCreateAsync("标题", "正文", "NonExistent", repo: "owner/repo");

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("未找到分类");
        result.GetFirstText().Should().Contain("NonExistent");
    }

    [Fact]
    public async Task DiscussionCreate_RepoIdQueryFails_ReturnsError() {
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"data":{"repository":{"discussionCategories":{"nodes":[{"id":"CAT_1","name":"General"}]}}}}""",
        });
        _api.EnqueueResponse(new GitHubApiResponse { Success = false, StatusCode = 404, Error = "仓库不存在" });

        var result = await _handler.GhDiscussionCreateAsync("标题", "正文", "General", repo: "owner/repo");

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("仓库不存在");
    }

    [Fact]
    public async Task DiscussionEdit_NoTitleAndNoBody_ReturnsError() {
        _api.NextResponse = new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"data":{"repository":{"discussion":{"id":"DISC_1"}}}}""",
        };

        var result = await _handler.GhDiscussionEditAsync(5, repo: "owner/repo");

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("需要 title 或 body");
    }

    [Fact]
    public async Task DiscussionEdit_IdQueryFails_ReturnsError() {
        _api.NextResponse = new GitHubApiResponse { Success = false, StatusCode = 404, Error = "Discussion 不存在" };

        var result = await _handler.GhDiscussionEditAsync(5, title: "新标题", repo: "owner/repo");

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("Discussion 不存在");
    }

    [Fact]
    public async Task DiscussionComment_IdQueryFails_ReturnsError() {
        _api.NextResponse = new GitHubApiResponse { Success = false, StatusCode = 404, Error = "Discussion 不存在" };

        var result = await _handler.GhDiscussionCommentAsync(7, "评论内容", repo: "owner/repo");

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("Discussion 不存在");
    }

    // === Project 边缘场景 ===

    [Fact]
    public async Task ProjectEdit_NoTitleAndNoDescription_ReturnsError() {
        _api.NextResponse = new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"data":{"viewer":{"projectV2":{"id":"PROJ_1"}}}}""",
        };

        var result = await _handler.GhProjectEditAsync(5);

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("需要 title 或 description");
    }

    [Fact]
    public async Task ProjectEdit_IdQueryFails_ReturnsError() {
        _api.NextResponse = new GitHubApiResponse { Success = false, StatusCode = 404, Error = "Project 不存在" };

        var result = await _handler.GhProjectEditAsync(5, title: "新标题");

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("Project 不存在");
    }

    [Fact]
    public async Task ProjectCreate_ViewerIdQueryFails_ReturnsError() {
        _api.NextResponse = new GitHubApiResponse { Success = false, StatusCode = 404, Error = "Viewer 查询失败" };

        var result = await _handler.GhProjectCreateAsync("用户项目");

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("Viewer 查询失败");
    }

    // === Codespace 边缘场景 ===

    [Fact]
    public async Task CodespaceCreate_InvalidRepoFormat_ReturnsError() {
        var result = await _handler.GhCodespaceCreateAsync("invalid-repo-no-slash");

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("仓库名格式错误");
    }

    [Fact]
    public async Task CodespaceCreate_RepoNotFound_ReturnsError() {
        _api.NextResponse = new GitHubApiResponse { Success = false, StatusCode = 404, Error = "仓库不存在" };

        var result = await _handler.GhCodespaceCreateAsync("owner/repo");

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("仓库不存在");
    }

    // === Ruleset 边缘场景 ===

    [Fact]
    public async Task RulesetList_NoOrgNoRepo_ReturnsRepoNotResolvedError() {
        var result = await _handler.GhRulesetListAsync();

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("无法解析仓库");
    }

    // === Status 边缘场景 ===

    [Fact]
    public async Task Status_ApiClientNull_ReturnsApiClientNotConfigured() {
        var handlerWithoutApi = new GitHubToolHandlers(
            new FakeDownloader(),
            new InMemoryFileSystem(),
            new PersistencePipeline(new InMemoryFileSystem()),
            null,
            null,
            NullLogger<GitHubToolHandlers>.Instance);

        var result = await handlerWithoutApi.GhStatusAsync();

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("GitHub REST API 客户端未配置");
    }
}
