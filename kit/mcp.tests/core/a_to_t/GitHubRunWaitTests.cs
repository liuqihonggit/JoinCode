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

    /// <summary>
    /// fail-fast 模式:run 还在 in_progress,但某 job 已 failure → 立即返回(fail-fast,不等 run completed)
    /// </summary>
    [Fact]
    public async Task RunWait_FailFast_JobFailureDuringRun_ReturnsImmediately() {
        var fs = new InMemoryFileSystem();
        var api = new FakeGitHubApiClient();
        api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = "{}" });
        api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"status":"in_progress","conclusion":null}"""
        });
        api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"jobs":[{"id":123,"name":"test","status":"completed","conclusion":"failure"}]}"""
        });
        api.NextLogLines = new[] { "##[error] test failed", "  Failed MyTest [FAIL]", "  Error Message: boom" };

        var handler = new GitHubToolHandlers(new FakeDownloader(), fs, api, null, NullLogger<GitHubToolHandlers>.Instance);

        var result = await handler.GhRunWaitAsync(
            run_id: "1", timeout_seconds: 10, poll_interval_seconds: 1, fail_fast: true,
            common: new GitHubCommonOptions { Repo = "owner/repo" });

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("fail-fast");
        result.GetFirstText().Should().Contain("日志已下载到");
        result.GetFirstText().Should().Contain("轮询次数: 1");
    }

    /// <summary>
    /// fail-fast 模式:run completed 且无失败 job → 返回成功
    /// </summary>
    [Fact]
    public async Task RunWait_FailFast_RunCompletedSuccess_ReturnsSuccess() {
        EnqueueResolveRunIdOk();
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"status":"completed","conclusion":"success"}"""
        });
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"jobs":[{"id":123,"name":"test","status":"completed","conclusion":"success"}]}"""
        });

        var result = await _handler.GhRunWaitAsync(
            run_id: "1", timeout_seconds: 10, poll_interval_seconds: 1, fail_fast: true,
            common: new GitHubCommonOptions { Repo = "owner/repo" });

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("success");
        result.GetFirstText().Should().NotContain("日志已下载");
    }

    /// <summary>
    /// fail-fast 模式:run completed 且有失败 job → 返回失败 job 日志
    /// </summary>
    [Fact]
    public async Task RunWait_FailFast_RunCompletedWithFailure_ReturnsLogs() {
        var fs = new InMemoryFileSystem();
        var api = new FakeGitHubApiClient();
        api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = "{}" });
        api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"status":"completed","conclusion":"failure"}"""
        });
        api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"jobs":[{"id":456,"name":"build","status":"completed","conclusion":"failure"}]}"""
        });
        api.NextLogLines = new[] { "##[error] build failed", "error CS0001: syntax error" };

        var handler = new GitHubToolHandlers(new FakeDownloader(), fs, api, null, NullLogger<GitHubToolHandlers>.Instance);

        var result = await handler.GhRunWaitAsync(
            run_id: "1", timeout_seconds: 10, poll_interval_seconds: 1, fail_fast: true,
            common: new GitHubCommonOptions { Repo = "owner/repo" });

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("日志已下载到");
        result.GetFirstText().Should().Contain("1 失败");
    }

    /// <summary>
    /// fail-fast 模式:轮询超时 → 返回超时摘要
    /// </summary>
    [Fact]
    public async Task RunWait_FailFast_Timeout_ReturnsSummary() {
        EnqueueResolveRunIdOk();
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"status":"in_progress","conclusion":null}"""
        });
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"jobs":[{"id":123,"name":"test","status":"in_progress","conclusion":null}]}"""
        });
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"status":"in_progress","conclusion":null}"""
        });
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"jobs":[{"id":123,"name":"test","status":"in_progress","conclusion":null}]}"""
        });
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"status":"in_progress","conclusion":null}"""
        });
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"jobs":[{"id":123,"name":"test","status":"in_progress","conclusion":null}]}"""
        });

        var result = await _handler.GhRunWaitAsync(
            run_id: "1", timeout_seconds: 2, poll_interval_seconds: 1, fail_fast: true,
            common: new GitHubCommonOptions { Repo = "owner/repo" });

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("等待超时");
    }

    /// <summary>
    /// gh ci alerts 从 LSM 读取告警通知
    /// </summary>
    [Fact]
    public async Task CiAlerts_ReadsFromKvStore_ReturnsAlerts() {
        var kvStore = new InMemoryKvStore();
        var api = new FakeGitHubApiClient();
        var handler = new GitHubToolHandlers(new FakeDownloader(), new InMemoryFileSystem(), api, null, NullLogger<GitHubToolHandlers>.Instance, kvStore);

        var json = """{"run_id":"1","job_id":123,"job_name":"test","log_path":"/tmp/log.log","timestamp":"2026-01-01T00:00:00Z"}""";
        await kvStore.PutAsync(Encoding.UTF8.GetBytes("gh:ci_alert:1:123:20260101"), Encoding.UTF8.GetBytes(json));

        var result = await handler.GhCiAlertsAsync(common: new GitHubCommonOptions { Repo = "owner/repo" });

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("CI 告警通知");
        result.GetFirstText().Should().Contain("Run 1");
        result.GetFirstText().Should().Contain("Job #123");
        result.GetFirstText().Should().Contain("/tmp/log.log");
    }

    /// <summary>
    /// gh ci alerts mark_read=true 后告警从 LSM 删除
    /// </summary>
    [Fact]
    public async Task CiAlerts_MarkRead_DeletesFromKvStore() {
        var kvStore = new InMemoryKvStore();
        var api = new FakeGitHubApiClient();
        var handler = new GitHubToolHandlers(new FakeDownloader(), new InMemoryFileSystem(), api, null, NullLogger<GitHubToolHandlers>.Instance, kvStore);

        var json = """{"run_id":"2","job_id":456,"job_name":"build","log_path":"/tmp/build.log","timestamp":"2026-01-01T00:00:00Z"}""";
        var key = Encoding.UTF8.GetBytes("gh:ci_alert:2:456:20260101");
        await kvStore.PutAsync(key, Encoding.UTF8.GetBytes(json));

        await handler.GhCiAlertsAsync(mark_read: true, common: new GitHubCommonOptions { Repo = "owner/repo" });

        var remaining = await kvStore.GetAsync(key);
        remaining.Should().BeNull();
    }

    /// <summary>
    /// gh ci alerts 无告警时返回"无 CI 告警通知"
    /// </summary>
    [Fact]
    public async Task CiAlerts_NoAlerts_ReturnsEmpty() {
        var kvStore = new InMemoryKvStore();
        var api = new FakeGitHubApiClient();
        var handler = new GitHubToolHandlers(new FakeDownloader(), new InMemoryFileSystem(), api, null, NullLogger<GitHubToolHandlers>.Instance, kvStore);

        var result = await handler.GhCiAlertsAsync(common: new GitHubCommonOptions { Repo = "owner/repo" });

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("无 CI 告警");
    }
}
