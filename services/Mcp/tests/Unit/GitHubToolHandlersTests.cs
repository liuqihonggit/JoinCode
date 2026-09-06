namespace Mcp.Tests;

public sealed class GitHubToolHandlersTests
{
    private readonly FakeGitHubApiClient _api = new();
    private readonly GitHubToolHandlers _handler;

    public GitHubToolHandlersTests()
    {
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
        _api.NextLogLines = lines;

        var result = await _handler.GhRunViewAsync("42", log: true, max_lines: 50, repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().Contain("line 0");
        text.Should().Contain("line 49");
        text.Should().Contain("skip_lines=50");
    }

    [Fact]
    public async Task RunView_NoLog_ReturnsFullDetail()
    {
        _api.NextResponse = new GitHubApiResponse
        {
            Success = true,
            StatusCode = 200,
            Body = """{"databaseId":42,"status":"completed","conclusion":"success"}""",
        };

        var result = await _handler.GhRunViewAsync("42", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("42");
    }

    [Fact]
    public async Task RunView_LogWithErrorFilter_ReturnsOnlyErrorLines()
    {
        _api.NextLogLines = "##[group]Run tests\n##[command]dotnet test\n##[error]Test failed: assert\n##[warning]deprecated\n##[error]Another error\nnormal line".Split('\n');

        var result = await _handler.GhRunViewAsync("42", log: true, filter: "error", max_lines: 10, repo: "owner/repo");

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
        _api.NextLogLines = "##[error]err\n##[warning]warn\n##[command]cmd\nnormal".Split('\n');

        var result = await _handler.GhRunViewAsync("42", log: true, filter: "warning", max_lines: 10, repo: "owner/repo");

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
        _api.NextLogLines = "##[warning]just a warning\nnormal line\n##[command]dotnet build".Split('\n');

        var result = await _handler.GhRunViewAsync("42", log: true, filter: "error", max_lines: 10, repo: "owner/repo");

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("未匹配到任何日志行");
    }

    [Fact]
    public async Task RunView_ExpandSteps_ReturnsStepListFromCache()
    {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"jobs":[]}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"updated_at":"2026-01-01T00:00:00Z"}""" });
        _api.NextLogLines = "Job\tSet up job\t2026-01-01T00:00:00Z line1\nJob\tCheckout\t2026-01-01T00:00:01Z line2\nJob\tTest - Brain\t2026-01-01T00:00:02Z ##[error]failed".Split('\n');

        var result = await _handler.GhRunViewAsync("100", expand: "steps", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().Contain("步骤列表");
        text.Should().Contain("Set up job");
        text.Should().Contain("Checkout");
        text.Should().Contain("Test - Brain");
    }

    [Fact]
    public async Task RunView_ExpandSteps_RestApiLogFormat_ExtractsStepNamesFromEntryName()
    {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"jobs":[]}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"updated_at":"2026-01-01T00:00:00Z"}""" });
        _api.NextLogLines = "[0_Set up job.txt] 2026-01-01T00:00:00Z line1\n[1_Checkout.txt] 2026-01-01T00:00:01Z line2\n[2_Test.txt] 2026-01-01T00:00:02Z ##[error]failed".Split('\n');

        var result = await _handler.GhRunViewAsync("100", expand: "steps", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().Contain("步骤列表");
        text.Should().Contain("Set up job");
        text.Should().Contain("Checkout");
        text.Should().Contain("Test");
        text.Should().NotContain("[0_");
        text.Should().NotContain(".txt]");
    }

    [Fact]
    public async Task RunView_ExpandSteps_ParallelDownload_RestApiLogFormat_ExtractsStepNames()
    {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"jobs":[{"id":1,"name":"build"},{"id":2,"name":"test"}]}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"updated_at":"2026-01-01T00:00:00Z"}""" });
        _api.NextLogLines = "[0_Checkout.txt] 2026-01-01T00:00:00Z line1\n[1_Build.txt] 2026-01-01T00:00:01Z line2".Split('\n');

        var result = await _handler.GhRunViewAsync("200", expand: "steps", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().Contain("步骤列表");
        text.Should().Contain("Checkout");
        text.Should().Contain("Build");
    }

    [Fact]
    public async Task RunView_ExpandStepName_ReturnsSectionSummaryForThatStepOnly()
    {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"jobs":[]}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"updated_at":"2026-01-01T00:00:00Z"}""" });
        _api.NextLogLines = "Job\tSet up job\t2026-01-01T00:00:00Z setup line\nJob\tTest - Brain\t2026-01-01T00:00:01Z ##[error]failed\nJob\tTest - Brain\t2026-01-01T00:00:02Z test output".Split('\n');

        var result = await _handler.GhRunViewAsync("104", expand: "step:Test - Brain", repo: "owner/repo");

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
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"jobs":[]}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"updated_at":"2026-01-01T00:00:00Z"}""" });
        _api.NextLogLines = "Job\tTest - Brain\t2026-01-01T00:00:00Z ##[error]err line\nJob\tTest - Brain\t2026-01-01T00:00:01Z normal line\nJob\tTest - Brain\t2026-01-01T00:00:02Z ##[warning]warn line".Split('\n');

        var result = await _handler.GhRunViewAsync("101", expand: "step:Test - Brain", repo: "owner/repo");

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
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"jobs":[]}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"updated_at":"2026-01-01T00:00:00Z"}""" });
        _api.NextLogLines = "Job\tTest - Brain\t2026-01-01T00:00:00Z ##[error]err line\nJob\tTest - Brain\t2026-01-01T00:00:01Z normal line\nJob\tTest - Brain\t2026-01-01T00:00:02Z ##[warning]warn line".Split('\n');

        var result = await _handler.GhRunViewAsync("102", expand: "step:Test - Brain/section:error", repo: "owner/repo");

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
        _api.NextLogLines = lines;

        var result = await _handler.GhRunViewAsync("42", log: true, max_lines: 10, skip_lines: 50, repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().Contain("line 50");
        text.Should().Contain("line 59");
        text.Should().NotContain("line 49");
    }

    [Fact]
    public async Task RunView_SkipLinesExceedsTotal_ReturnsNoMoreMessage()
    {
        _api.NextLogLines = "line 0\nline 1\nline 2".Split('\n');

        var result = await _handler.GhRunViewAsync("42", log: true, max_lines: 10, skip_lines: 100, repo: "owner/repo");

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("未匹配到更多日志行");
    }

    [Fact]
    public async Task RunView_ExpandStepSectionWithSkipLines_ReturnsLinesAfterSkipInSection()
    {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"jobs":[]}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"updated_at":"2026-01-01T00:00:00Z"}""" });
        _api.NextLogLines = Enumerable.Range(0, 50).Select(i => $"Job\tTest\t2026-01-01T00:00:00Z line {i}").ToArray();

        var result = await _handler.GhRunViewAsync("103", expand: "step:Test/section:normal", max_lines: 10, skip_lines: 20, repo: "owner/repo");

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
        _api.NextResponse = new GitHubApiResponse
        {
            Success = true,
            StatusCode = 200,
            Body = """{"assets":[{"name":"file.zip","browser_download_url":"https://x/file.zip"}]}""",
        };

        var result = await _handler.GhReleaseDownloadAsync("v1.0", "/tmp", pattern: "*.tar.gz", repo: "owner/repo");

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("没有匹配的 asset");
    }

    [Fact]
    public async Task ReleaseDownload_Success_DownloadsAllAssets()
    {
        _api.NextResponse = new GitHubApiResponse
        {
            Success = true,
            StatusCode = 200,
            Body = """{"assets":[{"name":"a.zip","browser_download_url":"https://x/a.zip"},{"name":"b.tar.gz","browser_download_url":"https://x/b.tar.gz"}]}""",
        };
        var fakeDownloader = new FakeDownloader();
        var handler = new GitHubToolHandlers(fakeDownloader, new InMemoryFileSystem(), new PersistencePipeline(new InMemoryFileSystem()), _api, null, NullLogger<GitHubToolHandlers>.Instance);

        var result = await handler.GhReleaseDownloadAsync("v1.0", "/tmp", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().Contain("2 成功");
        text.Should().Contain("0 失败");
        fakeDownloader.StartCallCount.Should().Be(2);
    }

    [Fact]
    public async Task ReleaseDownload_ViewFails_PropagatesError()
    {
        _api.NextResponse = new GitHubApiResponse
        {
            Success = false,
            StatusCode = 404,
            Error = "release not found",
        };

        var result = await _handler.GhReleaseDownloadAsync("v9.9", "/tmp", repo: "owner/repo");

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("release not found");
    }
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
    public IEnumerable<string> NextLogLines { get; set; } = Array.Empty<string>();

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
        foreach (var line in NextLogLines)
        {
            ct.ThrowIfCancellationRequested();
            yield return line;
        }
    }

    public async IAsyncEnumerable<string> GetJobLogsAsync(string owner, string repo, long jobId, [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var line in NextLogLines)
        {
            ct.ThrowIfCancellationRequested();
            yield return line;
        }
    }

    public Task<GitHubApiResponse> UploadAssetAsync(string owner, string repo, long releaseId, string fileName, Stream fileStream, CancellationToken ct = default)
    {
        LastMethod = HttpMethod.Post;
        LastPath = $"repos/{owner}/{repo}/releases/{releaseId}/assets";
        return Task.FromResult(NextResponse);
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
