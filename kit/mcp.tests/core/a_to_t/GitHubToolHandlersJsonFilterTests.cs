namespace Mcp.Tests;

/// <summary>
/// --json 精确字段选择测试 — 验证 FilterJsonFields 在各命令中的行为
/// </summary>
public sealed partial class GitHubToolHandlersTests {
    [Fact]
    public async Task PrList_WithJson_ReturnsFilteredFields() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """[{"number":1,"title":"bug","state":"open","body":"text","url":"https://x"}]""" };

        var result = await _handler.GhPrListAsync(json: "number,title", repo: "owner/repo");

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

        var result = await _handler.GhPrListAsync(search: "review:required", json: "number,title", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("\"number\":2");
        text.Should().Contain("\"title\":\"feat\"");
        text.Should().NotContain("\"state\"");
    }

    [Fact]
    public async Task IssueList_WithJson_ReturnsFilteredFields() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """[{"number":5,"title":"issue","state":"open","body":"desc"}]""" };

        var result = await _handler.GhIssueListAsync(json: "number,title", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("\"number\":5");
        text.Should().Contain("\"title\":\"issue\"");
        text.Should().NotContain("\"state\"");
    }

    [Fact]
    public async Task RunList_WithJson_ReturnsFilteredFields() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"workflow_runs":[{"id":123,"status":"completed","conclusion":"success","head_sha":"abc"}]}""" };

        var result = await _handler.GhRunListAsync(json: "id,status", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("\"id\":123");
        text.Should().Contain("\"status\":\"completed\"");
        text.Should().NotContain("\"conclusion\"");
    }

    [Fact]
    public async Task ReleaseList_WithJson_ReturnsFilteredFields() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """[{"id":1,"tag_name":"v1","name":"Release 1","draft":false}]""" };

        var result = await _handler.GhReleaseListAsync(json: "id,tag_name", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("\"id\":1");
        text.Should().Contain("\"tag_name\":\"v1\"");
        text.Should().NotContain("\"name\"");
    }

    [Fact]
    public async Task LabelList_WithJson_ReturnsFilteredFields() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """[{"id":1,"name":"bug","color":"d73a4a","description":"Bug"}]""" };

        var result = await _handler.GhLabelListAsync(json: "id,name", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("\"id\":1");
        text.Should().Contain("\"name\":\"bug\"");
        text.Should().NotContain("\"color\"");
    }

    [Fact]
    public async Task WorkflowList_WithJson_ReturnsFilteredFields() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"workflows":[{"id":42,"name":"CI","state":"active","path":".github/workflows/ci.yml"}]}""" };

        var result = await _handler.GhWorkflowListAsync(json: "id,name", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("\"id\":42");
        text.Should().Contain("\"name\":\"CI\"");
        text.Should().NotContain("\"state\"");
    }

    [Fact]
    public async Task PrView_WithJson_ReturnsFilteredFields() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"number":7,"title":"pr","state":"open","body":"text","mergeable":true}""" };

        var result = await _handler.GhPrViewAsync("7", json: "number,title", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("\"number\":7");
        text.Should().Contain("\"title\":\"pr\"");
        text.Should().NotContain("\"state\"");
    }

    [Fact]
    public async Task IssueView_WithJson_ReturnsFilteredFields() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"number":3,"title":"issue","state":"open","body":"desc"}""" };

        var result = await _handler.GhIssueViewAsync("3", json: "number,title", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("\"number\":3");
        text.Should().Contain("\"title\":\"issue\"");
        text.Should().NotContain("\"state\"");
    }

    [Fact]
    public async Task RepoView_WithJson_ReturnsFilteredFields() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"name":"repo","full_name":"owner/repo","description":"test","private":false}""" };

        var result = await _handler.GhRepoViewAsync(json: "name,full_name", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("\"name\":\"repo\"");
        text.Should().Contain("\"full_name\":\"owner/repo\"");
        text.Should().NotContain("\"description\"");
    }

    [Fact]
    public async Task RepoList_WithJson_ReturnsCustomFields() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """[{"name":"r1","full_name":"o/r1","language":"C#","fork":false}]""" };

        var result = await _handler.GhRepoListAsync(json: "name,language", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("\"name\":\"r1\"");
        text.Should().Contain("\"language\":\"C#\"");
        text.Should().NotContain("\"full_name\"");
    }
}
