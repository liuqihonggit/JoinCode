namespace Mcp.Tests;

public sealed partial class GitHubToolHandlersTests {
    [Fact]
    public async Task RepoEdit_WithEnableDiscussions_IncludesHasDiscussions() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"full_name":"o/r"}""" };

        await _handler.GhRepoEditAsync(enable_discussions: true, common: new GitHubCommonOptions { Repo = "owner/repo" });

        _api.LastBody.Should().Contain("\"has_discussions\":true");
    }

    [Fact]
    public async Task RepoEdit_WithEnableSquashMerge_IncludesAllowSquashMerge() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"full_name":"o/r"}""" };

        await _handler.GhRepoEditAsync(enable_squash_merge: true, common: new GitHubCommonOptions { Repo = "owner/repo" });

        _api.LastBody.Should().Contain("\"allow_squash_merge\":true");
    }

    [Fact]
    public async Task RepoEdit_WithEnableAutoMerge_IncludesAllowAutoMerge() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"full_name":"o/r"}""" };

        await _handler.GhRepoEditAsync(enable_auto_merge: true, common: new GitHubCommonOptions { Repo = "owner/repo" });

        _api.LastBody.Should().Contain("\"allow_auto_merge\":true");
    }

    [Fact]
    public async Task RepoEdit_WithAllowForking_IncludesAllowForking() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"full_name":"o/r"}""" };

        await _handler.GhRepoEditAsync(allow_forking: true, common: new GitHubCommonOptions { Repo = "owner/repo" });

        _api.LastBody.Should().Contain("\"allow_forking\":true");
    }

    [Fact]
    public async Task RepoEdit_WithTemplate_IncludesIsTemplate() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"full_name":"o/r"}""" };

        await _handler.GhRepoEditAsync(template: true, common: new GitHubCommonOptions { Repo = "owner/repo" });

        _api.LastBody.Should().Contain("\"is_template\":true");
    }

    [Fact]
    public async Task RepoEdit_WithSquashMergeCommitMessage_IncludesSquashPrCommitMessage() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"full_name":"o/r"}""" };

        await _handler.GhRepoEditAsync(squash_merge_commit_message: "pr-title", common: new GitHubCommonOptions { Repo = "owner/repo" });

        _api.LastBody.Should().Contain("\"squash_pr_commit_message\":\"pr-title\"");
    }

    [Fact]
    public async Task RepoEdit_WithEnableAdvancedSecurity_IncludesSecurityAndAnalysis() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"full_name":"o/r"}""" };

        await _handler.GhRepoEditAsync(enable_advanced_security: true, common: new GitHubCommonOptions { Repo = "owner/repo" });

        _api.LastBody.Should().Contain("\"security_and_analysis\"");
        _api.LastBody.Should().Contain("\"advanced_security\"");
        _api.LastBody.Should().Contain("\"status\":\"enabled\"");
    }

    [Fact]
    public async Task RepoEdit_WithDisableSecretScanning_IncludesDisabledStatus() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"full_name":"o/r"}""" };

        await _handler.GhRepoEditAsync(enable_secret_scanning: false, common: new GitHubCommonOptions { Repo = "owner/repo" });

        _api.LastBody.Should().Contain("\"secret_scanning\"");
        _api.LastBody.Should().Contain("\"status\":\"disabled\"");
    }

    [Fact]
    public async Task RepoEdit_WithAddTopic_CallsTopicsApi() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"full_name":"o/r"}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"names":["existing"]}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"names":["existing","new"]}""" });

        var result = await _handler.GhRepoEditAsync(add_topic: "new", common: new GitHubCommonOptions { Repo = "owner/repo" });

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("topics 已更新");
    }

    [Fact]
    public async Task RepoEdit_WithRemoveTopic_CallsTopicsApi() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"full_name":"o/r"}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"names":["keep","remove"]}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"names":["keep"]}""" });

        var result = await _handler.GhRepoEditAsync(remove_topic: "remove", common: new GitHubCommonOptions { Repo = "owner/repo" });

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("topics 已更新");
    }

    [Fact]
    public async Task RepoEdit_WithAllowUpdateBranch_IncludesAllowUpdateBranch() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"full_name":"o/r"}""" };

        await _handler.GhRepoEditAsync(allow_update_branch: true, common: new GitHubCommonOptions { Repo = "owner/repo" });

        _api.LastBody.Should().Contain("\"allow_update_branch\":true");
    }
}
