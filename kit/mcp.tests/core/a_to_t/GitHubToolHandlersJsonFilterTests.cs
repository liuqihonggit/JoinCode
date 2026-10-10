// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC11003
namespace Mcp.Tests;

/// <summary>
/// --json 精确字段选择测试 — 验证 FilterJsonFields 在各命令中的行为
/// </summary>
public sealed partial class GitHubToolHandlersTests {
    [Fact]
    public async Task PrList_WithJson_ReturnsFilteredFields() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """[{"number":1,"title":"bug","state":"open","body":"text","url":"https://x"}]""" };

        var result = await _handler.GhPrListAsync(common: new GitHubCommonOptions { JsonFields = "number,title", Repo = "owner/repo" });

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("\"number\":1");
        text.Should().Contain("\"title\":\"bug\"");
        text.Should().NotContain("\"state\"");
        text.Should().NotContain("\"body\"");
    }

    [Fact]
    public async Task PrList_WithJson_SearchApi_FiltersItemsArray() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"total_count":1,"items":[{"number":2,"title":"feat","state":"closed","body":"x"}]}""" };

        var result = await _handler.GhPrListAsync(search: "review:required", common: new GitHubCommonOptions { JsonFields = "number,title", Repo = "owner/repo" });

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("\"number\":2");
        text.Should().Contain("\"title\":\"feat\"");
        text.Should().NotContain("\"state\"");
    }

    [Fact]
    public async Task IssueList_WithJson_ReturnsFilteredFields() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """[{"number":5,"title":"issue","state":"open","body":"desc"}]""" };

        var result = await _handler.GhIssueListAsync(common: new GitHubCommonOptions { JsonFields = "number,title", Repo = "owner/repo" });

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("\"number\":5");
        text.Should().Contain("\"title\":\"issue\"");
        text.Should().NotContain("\"state\"");
    }

    [Fact]
    public async Task RunList_WithJson_ReturnsFilteredFields() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"workflow_runs":[{"id":123,"status":"completed","conclusion":"success","head_sha":"abc"}]}""" };

        var result = await _handler.GhRunListAsync(common: new GitHubCommonOptions { JsonFields = "id,status", Repo = "owner/repo" });

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("\"id\":123");
        text.Should().Contain("\"status\":\"completed\"");
        text.Should().NotContain("\"conclusion\"");
    }

    [Fact]
    public async Task ReleaseList_WithJson_ReturnsFilteredFields() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """[{"id":1,"tag_name":"v1","name":"Release 1","draft":false}]""" };

        var result = await _handler.GhReleaseListAsync(common: new GitHubCommonOptions { JsonFields = "id,tag_name", Repo = "owner/repo" });

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("\"id\":1");
        text.Should().Contain("\"tag_name\":\"v1\"");
        text.Should().NotContain("\"name\"");
    }

    [Fact]
    public async Task LabelList_WithJson_ReturnsFilteredFields() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """[{"id":1,"name":"bug","color":"d73a4a","description":"Bug"}]""" };

        var result = await _handler.GhLabelListAsync(common: new GitHubCommonOptions { JsonFields = "id,name", Repo = "owner/repo" });

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("\"id\":1");
        text.Should().Contain("\"name\":\"bug\"");
        text.Should().NotContain("\"color\"");
    }

    [Fact]
    public async Task WorkflowList_WithJson_ReturnsFilteredFields() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"workflows":[{"id":42,"name":"CI","state":"active","path":".github/workflows/ci.yml"}]}""" };

        var result = await _handler.GhWorkflowListAsync(common: new GitHubCommonOptions { JsonFields = "id,name", Repo = "owner/repo" });

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("\"id\":42");
        text.Should().Contain("\"name\":\"CI\"");
        text.Should().NotContain("\"state\"");
    }

    [Fact]
    public async Task PrView_WithJson_ReturnsFilteredFields() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"number":7,"title":"pr","state":"open","body":"text","mergeable":true}""" };

        var result = await _handler.GhPrViewAsync("7", common: new GitHubCommonOptions { JsonFields = "number,title", Repo = "owner/repo" });

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("\"number\":7");
        text.Should().Contain("\"title\":\"pr\"");
        text.Should().NotContain("\"state\"");
    }

    /// <summary>gh CLI 平铺字段别名: headRefName→head.ref, headRefOid→head.sha, baseRefName→base.ref, baseRefOid→base.sha</summary>
    [Fact]
    public async Task PrView_WithJson_HeadRefName_FlattensNestedField() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"number":7,"title":"pr","state":"open","head":{"ref":"feature-branch","sha":"abc123"},"base":{"ref":"main","sha":"def456"}}""" };

        var result = await _handler.GhPrViewAsync("7", common: new GitHubCommonOptions { JsonFields = "headRefName,headRefOid,baseRefName,baseRefOid", Repo = "owner/repo" });

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("\"headRefName\":\"feature-branch\"");
        text.Should().Contain("\"headRefOid\":\"abc123\"");
        text.Should().Contain("\"baseRefName\":\"main\"");
        text.Should().Contain("\"baseRefOid\":\"def456\"");
    }

    [Fact]
    public async Task IssueView_WithJson_ReturnsFilteredFields() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"number":3,"title":"issue","state":"open","body":"desc"}""" };

        var result = await _handler.GhIssueViewAsync("3", common: new GitHubCommonOptions { Repo = "owner/repo", JsonFields = "number,title" });

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("\"number\":3");
        text.Should().Contain("\"title\":\"issue\"");
        text.Should().NotContain("\"state\"");
    }

    [Fact]
    public async Task RepoView_WithJson_ReturnsFilteredFields() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"name":"repo","full_name":"owner/repo","description":"test","private":false}""" };

        var result = await _handler.GhRepoViewAsync(common: new GitHubCommonOptions { JsonFields = "name,full_name", Repo = "owner/repo" });

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("\"name\":\"repo\"");
        text.Should().Contain("\"full_name\":\"owner/repo\"");
        text.Should().NotContain("\"description\"");
    }

    [Fact]
    public async Task RepoList_WithJson_ReturnsCustomFields() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """[{"name":"r1","full_name":"o/r1","language":"C#","fork":false}]""" };

        var result = await _handler.GhRepoListAsync(common: new GitHubCommonOptions { Repo = "owner/repo", JsonFields = "name,language" });

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("\"name\":\"r1\"");
        text.Should().Contain("\"language\":\"C#\"");
        text.Should().NotContain("\"full_name\"");
    }

    [Fact]
    public async Task SecretList_WithJson_ReturnsFilteredFields() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"total_count":1,"secrets":[{"name":"K","created_at":"2026-01-01"}]}""" };

        var result = await _handler.GhSecretListAsync(common: new GitHubCommonOptions { JsonFields = "name", Repo = "owner/repo" });

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("\"name\":\"K\"");
        text.Should().NotContain("\"created_at\"");
    }

    [Fact]
    public async Task VariableList_WithJson_ReturnsFilteredFields() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"total_count":1,"variables":[{"name":"V","value":"x","updated_at":"2026-01-01"}]}""" };

        var result = await _handler.GhVariableListAsync(common: new GitHubCommonOptions { JsonFields = "name,value", Repo = "owner/repo" });

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("\"name\":\"V\"");
        text.Should().Contain("\"value\":\"x\"");
        text.Should().NotContain("\"updated_at\"");
    }

    [Fact]
    public async Task OrgList_WithJson_ReturnsFilteredFields() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """[{"login":"org","description":"d"}]""" };

        var result = await _handler.GhOrgListAsync(common: new GitHubCommonOptions { JsonFields = "login" });

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("\"login\":\"org\"");
        text.Should().NotContain("\"description\"");
    }

    [Fact]
    public async Task SshKeyList_WithJson_ReturnsFilteredFields() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """[{"id":1,"title":"k","key":"ssh-..."}]""" };

        var result = await _handler.GhSshKeyListAsync(common: new GitHubCommonOptions { JsonFields = "id,title" });

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("\"id\":1");
        text.Should().Contain("\"title\":\"k\"");
        text.Should().NotContain("\"key\"");
    }

    [Fact]
    public async Task SearchRepos_WithJson_ReturnsFilteredFields() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"total_count":1,"items":[{"full_name":"o/r","stargazers_count":5,"description":"d"}]}""" };

        var result = await _handler.GhSearchReposAsync(query: "stars:>1", common: new GitHubCommonOptions { JsonFields = "full_name" });

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("\"full_name\":\"o/r\"");
        text.Should().NotContain("\"stargazers_count\"");
    }

    [Fact]
    public async Task GistList_WithJson_ReturnsFilteredFields() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """[{"id":"abc","description":"d","public":false}]""" };

        var result = await _handler.GhGistListAsync(common: new GitHubCommonOptions { JsonFields = "id" });

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("\"id\":\"abc\"");
        text.Should().NotContain("\"description\"");
    }

    [Fact]
    public async Task RunView_WithJson_ReturnsFilteredFields() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"id":123}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"id":123,"status":"completed","conclusion":"success","head_sha":"abc"}""" });

        var result = await _handler.GhRunViewAsync("123", common: new GitHubCommonOptions { JsonFields = "id,status", Repo = "owner/repo" });

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("\"id\":123");
        text.Should().Contain("\"status\":\"completed\"");
        text.Should().NotContain("\"conclusion\"");
    }

    /// <summary>expand=jobs + json_fields 应对 job 列表过滤字段,而非 run 本身</summary>
    [Fact]
    public async Task RunView_ExpandJobs_WithJson_FiltersJobFields() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"id":123}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"total_count":2,"jobs":[{"name":"build","conclusion":"success","id":1},{"name":"test","conclusion":"failure","id":2}]}""" });

        var result = await _handler.GhRunViewAsync("123", expand: "jobs", common: new GitHubCommonOptions { JsonFields = "name,conclusion", Repo = "owner/repo" });

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("\"name\":\"build\"");
        text.Should().Contain("\"name\":\"test\"");
        text.Should().Contain("\"conclusion\":\"failure\"");
    }

    [Fact]
    public async Task ReleaseView_WithJson_ReturnsFilteredFields() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"id":1,"tag_name":"v1","name":"Release 1","draft":false}""" };

        var result = await _handler.GhReleaseViewAsync("v1", common: new GitHubCommonOptions { JsonFields = "id,tag_name", Repo = "owner/repo" });

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("\"id\":1");
        text.Should().Contain("\"tag_name\":\"v1\"");
        text.Should().NotContain("\"name\"");
    }

    [Fact]
    public async Task WorkflowView_WithJson_ReturnsFilteredFields() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"id":42,"name":"CI","state":"active","path":".github/workflows/ci.yml"}""" };

        var result = await _handler.GhWorkflowViewAsync("ci.yml", common: new GitHubCommonOptions { JsonFields = "id,name", Repo = "owner/repo" });

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("\"id\":42");
        text.Should().Contain("\"name\":\"CI\"");
        text.Should().NotContain("\"state\"");
    }

    [Fact]
    public async Task GistView_WithJson_ReturnsFilteredFields() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"id":"abc","description":"d","public":false}""" };

        var result = await _handler.GhGistViewAsync("abc", common: new GitHubCommonOptions { JsonFields = "id" });

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("\"id\":\"abc\"");
        text.Should().NotContain("\"description\"");
    }
}
