namespace Gh.Tests;

public sealed partial class GitHubToolHandlersTests {
    /// <summary>写方法 404 但同路径 GET 成功 → 资源存在，写方法被网络层拦截（TASK134 C1 成对判据，不依赖被改写的 body）</summary>
    [Fact]
    public async Task Api_WriteMethod404_ButGetSucceeds_HintsGraphQLFallback() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = false, StatusCode = 404, Error = "Not Found" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"default_branch":"main"}""" });

        var result = await _handler.GhApiAsync("repos/owner/repo/branches/main/protection", method: "PATCH", body: "{}");

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("GraphQL");
        result.GetFirstText().Should().Contain("网络层");
        _api.CallLog.Should().HaveCount(2);
        _api.CallLog[0].Should().Contain("PATCH");
        _api.CallLog[1].Should().Contain("GET");
    }

    /// <summary>写方法 404 且 GET 也 404 → 资源真不存在，保留原提示不误报网络拦截</summary>
    [Fact]
    public async Task Api_WriteMethod404_AndGetAlso404_KeepsOriginalHint() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = false, StatusCode = 404, Error = "Not Found" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = false, StatusCode = 404, Error = "Not Found" });

        var result = await _handler.GhApiAsync("repos/owner/repo/branches/nonexistent/protection", method: "PATCH", body: "{}");

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("仓库不存在");
        result.GetFirstText().Should().NotContain("GraphQL");
    }

    /// <summary>GET 的 404 不做探测（避免无意义二次请求）</summary>
    [Fact]
    public async Task Api_GetMethod404_NoExtraProbe() {
        _api.NextResponse = new GitHubApiResponse { Success = false, StatusCode = 404, Error = "Not Found" };

        await _handler.GhApiAsync("repos/nonexistent/repo", method: "GET");

        _api.CallLog.Should().HaveCount(1);
    }

    [Fact]
    public async Task Api_AutoMergeEndpoint_404_ContainsAllowAutoMergeHint() {
        _api.NextResponse = new GitHubApiResponse { Success = false, StatusCode = 404, Error = "Not Found" };

        var result = await _handler.GhApiAsync("repos/owner/repo/pulls/42/auto-merge", method: "PUT", body: "{\"merge_method\":\"squash\"}");

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("Allow auto-merge");
        result.GetFirstText().Should().Contain("404");
    }

    [Fact]
    public async Task Api_ReposEndpoint_404_ContainsRepoNotFoundHint() {
        _api.NextResponse = new GitHubApiResponse { Success = false, StatusCode = 404, Error = "Not Found" };

        var result = await _handler.GhApiAsync("repos/nonexistent/repo", method: "GET");

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("仓库不存在");
    }

    [Fact]
    public async Task Api_MergeEndpoint_403_ContainsBranchProtectionHint() {
        _api.NextResponse = new GitHubApiResponse { Success = false, StatusCode = 403, Error = "Forbidden" };

        var result = await _handler.GhApiAsync("repos/owner/repo/pulls/42/merge", method: "PUT", body: "{}");

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("分支保护");
    }

    [Fact]
    public async Task Api_GraphQLPath_NoMethod_AutoDefaultsToPost() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = "{\"data\":{\"viewer\":{\"login\":\"test\"}}}" };

        var result = await _handler.GhApiAsync("graphql", body: "{\"query\":\"query { viewer { login } }\"}");

        result.IsError.Should().BeFalse();
        _api.LastMethod.Should().Be(HttpMethod.Post);
    }

    [Fact]
    public async Task Api_GraphQLPath_WithFields_AutoPostAndConvertsFieldsToBody() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = "{\"data\":{\"viewer\":{\"login\":\"test\"}}}" };

        var result = await _handler.GhApiAsync("graphql", fields: "query=query { viewer { login } }");

        result.IsError.Should().BeFalse();
        _api.LastMethod.Should().Be(HttpMethod.Post);
        _api.LastBody.Should().Contain("\"query\"");
    }

    [Theory]
    [InlineData("/users/octocat", "users/octocat")]
    [InlineData("/repos/owner/repo", "repos/owner/repo")]
    [InlineData("/orgs/microsoft", "orgs/microsoft")]
    [InlineData("/search/repositories", "search/repositories")]
    [InlineData("/rate_limit", "rate_limit")]
    [InlineData("/graphql", "graphql")]
    public async Task Api_LeadingSlash_Stripped(string input, string expected) {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = "{}" };

        await _handler.GhApiAsync(input);

        _api.LastPath.Should().Be(expected);
    }

    [Theory]
    [InlineData("C:/Program Files/Git/users/octocat", "users/octocat")]
    [InlineData("C:/Program Files/Git/repos/owner/repo", "repos/owner/repo")]
    [InlineData("D:/Git/orgs/microsoft", "orgs/microsoft")]
    [InlineData("C:\\Program Files\\Git\\search\\repositories", "search/repositories")]
    [InlineData("C:/Program Files/Git/rate_limit", "rate_limit")]
    public async Task Api_MsysPathConversion_Undo(string input, string expected) {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = "{}" };

        await _handler.GhApiAsync(input);

        _api.LastPath.Should().Be(expected);
    }
}
