namespace Gh.Tests;

public sealed class GitHubRunWaitTests {
    private readonly FakeGitHubApiClient _api = new();
    private readonly GitHubToolHandlers _handler;

    public GitHubRunWaitTests() {
        _handler = new GitHubToolHandlers(new FakeDownloader(), new InMemoryFileSystem(), _api, null, NullLogger<GitHubToolHandlers>.Instance);
    }

    /// <summary>为 ResolveRunIdAsync 入队一个成功响应(它先发一次 GET 验证 run_id 存在,只看 Success 不解析 Body)</summary>
    private void EnqueueResolveRunIdOk()
        => _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = "{}" });

    /// <summary>单次 HTTP 超时只终止当前请求，轮询应按退避重试并最终返回完成状态。</summary>
    [Fact]
    public async Task RunWait_TransientRequestTimeout_RetriesAndCompletes() {
        var api = new FakeGitHubApiClient();
        api.EnqueueException(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout."));
        api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"status":"completed","conclusion":"success"}"""
        });
        var progress = new List<string>();

        var result = await GitHubRunPoller.WaitForRunCompletionAsync(
            api, "owner", "repo", "1", TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(1),
            p => progress.Add(p.Message ?? string.Empty), "gh_run_wait", CancellationToken.None);

        result.Outcome.Should().Be(RunWaitOutcome.Completed);
        result.Conclusion.Should().Be("success");
        result.PollCount.Should().Be(2);
        progress.Should().Contain(message => message.Contains("请求超时", StringComparison.Ordinal));
    }

    /// <summary>客户端映射出的无状态码网络失败也应重试，而不是提前终止整个等待。</summary>
    [Fact]
    public async Task RunWait_TransientNetworkFailure_RetriesAndCompletes() {
        var api = new FakeGitHubApiClient();
        api.EnqueueResponse(new GitHubApiResponse {
            Success = false, StatusCode = 0, Error = "GitHub API 连接失败"
        });
        api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"status":"completed","conclusion":"success"}"""
        });
        var progress = new List<string>();

        var result = await GitHubRunPoller.WaitForRunCompletionAsync(
            api, "owner", "repo", "1", TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(1),
            p => progress.Add(p.Message ?? string.Empty), "gh_run_wait", CancellationToken.None);

        result.Outcome.Should().Be(RunWaitOutcome.Completed);
        result.Conclusion.Should().Be("success");
        result.PollCount.Should().Be(2);
        progress.Should().Contain(message => message.Contains("GitHub API 连接失败", StringComparison.Ordinal));
    }

    /// <summary>首次请求即超时且总期限到达时，应返回 Timeout 而不是因无轮询数据崩溃。</summary>
    [Fact]
    public async Task RunWait_FirstRequestTimeout_ReturnsTimeoutWithoutData() {
        var api = new FakeGitHubApiClient();
        api.SendAsyncOverride = _ => throw new TaskCanceledException("request timeout");

        var result = await GitHubRunPoller.WaitForRunCompletionAsync(
            api, "owner", "repo", "1", TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(1),
            null, "gh_run_wait", CancellationToken.None);

        result.Outcome.Should().Be(RunWaitOutcome.Timeout);
        result.Conclusion.Should().Be("unknown");
        result.Body.Should().BeEmpty();
        result.PollCount.Should().BeGreaterThan(0);
    }

    /// <summary>总等待期限必须取消在途 HTTP 请求，不能等客户端自身 100 秒超时。</summary>
    [Fact]
    public async Task RunWait_OverallTimeout_CancelsInFlightRequest() {
        var api = new FakeGitHubApiClient();
        var responseSource = new TaskCompletionSource<GitHubApiResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requestToken = CancellationToken.None;
        api.SendAsyncOverride = ct => {
            requestToken = ct;
            return responseSource.Task.WaitAsync(ct);
        };
        var waitTask = GitHubRunPoller.WaitForRunCompletionAsync(
            api, "owner", "repo", "1", TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(1),
            null, "gh_run_wait", CancellationToken.None);

        try {
            var result = await waitTask.WaitAsync(TimeSpan.FromSeconds(3));
            result.Outcome.Should().Be(RunWaitOutcome.Timeout);
            result.Conclusion.Should().Be("unknown");
            requestToken.IsCancellationRequested.Should().BeTrue();
        } finally {
            responseSource.TrySetResult(new GitHubApiResponse {
                Success = true, StatusCode = 200,
                Body = """{"status":"completed","conclusion":"success"}"""
            });
            await waitTask;
        }
    }

    /// <summary>真实 handler 的 run_id 前置解析也必须受同一个总等待期限约束。</summary>
    [Fact]
    public async Task RunWait_OverallTimeout_CoversRunIdResolution() {
        var api = new FakeGitHubApiClient();
        var responseSource = new TaskCompletionSource<GitHubApiResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requestToken = CancellationToken.None;
        api.SendAsyncOverride = ct => {
            requestToken = ct;
            return responseSource.Task.WaitAsync(ct);
        };
        var handler = new GitHubToolHandlers(
            new FakeDownloader(), new InMemoryFileSystem(), api, null, NullLogger<GitHubToolHandlers>.Instance);
        var waitTask = handler.GhRunWaitAsync(
            run_id: "1", timeout_seconds: 1, poll_interval_seconds: 1,
            common: new GitHubCommonOptions { Repo = "owner/repo" });

        try {
            var result = await waitTask.WaitAsync(TimeSpan.FromSeconds(3));
            result.IsError.Should().BeFalse();
            (result.GetFirstText() ?? string.Empty).Should().Contain("等待超时");
            requestToken.IsCancellationRequested.Should().BeTrue();
        } finally {
            responseSource.TrySetResult(new GitHubApiResponse {
                Success = true, StatusCode = 200,
                Body = """{"status":"in_progress","conclusion":null}"""
            });
            await waitTask;
        }
    }

    /// <summary>请求在途时调用方取消必须立即传播，且不能触发重试。</summary>
    [Fact]
    public async Task RunWait_InFlightCallerCancellation_PropagatesWithoutRetry() {
        var api = new FakeGitHubApiClient();
        var responseSource = new TaskCompletionSource<GitHubApiResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        api.SendAsyncOverride = ct => {
            started.SetResult();
            return responseSource.Task.WaitAsync(ct);
        };
        using var cts = new CancellationTokenSource();
        var waitTask = GitHubRunPoller.WaitForRunCompletionAsync(
            api, "owner", "repo", "1", TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(1),
            null, "gh_run_wait", cts.Token);

        try {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(1));
            cts.Cancel();
            var act = async () => await waitTask;
            await act.Should().ThrowAsync<OperationCanceledException>();
            api.CallLog.Should().ContainSingle();
        } finally {
            responseSource.TrySetResult(new GitHubApiResponse {
                Success = true, StatusCode = 200,
                Body = """{"status":"completed","conclusion":"success"}"""
            });
            if (!cts.IsCancellationRequested)
                await waitTask;
        }
    }

    /// <summary>瞬态失败不能覆盖上一次成功状态，超时摘要应保留状态并带出最后错误。</summary>
    [Fact]
    public async Task RunWait_TransientFailure_RetainsLastStateOnTimeout() {
        var api = new FakeGitHubApiClient();
        var calls = 0;
        api.SendAsyncOverride = _ => Task.FromResult(Interlocked.Increment(ref calls) == 1
            ? new GitHubApiResponse {
                Success = true, StatusCode = 200,
                Body = """{"status":"in_progress","conclusion":null,"html_url":"https://example.invalid/run/1"}"""
            }
            : new GitHubApiResponse { Success = false, StatusCode = 0, Error = "connection reset" });

        var result = await GitHubRunPoller.WaitForRunCompletionAsync(
            api, "owner", "repo", "1", TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(1),
            null, "gh_run_wait", CancellationToken.None);

        result.Outcome.Should().Be(RunWaitOutcome.Timeout);
        result.Conclusion.Should().Be("in_progress");
        result.Body.Should().Contain("in_progress");
        result.Error.Should().Contain("connection reset");
    }

    /// <summary>响应体读取阶段的 IO 失败与请求失败同属瞬态，应重试而不是穿出进程。</summary>
    [Fact]
    public async Task RunWait_ResponseBodyIOException_RetriesAndCompletes() {
        var api = new FakeGitHubApiClient();
        api.EnqueueException(new IOException("response body truncated"));
        api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"status":"completed","conclusion":"success"}"""
        });
        var progress = new List<string>();

        var result = await GitHubRunPoller.WaitForRunCompletionAsync(
            api, "owner", "repo", "1", TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(1),
            p => progress.Add(p.Message ?? string.Empty), "gh_run_wait", CancellationToken.None);

        result.Outcome.Should().Be(RunWaitOutcome.Completed);
        result.PollCount.Should().Be(2);
        progress.Should().Contain(message => message.Contains("读取 GitHub API 响应失败", StringComparison.Ordinal));
    }

    /// <summary>HTTP 401/404 等真实状态码必须保持终态，不能被传输失败重试策略掩盖。</summary>
    [Fact]
    public async Task RunWait_HttpStatusError_DoesNotRetry() {
        var api = new FakeGitHubApiClient();
        api.EnqueueResponse(new GitHubApiResponse { Success = false, StatusCode = 401, Error = "Bad credentials" });

        var result = await GitHubRunPoller.WaitForRunCompletionAsync(
            api, "owner", "repo", "1", TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(1),
            null, "gh_run_wait", CancellationToken.None);

        result.Outcome.Should().Be(RunWaitOutcome.Error);
        result.Error.Should().Be("Bad credentials");
        result.PollCount.Should().Be(1);
        api.CallLog.Should().ContainSingle();
    }

    /// <summary>run_id 前置解析遇到传输失败时应返回真实网络错误，不能误报 run 不存在。</summary>
    [Fact]
    public async Task RunWait_ResolveNetworkFailure_ReturnsNetworkError() {
        var api = new FakeGitHubApiClient();
        api.EnqueueResponse(new GitHubApiResponse { Success = false, StatusCode = 0, Error = "GitHub API 连接失败: DNS 解析失败" });
        var handler = new GitHubToolHandlers(
            new FakeDownloader(), new InMemoryFileSystem(), api, null, NullLogger<GitHubToolHandlers>.Instance);

        var result = await handler.GhRunWaitAsync(
            run_id: "1", timeout_seconds: 5, poll_interval_seconds: 1,
            common: new GitHubCommonOptions { Repo = "owner/repo" });

        result.IsError.Should().BeTrue();
        var text = result.GetFirstText() ?? string.Empty;
        text.Should().Contain("DNS 解析失败");
        text.Should().NotContain("不存在");
    }

    /// <summary>调用方取消必须继续传播，不能被单次请求超时重试逻辑吞掉。</summary>
    [Fact]
    public async Task RunWait_CallerCancellation_Propagates() {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var act = async () => await GitHubRunPoller.WaitForRunCompletionAsync(
            new FakeGitHubApiClient(), "owner", "repo", "1", TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(1),
            null, "gh_run_wait", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

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
    public async Task RunWait_Failure_ReturnsLsmCacheHint() {
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
        api.NextLogLines = new[] { "##[error] test failed", "Error Message: boom" };

        var handler = new GitHubToolHandlers(new FakeDownloader(), fs, api, null, NullLogger<GitHubToolHandlers>.Instance);

        var result = await handler.GhRunWaitAsync(
            run_id: "1", timeout_seconds: 10, poll_interval_seconds: 1, common: new GitHubCommonOptions { Repo = "owner/repo" });

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("失败日志");
        result.GetFirstText().Should().Contain("test failed");
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
    public async Task PrWait_FailedChecks_ReturnsLsmCacheHint() {
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

        var handler = new GitHubToolHandlers(new FakeDownloader(), fs, api, null, NullLogger<GitHubToolHandlers>.Instance);

        var result = await handler.GhPrWaitAsync(
            pr_number: "42", timeout_seconds: 10, poll_interval_seconds: 1, common: new GitHubCommonOptions { Repo = "owner/repo" });

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("1 失败");
        result.GetFirstText().Should().Contain("gh run view");
        result.GetFirstText().Should().Contain("--refresh");
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
        result.GetFirstText().Should().Contain("失败 job");
        result.GetFirstText().Should().Contain("失败日志");
        result.GetFirstText().Should().Contain("test failed");
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
        result.GetFirstText().Should().Contain("失败 job");
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

        var json = """{"run_id":"1","job_id":123,"job_name":"test","conclusion":"failure","timestamp":"2026-01-01T00:00:00Z"}""";
        await kvStore.PutAsync(Encoding.UTF8.GetBytes("gh:ci_alert:1:123:20260101"), Encoding.UTF8.GetBytes(json));

        var result = await handler.GhCiAlertsAsync(common: new GitHubCommonOptions { Repo = "owner/repo" });

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("CI 告警通知");
        result.GetFirstText().Should().Contain("Run 1");
        result.GetFirstText().Should().Contain("Job #123");
        result.GetFirstText().Should().Contain("failure");
    }

    /// <summary>
    /// gh ci alerts mark_read=true 后告警从 LSM 删除
    /// </summary>
    [Fact]
    public async Task CiAlerts_MarkRead_DeletesFromKvStore() {
        var kvStore = new InMemoryKvStore();
        var api = new FakeGitHubApiClient();
        var handler = new GitHubToolHandlers(new FakeDownloader(), new InMemoryFileSystem(), api, null, NullLogger<GitHubToolHandlers>.Instance, kvStore);

        var json = """{"run_id":"2","job_id":456,"job_name":"build","conclusion":"failure","timestamp":"2026-01-01T00:00:00Z"}""";
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

    /// <summary>
    /// fail-fast 模式:run 被 cancelled,jobs 还没更新 conclusion(failedJobIds 为空) → 立即返回,信息含"取消"
    /// </summary>
    /// <para>用户手工取消 CI run 是常见操作,wait 应立即感知并退出,不应继续轮询空转</para>
    [Fact]
    public async Task RunWait_FailFast_RunCancelled_NoFailedJobs_ReturnsCancelledImmediately() {
        EnqueueResolveRunIdOk();
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"status":"completed","conclusion":"cancelled"}"""
        });
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"jobs":[]}"""
        });

        var result = await _handler.GhRunWaitAsync(
            run_id: "1", timeout_seconds: 10, poll_interval_seconds: 1, fail_fast: true,
            common: new GitHubCommonOptions { Repo = "owner/repo" });

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("取消");
        result.GetFirstText().Should().Contain("轮询次数: 1");
    }

    /// <summary>
    /// fail-fast 模式:run 被 timed_out,jobs 还没更新 conclusion → 立即返回,信息含"超时"
    /// </summary>
    [Fact]
    public async Task RunWait_FailFast_RunTimedOut_NoFailedJobs_ReturnsTimedOutImmediately() {
        EnqueueResolveRunIdOk();
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"status":"completed","conclusion":"timed_out"}"""
        });
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"jobs":[]}"""
        });

        var result = await _handler.GhRunWaitAsync(
            run_id: "1", timeout_seconds: 10, poll_interval_seconds: 1, fail_fast: true,
            common: new GitHubCommonOptions { Repo = "owner/repo" });

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("超时");
        result.GetFirstText().Should().Contain("轮询次数: 1");
    }

    /// <summary>
    /// 非 fail-fast 模式:run 被 cancelled → 返回信息含"取消"或"失败",不应返回"已完成 ✅"
    /// </summary>
    [Fact]
    public async Task RunWait_NonFailFast_RunCancelled_ReturnsCancelledNotSuccess() {
        EnqueueResolveRunIdOk();
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"status":"completed","conclusion":"cancelled","html_url":"https://github.com/o/r/actions/runs/1"}"""
        });
        _api.EnqueueResponse(new GitHubApiResponse {
            Success = true, StatusCode = 200,
            Body = """{"jobs":[]}"""
        });

        var result = await _handler.GhRunWaitAsync(
            run_id: "1", timeout_seconds: 10, poll_interval_seconds: 1,
            common: new GitHubCommonOptions { Repo = "owner/repo" });

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("cancelled");
        result.GetFirstText().Should().NotContain("✅");
    }
}
