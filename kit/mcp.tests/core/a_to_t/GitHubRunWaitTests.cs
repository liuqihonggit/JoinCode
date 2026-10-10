namespace Mcp.Tests;

public sealed class GitHubRunWaitTests {
    private readonly FakeGitHubApiClient _api = new();
    private readonly GitHubToolHandlers _handler;

    public GitHubRunWaitTests() {
        MemoryCache.Default.Trim(100);
        _handler = new GitHubToolHandlers(new FakeDownloader(), new InMemoryFileSystem(), _api, null, NullLogger<GitHubToolHandlers>.Instance);
    }

    /// <summary>为 ResolveRunIdAsync 入队一个成功响应(它先发一次 GET 验证 run_id 存在,只看 Success 不解析 Body)</summary>
    private void EnqueueResolveRunIdOk()
        => _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = "{}" });

    [Fact]
    public async Task RunWait_AlreadyCompleted_ReturnsSuccess() {
        EnqueueResolveRunIdOk();
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"status":"completed","conclusion":"success","html_url":"https://github.com/o/r/actions/runs/1"}"""
        });

        var result = await _handler.GhRunWaitAsync(
            run_id: "1", timeout_seconds: 10, poll_interval_seconds: 1, common: new GitHubCommonOptions { Repo = "owner/repo" });

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("✅ success");
        result.GetFirstText().Should().Contain("轮询次数: 1");
    }

    [Fact]
    public async Task RunWait_PollingThenCompleted_ReturnsFailure() {
        EnqueueResolveRunIdOk();
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"status":"in_progress","conclusion":null}"""
        });
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"status":"completed","conclusion":"failure","html_url":"https://github.com/o/r/actions/runs/2"}"""
        });
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"jobs":[]}"""
        });

        var result = await _handler.GhRunWaitAsync(
            run_id: "2", timeout_seconds: 10, poll_interval_seconds: 1, common: new GitHubCommonOptions { Repo = "owner/repo" });

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("❌ failure");
        result.GetFirstText().Should().Contain("轮询次数: 2");
    }

    [Fact]
    public async Task RunWait_Timeout_ReturnsPendingStatus() {
        EnqueueResolveRunIdOk();
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"status":"in_progress","conclusion":null}"""
        });
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"status":"in_progress","conclusion":null}"""
        });
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"status":"in_progress","conclusion":null}"""
        });

        var result = await _handler.GhRunWaitAsync(
            run_id: "3", timeout_seconds: 2, poll_interval_seconds: 1, common: new GitHubCommonOptions { Repo = "owner/repo" });

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("等待超时");
        result.GetFirstText().Should().Contain("in_progress");
    }

    [Fact]
    public async Task RunWait_ApiError_ReturnsError() {
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = false, StatusCode = 404, Error = "Not Found"
        });
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200, Body = """{"workflow_runs":[]}"""
        });

        var result = await _handler.GhRunWaitAsync(
            run_id: "999", timeout_seconds: 10, poll_interval_seconds: 1, common: new GitHubCommonOptions { Repo = "owner/repo" });

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("不存在");
    }

    [Fact]
    public async Task PrWait_AllChecksCompleted_ReturnsSummary() {
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"number":42,"head":{"sha":"abc123"}}"""
        });
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"check_runs":[{"name":"build","status":"completed","conclusion":"success"},{"name":"test","status":"completed","conclusion":"success"}]}"""
        });

        var result = await _handler.GhPrWaitAsync(
            pr_number: "42", timeout_seconds: 10, poll_interval_seconds: 1, common: new GitHubCommonOptions { Repo = "owner/repo" });

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("2 通过");
        result.GetFirstText().Should().Contain("已完成");
    }

    [Fact]
    public async Task PrWait_PendingThenCompleted_ReturnsSummary() {
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"number":42,"head":{"sha":"abc123"}}"""
        });
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"check_runs":[{"name":"build","status":"completed","conclusion":"success"},{"name":"test","status":"in_progress","conclusion":null}]}"""
        });
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"check_runs":[{"name":"build","status":"completed","conclusion":"success"},{"name":"test","status":"completed","conclusion":"failure"}]}"""
        });

        var result = await _handler.GhPrWaitAsync(
            pr_number: "42", timeout_seconds: 10, poll_interval_seconds: 1, common: new GitHubCommonOptions { Repo = "owner/repo" });

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("1 失败");
        result.GetFirstText().Should().Contain("轮询次数: 2");
    }

    [Fact]
    public async Task PrWait_Timeout_ReturnsPendingSummary() {
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"number":42,"head":{"sha":"abc123"}}"""
        });
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"check_runs":[{"name":"build","status":"in_progress","conclusion":null}]}"""
        });
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"check_runs":[{"name":"build","status":"in_progress","conclusion":null}]}"""
        });
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"check_runs":[{"name":"build","status":"in_progress","conclusion":null}]}"""
        });

        var result = await _handler.GhPrWaitAsync(
            pr_number: "42", timeout_seconds: 2, poll_interval_seconds: 1, common: new GitHubCommonOptions { Repo = "owner/repo" });

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("等待超时");
        result.GetFirstText().Should().Contain("1 进行中");
    }

    [Fact]
    public async Task RunWait_Failure_DownloadsLogsToDisk() {
        var fs = new InMemoryFileSystem();
        var api = new FakeGitHubApiClient();
        api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"status":"completed","conclusion":"failure","html_url":"https://github.com/o/r/actions/runs/1"}"""
        });
        api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"status":"completed","conclusion":"failure","html_url":"https://github.com/o/r/actions/runs/1"}"""
        });
        api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"jobs":[{"id":123,"conclusion":"failure"}]}"""
        });
        api.NextLogLines = new[] { "##[error] test failed", "  Failed MyTest [FAIL]", "  Error Message: boom" };

        var handler = new GitHubToolHandlers(new FakeDownloader(), fs, api, null, NullLogger<GitHubToolHandlers>.Instance);

        var result = await handler.GhRunWaitAsync(
            run_id: "1", timeout_seconds: 10, poll_interval_seconds: 1, common: new GitHubCommonOptions { Repo = "owner/repo" });

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("日志已下载到");
        result.GetFirstText().Should().Contain("run_1_");
        result.GetFirstText().Should().Contain(".jcc");
        result.GetFirstText().Should().Contain("gh_logs");
    }

    [Fact]
    public async Task RunWait_Success_DoesNotDownloadLogs() {
        EnqueueResolveRunIdOk();
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"status":"completed","conclusion":"success","html_url":"https://github.com/o/r/actions/runs/1"}"""
        });

        var result = await _handler.GhRunWaitAsync(
            run_id: "1", timeout_seconds: 10, poll_interval_seconds: 1, common: new GitHubCommonOptions { Repo = "owner/repo" });

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().NotContain("日志已下载");
    }

    [Fact]
    public async Task PrWait_FailedChecks_DownloadsLogsToDisk() {
        var fs = new InMemoryFileSystem();
        var api = new FakeGitHubApiClient();
        api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"number":42,"head":{"sha":"abc123"}}"""
        });
        api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"check_runs":[{"name":"build","status":"completed","conclusion":"success"},{"name":"test","status":"completed","conclusion":"failure"}]}"""
        });
        api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"workflow_runs":[{"id":99,"conclusion":"failure","head_sha":"abc123"}]}"""
        });
        api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"jobs":[{"id":456,"conclusion":"failure"}]}"""
        });
        api.NextLogLines = new[] { "##[error] build failed", "error CS0001: syntax error" };

        var handler = new GitHubToolHandlers(new FakeDownloader(), fs, api, null, NullLogger<GitHubToolHandlers>.Instance);

        var result = await handler.GhPrWaitAsync(
            pr_number: "42", timeout_seconds: 10, poll_interval_seconds: 1, common: new GitHubCommonOptions { Repo = "owner/repo" });

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("1 失败");
        result.GetFirstText().Should().Contain("日志已下载到");
    }
}
