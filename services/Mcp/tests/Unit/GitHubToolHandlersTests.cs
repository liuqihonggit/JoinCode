namespace Mcp.Tests;

public sealed class GitHubToolHandlersTests
{
    private readonly FakeGitHubCommandRunner _gh = new();
    private readonly GitHubToolHandlers _handler;

    public GitHubToolHandlersTests()
    {
        _handler = new GitHubToolHandlers(
            _gh,
            new FakeDownloader(),
            new InMemoryFileSystem(),
            NullLogger<GitHubToolHandlers>.Instance);
    }

    [Fact]
    public async Task PrView_Success_ReturnsOutput()
    {
        _gh.NextResult = new GitHubCommandResult
        {
            Success = true,
            Output = """{"number":123,"title":"feat: add","state":"OPEN","url":"https://github.com/o/r/pull/123"}""",
            ExitCode = 0,
        };

        var result = await _handler.GhPrViewAsync("123");

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("123");
        _gh.LastArguments.Should().Contain("pr view 123");
        _gh.LastArguments.Should().Contain("--json");
    }

    [Fact]
    public async Task PrView_Failure_ReturnsError()
    {
        _gh.NextResult = new GitHubCommandResult
        {
            Success = false,
            Error = "could not find pr",
            ExitCode = 1,
        };

        var result = await _handler.GhPrViewAsync("999");

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("could not find pr");
    }

    [Fact]
    public async Task PrChecks_Skipping_NotCountedAsFail()
    {
        _gh.NextResult = new GitHubCommandResult
        {
            Success = true,
            Output = "build\tpass\t1m\thttps://x\nlint\tskipping\t0s\thttps://y\ntest\tfail\t2m\thttps://z",
            ExitCode = 0,
        };

        var result = await _handler.GhPrChecksAsync("1");

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
        text.Should().Contain("已截断");
        text.Should().Contain("共 300 行");
        text.Should().Contain("仅显示前 50 行");
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
    public async Task RunView_ExpandStepName_ReturnsOnlyThatStepFromCache()
    {
        _gh.NextResult = new GitHubCommandResult
        {
            Success = true,
            Output = "Job\tSet up job\t2026-01-01T00:00:00Z setup line\nJob\tTest - Brain\t2026-01-01T00:00:01Z ##[error]failed\nJob\tTest - Brain\t2026-01-01T00:00:02Z test output",
            ExitCode = 0,
        };

        var result = await _handler.GhRunViewAsync("101", expand: "step:Test - Brain");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().Contain("步骤:Test - Brain");
        text.Should().Contain("##[error]failed");
        text.Should().Contain("test output");
        text.Should().NotContain("setup line");
    }

    [Fact]
    public async Task RunView_ExpandStepName_WithFilter_AppliesMarkerFilter()
    {
        _gh.NextResult = new GitHubCommandResult
        {
            Success = true,
            Output = "Job\tTest - Brain\t2026-01-01T00:00:00Z ##[error]err line\nJob\tTest - Brain\t2026-01-01T00:00:01Z normal line\nJob\tTest - Brain\t2026-01-01T00:00:02Z ##[warning]warn line",
            ExitCode = 0,
        };

        var result = await _handler.GhRunViewAsync("102", expand: "step:Test - Brain", filter: "error");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().Contain("##[error]err line");
        text.Should().NotContain("normal line");
        text.Should().NotContain("##[warning]");
    }

    [Fact]
    public async Task IssueCreate_QuotesTitleWithSpaces()
    {
        _gh.NextResult = new GitHubCommandResult
        {
            Success = true,
            Output = "https://github.com/o/r/issues/1",
            ExitCode = 0,
        };

        await _handler.GhIssueCreateAsync("fix: bug in parser", body: "details here");

        _gh.LastArguments.Should().Contain("--title \"fix: bug in parser\"");
        _gh.LastArguments.Should().Contain("--body \"details here\"");
    }

    [Fact]
    public async Task PrMerge_DefaultSquash_AppendsAutoWhenRequested()
    {
        _gh.NextResult = new GitHubCommandResult { Success = true, Output = "", ExitCode = 0 };

        await _handler.GhPrMergeAsync("5", auto_merge: true);

        _gh.LastArguments.Should().Contain("pr merge 5");
        _gh.LastArguments.Should().Contain("--squash");
        _gh.LastArguments.Should().Contain("--auto");
    }

    [Fact]
    public async Task Api_Get_DisablesJq_PassesMethod()
    {
        _gh.NextResult = new GitHubCommandResult
        {
            Success = true,
            Output = """{"id":1,"name":"repo"}""",
            ExitCode = 0,
        };

        var result = await _handler.GhApiAsync("repos/owner/repo", method: "GET");

        result.IsError.Should().BeFalse();
        _gh.LastArguments.Should().Contain("--method GET");
        _gh.LastArguments.Should().NotContain("--jq");
        _gh.LastArguments.Should().Contain("repos/owner/repo");
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
        var handler = new GitHubToolHandlers(_gh, fakeDownloader, new InMemoryFileSystem(), NullLogger<GitHubToolHandlers>.Instance);

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
