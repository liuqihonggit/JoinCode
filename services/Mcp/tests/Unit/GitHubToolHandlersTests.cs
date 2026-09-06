namespace Mcp.Tests;

public sealed class GitHubToolHandlersTests
{
    private readonly FakeGitHubCommandRunner _gh = new();
    private readonly FakeGitHubApiClient _api = new();
    private readonly GitHubToolHandlers _handler;

    public GitHubToolHandlersTests()
    {
        _handler = new GitHubToolHandlers(
            _gh,
            new FakeDownloader(),
            new InMemoryFileSystem(),
            new PersistencePipeline(new InMemoryFileSystem()),
            _api,
            null,
            NullLogger<GitHubToolHandlers>.Instance);
    }

    [Fact]
    public async Task PrView_Success_ReturnsOutput()
    {
        _api.NextResponse = new GitHubApiResponse
        {
            Success = true,
            StatusCode = 200,
            Body = """{"number":123,"title":"feat: add","state":"open","url":"https://github.com/o/r/pull/123"}""",
        };

        var result = await _handler.GhPrViewAsync("123", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("123");
        _api.LastMethod.Should().Be(HttpMethod.Get);
        _api.LastPath.Should().Be("repos/owner/repo/pulls/123");
    }

    [Fact]
    public async Task PrView_Failure_ReturnsError()
    {
        _api.NextResponse = new GitHubApiResponse
        {
            Success = false,
            StatusCode = 404,
            Error = "could not find pr",
        };

        var result = await _handler.GhPrViewAsync("999", repo: "owner/repo");

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("could not find pr");
    }

    [Fact]
    public async Task PrChecks_Skipping_NotCountedAsFail()
    {
        _api.EnqueueResponse(new GitHubApiResponse
        {
            Success = true,
            StatusCode = 200,
            Body = """{"head":{"sha":"abc123"}}""",
        });
        _api.EnqueueResponse(new GitHubApiResponse
        {
            Success = true,
            StatusCode = 200,
            Body = """{"check_runs":[{"name":"build","conclusion":"success"},{"name":"lint","conclusion":"skipped"},{"name":"test","conclusion":"failure"}]}""",
        });

        var result = await _handler.GhPrChecksAsync("1", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().Contain("1 通过");
        text.Should().Contain("1 失败");
        text.Should().Contain("1 跳过(依赖链跳过,非失败)");
    }

    [Fact]
    public async Task RunView_Log_TruncatesToMaxLines()
    {
        var lines = Enumerable.Range(0, 300).Select(i => $"line {i}").ToArray();
        _gh.NextResult = new GitHubCommandResult
        {
            Success = true,
            Output = string.Join('\n', lines),
            ExitCode = 0,
        };

        var result = await _handler.GhRunViewAsync("42", log: true, max_lines: 50);

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().Contain("共 300 行");
        text.Should().Contain("显示第 1-50 行");
        text.Should().Contain("skip_lines=50");
    }

    [Fact]
    public async Task RunView_NoLog_ReturnsFullDetail()
    {
        _gh.NextResult = new GitHubCommandResult
        {
            Success = true,
            Output = """{"databaseId":42,"status":"completed","conclusion":"success"}""",
            ExitCode = 0,
        };

        var result = await _handler.GhRunViewAsync("42");

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("42");
        _gh.LastArguments.Should().NotContain("--log");
    }

    [Fact]
    public async Task RunView_LogWithErrorFilter_ReturnsOnlyErrorLines()
    {
        _gh.NextResult = new GitHubCommandResult
        {
            Success = true,
            Output = "##[group]Run tests\n##[command]dotnet test\n##[error]Test failed: assert\n##[warning]deprecated\n##[error]Another error\nnormal line",
            ExitCode = 0,
        };

        var result = await _handler.GhRunViewAsync("42", log: true, filter: "error", max_lines: 10);

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().Contain("过滤:error");
        text.Should().Contain("##[error]Test failed: assert");
        text.Should().Contain("##[error]Another error");
        text.Should().NotContain("##[warning]");
        text.Should().NotContain("##[command]");
        text.Should().NotContain("normal line");
    }

    [Fact]
    public async Task RunView_LogWithWarningFilter_ReturnsErrorAndWarningLines()
    {
        _gh.NextResult = new GitHubCommandResult
        {
            Success = true,
            Output = "##[error]err\n##[warning]warn\n##[command]cmd\nnormal",
            ExitCode = 0,
        };

        var result = await _handler.GhRunViewAsync("42", log: true, filter: "warning", max_lines: 10);

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().Contain("##[error]err");
        text.Should().Contain("##[warning]warn");
        text.Should().NotContain("##[command]");
        text.Should().NotContain("normal");
    }

    [Fact]
    public async Task RunView_LogWithErrorFilter_NoMatch_ReturnsEmptyMessage()
    {
        _gh.NextResult = new GitHubCommandResult
        {
            Success = true,
            Output = "##[warning]just a warning\nnormal line\n##[command]dotnet build",
            ExitCode = 0,
        };

        var result = await _handler.GhRunViewAsync("42", log: true, filter: "error", max_lines: 10);

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("未匹配到任何日志行");
    }

    [Fact]
    public async Task RunView_ExpandSteps_ReturnsStepListFromCache()
    {
        _gh.NextResult = new GitHubCommandResult
        {
            Success = true,
            Output = "Job\tSet up job\t2026-01-01T00:00:00Z line1\nJob\tCheckout\t2026-01-01T00:00:01Z line2\nJob\tTest - Brain\t2026-01-01T00:00:02Z ##[error]failed",
            ExitCode = 0,
        };

        var result = await _handler.GhRunViewAsync("100", expand: "steps");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().Contain("步骤列表");
        text.Should().Contain("Set up job");
        text.Should().Contain("Checkout");
        text.Should().Contain("Test - Brain");
    }

    [Fact]
    public async Task RunView_ExpandStepName_ReturnsSectionSummaryForThatStepOnly()
    {
        _gh.NextResult = new GitHubCommandResult
        {
            Success = true,
            Output = "Job\tSet up job\t2026-01-01T00:00:00Z setup line\nJob\tTest - Brain\t2026-01-01T00:00:01Z ##[error]failed\nJob\tTest - Brain\t2026-01-01T00:00:02Z test output",
            ExitCode = 0,
        };

        var result = await _handler.GhRunViewAsync("104", expand: "step:Test - Brain");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().Contain("步骤:Test - Brain");
        text.Should().Contain("error");
        text.Should().Contain("normal");
        text.Should().NotContain("setup line");
    }

    [Fact]
    public async Task RunView_ExpandStepName_ReturnsSectionSummary()
    {
        _gh.NextResult = new GitHubCommandResult
        {
            Success = true,
            Output = "Job\tTest - Brain\t2026-01-01T00:00:00Z ##[error]err line\nJob\tTest - Brain\t2026-01-01T00:00:01Z normal line\nJob\tTest - Brain\t2026-01-01T00:00:02Z ##[warning]warn line",
            ExitCode = 0,
        };

        var result = await _handler.GhRunViewAsync("101", expand: "step:Test - Brain");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().Contain("sections");
        text.Should().Contain("error");
        text.Should().Contain("warning");
        text.Should().Contain("normal");
    }

    [Fact]
    public async Task RunView_ExpandStepSection_ReturnsSectionContent()
    {
        _gh.NextResult = new GitHubCommandResult
        {
            Success = true,
            Output = "Job\tTest - Brain\t2026-01-01T00:00:00Z ##[error]err line\nJob\tTest - Brain\t2026-01-01T00:00:01Z normal line\nJob\tTest - Brain\t2026-01-01T00:00:02Z ##[warning]warn line",
            ExitCode = 0,
        };

        var result = await _handler.GhRunViewAsync("102", expand: "step:Test - Brain/section:error");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().Contain("##[error]err line");
        text.Should().NotContain("normal line");
        text.Should().NotContain("##[warning]");
    }

    [Fact]
    public async Task RunView_LogWithSkipLines_ReturnsLinesAfterSkip()
    {
        var lines = Enumerable.Range(0, 100).Select(i => $"line {i}").ToArray();
        _gh.NextResult = new GitHubCommandResult
        {
            Success = true,
            Output = string.Join('\n', lines),
            ExitCode = 0,
        };

        var result = await _handler.GhRunViewAsync("42", log: true, max_lines: 10, skip_lines: 50);

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().Contain("line 50");
        text.Should().Contain("line 59");
        text.Should().NotContain("line 49");
    }

    [Fact]
    public async Task RunView_SkipLinesExceedsTotal_ReturnsNoMoreMessage()
    {
        _gh.NextResult = new GitHubCommandResult
        {
            Success = true,
            Output = "line 0\nline 1\nline 2",
            ExitCode = 0,
        };

        var result = await _handler.GhRunViewAsync("42", log: true, max_lines: 10, skip_lines: 100);

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("已跳过全部");
    }

    [Fact]
    public async Task RunView_ExpandStepSectionWithSkipLines_ReturnsLinesAfterSkipInSection()
    {
        _gh.NextResult = new GitHubCommandResult
        {
            Success = true,
            Output = string.Join('\n', Enumerable.Range(0, 50).Select(i => $"Job\tTest\t2026-01-01T00:00:00Z line {i}")),
            ExitCode = 0,
        };

        var result = await _handler.GhRunViewAsync("103", expand: "step:Test/section:normal", max_lines: 10, skip_lines: 20);

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().Contain("line 20");
        text.Should().Contain("line 29");
        text.Should().NotContain("line 19");
    }

    [Fact]
    public async Task IssueCreate_QuotesTitleWithSpaces()
    {
        _api.NextResponse = new GitHubApiResponse
        {
            Success = true,
            StatusCode = 201,
            Body = """{"number":1,"html_url":"https://github.com/o/r/issues/1"}""",
        };

        await _handler.GhIssueCreateAsync("fix: bug in parser", body: "details here", repo: "owner/repo");

        _api.LastMethod.Should().Be(HttpMethod.Post);
        _api.LastPath.Should().Be("repos/owner/repo/issues");
        _api.LastBody.Should().Contain("\"title\":\"fix: bug in parser\"");
        _api.LastBody.Should().Contain("\"body\":\"details here\"");
    }

    [Fact]
    public async Task PrMerge_DefaultSquash_AppendsAutoWhenRequested()
    {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = "" };

        await _handler.GhPrMergeAsync("5", auto_merge: true, repo: "owner/repo");

        _api.LastMethod.Should().Be(HttpMethod.Put);
        _api.LastPath.Should().Be("repos/owner/repo/pulls/5/enable-automerge");
        _api.LastBody.Should().Contain("squash");
    }

    [Fact]
    public async Task Api_Get_DisablesJq_PassesMethod()
    {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"id":1,"name":"repo"}""" };

        var result = await _handler.GhApiAsync("repos/owner/repo", method: "GET");

        result.IsError.Should().BeFalse();
        _api.LastMethod.Should().Be(HttpMethod.Get);
        _api.LastPath.Should().Be("repos/owner/repo");
    }

    [Fact]
    public async Task ReleaseDownload_NoMatchingAsset_ReturnsError()
    {
        _gh.NextResult = new GitHubCommandResult
        {
            Success = true,
            Output = """{"assets":[{"name":"file.zip","url":"https://x/file.zip"}]}""",
            ExitCode = 0,
        };

        var result = await _handler.GhReleaseDownloadAsync("v1.0", "/tmp", pattern: "*.tar.gz");

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("没有匹配的 asset");
    }

    [Fact]
    public async Task ReleaseDownload_Success_DownloadsAllAssets()
    {
        _gh.NextResult = new GitHubCommandResult
        {
            Success = true,
            Output = """{"assets":[{"name":"a.zip","url":"https://x/a.zip"},{"name":"b.tar.gz","url":"https://x/b.tar.gz"}]}""",
            ExitCode = 0,
        };
        var fakeDownloader = new FakeDownloader();
        var handler = new GitHubToolHandlers(_gh, fakeDownloader, new InMemoryFileSystem(), new PersistencePipeline(new InMemoryFileSystem()), _api, null, NullLogger<GitHubToolHandlers>.Instance);

        var result = await handler.GhReleaseDownloadAsync("v1.0", "/tmp");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().Contain("2 成功");
        text.Should().Contain("0 失败");
        fakeDownloader.StartCallCount.Should().Be(2);
    }

    [Fact]
    public async Task ReleaseDownload_ViewFails_PropagatesError()
    {
        _gh.NextResult = new GitHubCommandResult
        {
            Success = false,
            Error = "release not found",
            ExitCode = 1,
        };

        var result = await _handler.GhReleaseDownloadAsync("v9.9", "/tmp");

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("release not found");
    }
}

internal sealed class FakeGitHubCommandRunner : IGitHubCommandRunner
{
    public GitHubCommandResult NextResult { get; set; } = new() { Success = true, Output = "{}", ExitCode = 0 };
    public string? LastArguments { get; private set; }

    public Task<GitHubCommandResult> ExecuteAsync(string arguments, string? workingDirectory = null, int? timeoutMs = null, CancellationToken ct = default)
    {
        LastArguments = arguments;
        return Task.FromResult(NextResult);
    }

    public async IAsyncEnumerable<string> ExecuteStreamingAsync(
        string arguments,
        string? workingDirectory = null,
        int? timeoutMs = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        LastArguments = arguments;
        if (string.IsNullOrEmpty(NextResult.Output)) yield break;
        foreach (var line in NextResult.Output.Split('\n'))
        {
            ct.ThrowIfCancellationRequested();
            yield return line;
        }
    }

    public Task<PrCreateResult> CreatePrAsync(string title, string? body, string baseBranch, string headBranch, string? repo = null, bool draft = false, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<PrListResult> ListPrsAsync(string? repo = null, string state = "open", int limit = 30, CancellationToken ct = default)
        => throw new NotImplementedException();
}

internal sealed class FakeGitHubApiClient : IGitHubApiClient
{
    private readonly Queue<GitHubApiResponse> _responses = new();
    public GitHubApiResponse NextResponse
    {
        get => _responses.Count > 0 ? _responses.Peek() : _default;
        set { _responses.Clear(); _responses.Enqueue(value); }
    }
    private readonly GitHubApiResponse _default = new() { Success = true, StatusCode = 200, Body = "[]" };
    public string? LastPath { get; private set; }
    public HttpMethod? LastMethod { get; private set; }
    public string? LastBody { get; private set; }
    public void EnqueueResponse(GitHubApiResponse response) => _responses.Enqueue(response);

    public Task<GitHubApiResponse> SendAsync(HttpMethod method, string path, string? body = null, IReadOnlyDictionary<string, string>? query = null, bool paginate = false, CancellationToken ct = default)
    {
        LastMethod = method;
        LastPath = path;
        LastBody = body;
        var response = _responses.Count > 0 ? _responses.Dequeue() : _default;
        return Task.FromResult(response);
    }

    public async IAsyncEnumerable<string> GetRunLogsAsync(string owner, string repo, long runId, [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield break;
    }
}

internal sealed class FakeDownloader : IDownloader
{
    public DownloadResult NextResult { get; set; } = new(true, "", 100, 100, TimeSpan.Zero, DownloadState.Completed);
    public int StartCallCount { get; private set; }
    public IDownloadSession StartDownload(string url, string filePath, DownloadOptions? options = null, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        StartCallCount++;
        return new FakeDownloadSession { Result = NextResult with { FilePath = filePath } };
    }
}

internal sealed class FakeDownloadSession : IDownloadSession
{
    public DownloadResult Result { get; set; } = new(true, "", 0, 0, TimeSpan.Zero, DownloadState.Completed);
    public DownloadState State => Result.FinalState;
    public Task PauseAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task ResumeAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task CancelAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task<DownloadResult> WaitForCompletionAsync(CancellationToken ct = default) => Task.FromResult(Result);
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
