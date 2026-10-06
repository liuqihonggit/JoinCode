namespace Mcp.Tests;

public sealed class GitHubRunWaitTests {
    private readonly FakeGitHubApiClient _api = new();
    private readonly GitHubToolHandlers _handler;

    public GitHubRunWaitTests() {
        MemoryCache.Default.Trim(100);
        _handler = new GitHubToolHandlers(
            new FakeDownloader(),
            new InMemoryFileSystem(),
            new PersistencePipeline(new InMemoryFileSystem()),
            _api,
            null,
            NullLogger<GitHubToolHandlers>.Instance);
    }

    [Fact]
    public async Task RunWait_AlreadyCompleted_ReturnsSuccess() {
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"status":"completed","conclusion":"success","html_url":"https://github.com/o/r/actions/runs/1"}"""
        });

        var result = await _handler.GhRunWaitAsync(
            run_id: "1", repo: "owner/repo", timeout_seconds: 10, poll_interval_seconds: 1);

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("✅ success");
        result.GetFirstText().Should().Contain("轮询次数: 1");
    }

    [Fact]
    public async Task RunWait_PollingThenCompleted_ReturnsFailure() {
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"status":"in_progress","conclusion":null}"""
        });
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"status":"completed","conclusion":"failure","html_url":"https://github.com/o/r/actions/runs/2"}"""
        });

        var result = await _handler.GhRunWaitAsync(
            run_id: "2", repo: "owner/repo", timeout_seconds: 10, poll_interval_seconds: 1);

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("❌ failure");
        result.GetFirstText().Should().Contain("轮询次数: 2");
    }

    [Fact]
    public async Task RunWait_Timeout_ReturnsPendingStatus() {
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
            run_id: "3", repo: "owner/repo", timeout_seconds: 2, poll_interval_seconds: 1);

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("等待超时");
        result.GetFirstText().Should().Contain("in_progress");
    }

    [Fact]
    public async Task RunWait_ApiError_ReturnsError() {
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = false, StatusCode = 404, Error = "Not Found"
        });

        var result = await _handler.GhRunWaitAsync(
            run_id: "999", repo: "owner/repo", timeout_seconds: 10, poll_interval_seconds: 1);

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("Not Found");
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
            pr_number: "42", repo: "owner/repo", timeout_seconds: 10, poll_interval_seconds: 1);

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
            pr_number: "42", repo: "owner/repo", timeout_seconds: 10, poll_interval_seconds: 1);

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
            pr_number: "42", repo: "owner/repo", timeout_seconds: 2, poll_interval_seconds: 1);

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("等待超时");
        result.GetFirstText().Should().Contain("1 进行中");
    }
}
