namespace Mcp.Tests;

/// <summary>
/// --json 精确字段选择测试 — 验证 FilterJsonFields 在各命令中的行为
/// </summary>
public sealed partial class GitHubToolHandlersTests {
    [Fact]
    public async Task PrList_WithJson_ReturnsFilteredFields() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """[{"number":1,"title":"bug","state":"open","body":"text","url":"https://x"}]""" };

        var result = await _handler.GhPrListAsync(json_fields: "number,title", repo: "owner/repo");

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

        var result = await _handler.GhPrListAsync(search: "review:required", json_fields: "number,title", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("\"number\":2");
        text.Should().Contain("\"title\":\"feat\"");
        text.Should().NotContain("\"state\"");
    }

    [Fact]
    public async Task IssueList_WithJson_ReturnsFilteredFields() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """[{"number":5,"title":"issue","state":"open","body":"desc"}]""" };

        var result = await _handler.GhIssueListAsync(json_fields: "number,title", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("\"number\":5");
        text.Should().Contain("\"title\":\"issue\"");
        text.Should().NotContain("\"state\"");
    }

    [Fact]
    public async Task RunList_WithJson_ReturnsFilteredFields() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"workflow_runs":[{"id":123,"status":"completed","conclusion":"success","head_sha":"abc"}]}""" };

        var result = await _handler.GhRunListAsync(json_fields: "id,status", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("\"id\":123");
        text.Should().Contain("\"status\":\"completed\"");
        text.Should().NotContain("\"conclusion\"");
    }

    [Fact]
    public async Task ReleaseList_WithJson_ReturnsFilteredFields() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """[{"id":1,"tag_name":"v1","name":"Release 1","draft":false}]""" };

        var result = await _handler.GhReleaseListAsync(json_fields: "id,tag_name", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("\"id\":1");
        text.Should().Contain("\"tag_name\":\"v1\"");
        text.Should().NotContain("\"name\"");
    }

    [Fact]
    public async Task LabelList_WithJson_ReturnsFilteredFields() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """[{"id":1,"name":"bug","color":"d73a4a","description":"Bug"}]""" };

        var result = await _handler.GhLabelListAsync(json_fields: "id,name", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("\"id\":1");
        text.Should().Contain("\"name\":\"bug\"");
        text.Should().NotContain("\"color\"");
    }

    [Fact]
    public async Task WorkflowList_WithJson_ReturnsFilteredFields() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"workflows":[{"id":42,"name":"CI","state":"active","path":".github/workflows/ci.yml"}]}""" };

        var result = await _handler.GhWorkflowListAsync(json_fields: "id,name", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("\"id\":42");
        text.Should().Contain("\"name\":\"CI\"");
        text.Should().NotContain("\"state\"");
    }

    [Fact]
    public async Task PrView_WithJson_ReturnsFilteredFields() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"number":7,"title":"pr","state":"open","body":"text","mergeable":true}""" };

        var result = await _handler.GhPrViewAsync("7", json_fields: "number,title", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("\"number\":7");
        text.Should().Contain("\"title\":\"pr\"");
        text.Should().NotContain("\"state\"");
    }

    [Fact]
    public async Task IssueView_WithJson_ReturnsFilteredFields() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"number":3,"title":"issue","state":"open","body":"desc"}""" };

        var result = await _handler.GhIssueViewAsync("3", json_fields: "number,title", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("\"number\":3");
        text.Should().Contain("\"title\":\"issue\"");
        text.Should().NotContain("\"state\"");
    }

    [Fact]
    public async Task RepoView_WithJson_ReturnsFilteredFields() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"name":"repo","full_name":"owner/repo","description":"test","private":false}""" };

        var result = await _handler.GhRepoViewAsync(json_fields: "name,full_name", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("\"name\":\"repo\"");
        text.Should().Contain("\"full_name\":\"owner/repo\"");
        text.Should().NotContain("\"description\"");
    }

    [Fact]
    public async Task RepoList_WithJson_ReturnsCustomFields() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """[{"name":"r1","full_name":"o/r1","language":"C#","fork":false}]""" };

        var result = await _handler.GhRepoListAsync(json_fields: "name,language", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("\"name\":\"r1\"");
        text.Should().Contain("\"language\":\"C#\"");
        text.Should().NotContain("\"full_name\"");
    }

    [Fact]
    public async Task SecretList_WithJson_ReturnsFilteredFields() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"total_count":1,"secrets":[{"name":"K","created_at":"2026-01-01"}]}""" };

        var result = await _handler.GhSecretListAsync(json_fields: "name", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("\"name\":\"K\"");
        text.Should().NotContain("\"created_at\"");
    }

    [Fact]
    public async Task VariableList_WithJson_ReturnsFilteredFields() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"total_count":1,"variables":[{"name":"V","value":"x","updated_at":"2026-01-01"}]}""" };

        var result = await _handler.GhVariableListAsync(json_fields: "name,value", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("\"name\":\"V\"");
        text.Should().Contain("\"value\":\"x\"");
        text.Should().NotContain("\"updated_at\"");
    }

    [Fact]
    public async Task OrgList_WithJson_ReturnsFilteredFields() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """[{"login":"org","description":"d"}]""" };

        var result = await _handler.GhOrgListAsync(json_fields: "login");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("\"login\":\"org\"");
        text.Should().NotContain("\"description\"");
    }

    [Fact]
    public async Task SshKeyList_WithJson_ReturnsFilteredFields() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """[{"id":1,"title":"k","key":"ssh-..."}]""" };

        var result = await _handler.GhSshKeyListAsync(json_fields: "id,title");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("\"id\":1");
        text.Should().Contain("\"title\":\"k\"");
        text.Should().NotContain("\"key\"");
    }

    [Fact]
    public async Task SearchRepos_WithJson_ReturnsFilteredFields() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"total_count":1,"items":[{"full_name":"o/r","stargazers_count":5,"description":"d"}]}""" };

        var result = await _handler.GhSearchReposAsync(query: "stars:>1", json_fields: "full_name");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("\"full_name\":\"o/r\"");
        text.Should().NotContain("\"stargazers_count\"");
    }

    [Fact]
    public async Task GistList_WithJson_ReturnsFilteredFields() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """[{"id":"abc","description":"d","public":false}]""" };

        var result = await _handler.GhGistListAsync(json_fields: "id");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("\"id\":\"abc\"");
        text.Should().NotContain("\"description\"");
    }

    [Fact]
    public async Task RunView_WithJson_ReturnsFilteredFields() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"id":123,"status":"completed","conclusion":"success","head_sha":"abc"}""" };

        var result = await _handler.GhRunViewAsync("123", json_fields: "id,status", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("\"id\":123");
        text.Should().Contain("\"status\":\"completed\"");
        text.Should().NotContain("\"conclusion\"");
    }

    [Fact]
    public async Task ReleaseView_WithJson_ReturnsFilteredFields() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"id":1,"tag_name":"v1","name":"Release 1","draft":false}""" };

        var result = await _handler.GhReleaseViewAsync("v1", json_fields: "id,tag_name", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("\"id\":1");
        text.Should().Contain("\"tag_name\":\"v1\"");
        text.Should().NotContain("\"name\"");
    }

    [Fact]
    public async Task WorkflowView_WithJson_ReturnsFilteredFields() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"id":42,"name":"CI","state":"active","path":".github/workflows/ci.yml"}""" };

        var result = await _handler.GhWorkflowViewAsync("ci.yml", json_fields: "id,name", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("\"id\":42");
        text.Should().Contain("\"name\":\"CI\"");
        text.Should().NotContain("\"state\"");
    }

    [Fact]
    public async Task GistView_WithJson_ReturnsFilteredFields() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"id":"abc","description":"d","public":false}""" };

        var result = await _handler.GhGistViewAsync("abc", json_fields: "id");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("\"id\":\"abc\"");
        text.Should().NotContain("\"description\"");
    }
}
