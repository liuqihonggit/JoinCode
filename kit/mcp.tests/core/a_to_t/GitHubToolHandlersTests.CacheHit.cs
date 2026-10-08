namespace Mcp.Tests;

public sealed partial class GitHubToolHandlersTests {
    /// <summary>缓存复用: expand=failed 首次下载日志写入 LSM,二次请求命中缓存不重新下载</summary>
    [Fact]
    public async Task RunView_ExpandFailed_CacheHit_SecondCallDoesNotRedownload() {
        var runDetail = """{"id":42,"status":"completed","conclusion":"failure"}""";
        var jobsJson = """{"jobs":[{"id":101,"name":"unit-tests","status":"completed","conclusion":"failure"}]}""";

        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = runDetail });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = jobsJson });
        _api.NextLogLines = new[] { "##[error] something failed", "details", "end" };

        var result1 = await _handler.GhRunViewAsync("42", expand: "failed", repo: "owner/repo");
        result1.IsError.Should().BeFalse();
        result1.GetFirstText()!.Should().Contain("##[error]", "首次调用应下载并返回日志");

        _api.NextLogLines = Array.Empty<string>();
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = runDetail });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = jobsJson });

        var result2 = await _handler.GhRunViewAsync("42", expand: "failed", repo: "owner/repo");
        result2.IsError.Should().BeFalse();
        result2.GetFirstText()!.Should().Contain("##[error]", "二次调用应命中 LSM 缓存仍返回日志(不重新下载)");
    }

    /// <summary>缓存复用: filter=failed 首次下载写入 LSM,二次命中缓存</summary>
    [Fact]
    public async Task RunView_FilterFailed_CacheHit_SecondCallDoesNotRedownload() {
        var runDetail = """{"id":42,"status":"completed","conclusion":"failure"}""";
        var jobsJson = """{"jobs":[{"id":101,"name":"unit-tests","status":"completed","conclusion":"failure"}]}""";

        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = runDetail });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = jobsJson });
        _api.NextLogLines = new[] { "  Failed MyTest [FAIL]", "  Error Message: boom", "  Stack Trace: at line 1" };

        var result1 = await _handler.GhRunViewAsync("42", filter: "failed", repo: "owner/repo");
        result1.IsError.Should().BeFalse();

        _api.NextLogLines = Array.Empty<string>();
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = runDetail });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = jobsJson });

        var result2 = await _handler.GhRunViewAsync("42", filter: "failed", repo: "owner/repo");
        result2.IsError.Should().BeFalse();
        result2.GetFirstText()!.Should().Contain("MyTest", "二次调用应命中 LSM 缓存仍返回测试失败信息");
    }
}
