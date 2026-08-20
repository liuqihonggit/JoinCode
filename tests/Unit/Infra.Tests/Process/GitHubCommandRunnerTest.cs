namespace Infra.Tests.Process;

/// <summary>
/// GitHubCommandRunner 单元测试 — 验证 ExecuteAsync / CreatePrAsync / ListPrsAsync / 重试机制 / PR 解析
/// </summary>
public sealed class GitHubCommandRunnerTest
{
    private readonly Mock<IProcessService> _processService = new();
    private readonly Mock<IGitCommandRunner> _gitRunner = new();
    private readonly PrBodyGenerator _prBodyGenerator;
    private readonly GitHubCommandRunner _runner;

    public GitHubCommandRunnerTest()
    {
        _prBodyGenerator = new PrBodyGenerator(_gitRunner.Object);
        _runner = new GitHubCommandRunner(_processService.Object, _prBodyGenerator);
    }

    private static ProcessResult CreateResult(int exitCode, string stdout = "", string stderr = "")
    {
        return new ProcessResult
        {
            ExitCode = exitCode,
            StandardOutput = stdout,
            StandardError = stderr,
            ExecutionTime = TimeSpan.FromMilliseconds(1)
        };
    }

    // === ExecuteAsync ===

    [Fact]
    public async Task ExecuteAsync_Success_ReturnsSuccessResult()
    {
        _processService.Setup(x => x.ExecuteAsync(It.IsAny<ProcessOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResult(0, "output", ""));

        var result = await _runner.ExecuteAsync("pr list");

        result.Success.Should().BeTrue();
        result.Output.Should().Be("output");
        result.ExitCode.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_NonZeroExitCode_ReturnsFailureResult()
    {
        _processService.Setup(x => x.ExecuteAsync(It.IsAny<ProcessOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResult(1, "", "error"));

        var result = await _runner.ExecuteAsync("pr list");

        result.Success.Should().BeFalse();
        result.Error.Should().Be("error");
        result.ExitCode.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_ProcessThrowsException_ReturnsFailureResult()
    {
        _processService.Setup(x => x.ExecuteAsync(It.IsAny<ProcessOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var result = await _runner.ExecuteAsync("pr list");

        result.Success.Should().BeFalse();
        result.Error.Should().Be("boom");
        result.ExitCode.Should().Be(-1);
    }

    [Fact]
    public async Task ExecuteAsync_PassesGitHubEnvironmentVariables()
    {
        ProcessOptions? captured = null;
        _processService.Setup(x => x.ExecuteAsync(It.IsAny<ProcessOptions>(), It.IsAny<CancellationToken>()))
            .Callback<ProcessOptions, CancellationToken>((opts, _) => captured = opts)
            .ReturnsAsync(CreateResult(0, "", ""));

        await _runner.ExecuteAsync("pr list");

        captured.Should().NotBeNull();
        captured!.FileName.Should().Be("gh");
        captured.Arguments.Should().Be("pr list");
        captured.EnvironmentVariables.Should().NotBeNull();
        captured.EnvironmentVariables!["GH_TERMINAL_PROMPT"].Should().Be("0");
        captured.EnvironmentVariables!["GH_FORCE_TTY"].Should().Be("100%");
    }

    [Fact]
    public async Task ExecuteAsync_PassesWorkingDirectory()
    {
        ProcessOptions? captured = null;
        _processService.Setup(x => x.ExecuteAsync(It.IsAny<ProcessOptions>(), It.IsAny<CancellationToken>()))
            .Callback<ProcessOptions, CancellationToken>((opts, _) => captured = opts)
            .ReturnsAsync(CreateResult(0, "", ""));

        await _runner.ExecuteAsync("pr list", "/repo");

        captured!.WorkingDirectory.Should().Be("/repo");
    }

    // === CreatePrAsync ===

    [Fact]
    public async Task CreatePrAsync_WithBody_UsesProvidedBody()
    {
        _processService.Setup(x => x.ExecuteAsync(It.IsAny<ProcessOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResult(0, "https://github.com/owner/repo/pull/123", ""));

        var result = await _runner.CreatePrAsync("title", "my body", "main", "feature");

        result.Success.Should().BeTrue();
        result.PrUrl.Should().Be("https://github.com/owner/repo/pull/123");
        result.PrNumber.Should().Be("123");
    }

    [Fact]
    public async Task CreatePrAsync_NullBody_AutoGeneratesBodyFromCommits()
    {
        _gitRunner.Setup(x => x.ExecuteAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitCommandResult { Success = true, Output = "feat: new feature" });
        _processService.Setup(x => x.ExecuteAsync(It.IsAny<ProcessOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResult(0, "https://github.com/owner/repo/pull/42", ""));

        var result = await _runner.CreatePrAsync("title", null, "main", "feature");

        result.Success.Should().BeTrue();
        result.PrNumber.Should().Be("42");
    }

    [Fact]
    public async Task CreatePrAsync_EmptyBody_AutoGeneratesBody()
    {
        _gitRunner.Setup(x => x.ExecuteAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitCommandResult { Success = true, Output = "" });
        _processService.Setup(x => x.ExecuteAsync(It.IsAny<ProcessOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResult(0, "https://github.com/o/r/pull/1", ""));

        var result = await _runner.CreatePrAsync("title", "  ", "main", "feature");

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task CreatePrAsync_Draft_AddsDraftFlag()
    {
        ProcessOptions? captured = null;
        _processService.Setup(x => x.ExecuteAsync(It.IsAny<ProcessOptions>(), It.IsAny<CancellationToken>()))
            .Callback<ProcessOptions, CancellationToken>((opts, _) => captured = opts)
            .ReturnsAsync(CreateResult(0, "https://github.com/o/r/pull/1", ""));

        await _runner.CreatePrAsync("t", "b", "main", "feature", draft: true);

        captured!.Arguments.Should().Contain("--draft");
    }

    [Fact]
    public async Task CreatePrAsync_NoDraft_OmitsDraftFlag()
    {
        ProcessOptions? captured = null;
        _processService.Setup(x => x.ExecuteAsync(It.IsAny<ProcessOptions>(), It.IsAny<CancellationToken>()))
            .Callback<ProcessOptions, CancellationToken>((opts, _) => captured = opts)
            .ReturnsAsync(CreateResult(0, "https://github.com/o/r/pull/1", ""));

        await _runner.CreatePrAsync("t", "b", "main", "feature", draft: false);

        captured!.Arguments.Should().NotContain("--draft");
    }

    [Fact]
    public async Task CreatePrAsync_WithRepo_AddsRepoFlag()
    {
        ProcessOptions? captured = null;
        _processService.Setup(x => x.ExecuteAsync(It.IsAny<ProcessOptions>(), It.IsAny<CancellationToken>()))
            .Callback<ProcessOptions, CancellationToken>((opts, _) => captured = opts)
            .ReturnsAsync(CreateResult(0, "https://github.com/o/r/pull/1", ""));

        await _runner.CreatePrAsync("t", "b", "main", "feature", repo: "owner/repo");

        captured!.Arguments.Should().Contain("--repo owner/repo");
    }

    [Fact]
    public async Task CreatePrAsync_Failure_ReturnsError()
    {
        _processService.Setup(x => x.ExecuteAsync(It.IsAny<ProcessOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResult(1, "", "some error"));

        var result = await _runner.CreatePrAsync("t", "b", "main", "feature");

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task CreatePrAsync_NullTitle_ThrowsArgumentNullException()
    {
        var act = async () => await _runner.CreatePrAsync(null!, "b", "main", "feature");

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task CreatePrAsync_NullBaseBranch_ThrowsArgumentNullException()
    {
        var act = async () => await _runner.CreatePrAsync("t", "b", null!, "feature");

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task CreatePrAsync_UrlWithNoPrNumber_ParsesNullNumber()
    {
        _processService.Setup(x => x.ExecuteAsync(It.IsAny<ProcessOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResult(0, "no url here", ""));

        var result = await _runner.CreatePrAsync("t", "b", "main", "feature");

        result.Success.Should().BeTrue();
        result.PrUrl.Should().BeNull();
        result.PrNumber.Should().BeNull();
    }

    // === ListPrsAsync ===

    [Fact]
    public async Task ListPrsAsync_Success_ParsesItems()
    {
        var output = "1\tTitle1\tbranch1\tOPEN\n2\tTitle2\tbranch2\tOPEN";
        _processService.Setup(x => x.ExecuteAsync(It.IsAny<ProcessOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResult(0, output, ""));

        var result = await _runner.ListPrsAsync();

        result.Success.Should().BeTrue();
        result.Items.Should().HaveCount(2);
        result.Items[0].Number.Should().Be("1");
        result.Items[0].Title.Should().Be("Title1");
        result.Items[0].Branch.Should().Be("branch1");
        result.Items[0].State.Should().Be("OPEN");
    }

    [Fact]
    public async Task ListPrsAsync_EmptyOutput_ReturnsEmptyList()
    {
        _processService.Setup(x => x.ExecuteAsync(It.IsAny<ProcessOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResult(0, "", ""));

        var result = await _runner.ListPrsAsync();

        result.Success.Should().BeTrue();
        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task ListPrsAsync_Failure_ReturnsError()
    {
        _processService.Setup(x => x.ExecuteAsync(It.IsAny<ProcessOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResult(1, "", "error"));

        var result = await _runner.ListPrsAsync();

        result.Success.Should().BeFalse();
        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task ListPrsAsync_WithRepo_AddsRepoFlag()
    {
        ProcessOptions? captured = null;
        _processService.Setup(x => x.ExecuteAsync(It.IsAny<ProcessOptions>(), It.IsAny<CancellationToken>()))
            .Callback<ProcessOptions, CancellationToken>((opts, _) => captured = opts)
            .ReturnsAsync(CreateResult(0, "", ""));

        await _runner.ListPrsAsync(repo: "owner/repo");

        captured!.Arguments.Should().Contain("--repo owner/repo");
    }

    [Fact]
    public async Task ListPrsAsync_PassesStateAndLimit()
    {
        ProcessOptions? captured = null;
        _processService.Setup(x => x.ExecuteAsync(It.IsAny<ProcessOptions>(), It.IsAny<CancellationToken>()))
            .Callback<ProcessOptions, CancellationToken>((opts, _) => captured = opts)
            .ReturnsAsync(CreateResult(0, "", ""));

        await _runner.ListPrsAsync(state: "closed", limit: 10);

        captured!.Arguments.Should().Contain("--state closed");
        captured.Arguments.Should().Contain("--limit 10");
    }

    // === 重试机制 ===

    [Fact]
    public async Task CreatePrAsync_RetryableError_RetriesAndSucceeds()
    {
        _processService.SetupSequence(x => x.ExecuteAsync(It.IsAny<ProcessOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResult(1, "", "timeout"))
            .ReturnsAsync(CreateResult(0, "https://github.com/o/r/pull/1", ""));

        var result = await _runner.CreatePrAsync("t", "b", "main", "feature");

        result.Success.Should().BeTrue();
        result.PrNumber.Should().Be("1");
    }

    [Fact]
    public async Task CreatePrAsync_NonRetryableError_DoesNotRetry()
    {
        _processService.Setup(x => x.ExecuteAsync(It.IsAny<ProcessOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResult(1, "", "bad request error"));

        var result = await _runner.CreatePrAsync("t", "b", "main", "feature");

        result.Success.Should().BeFalse();
        _processService.Verify(x => x.ExecuteAsync(It.IsAny<ProcessOptions>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ListPrsAsync_RetryableNetworkError_RetriesAndSucceeds()
    {
        var output = "1\tTitle\tbranch\tOPEN";
        _processService.SetupSequence(x => x.ExecuteAsync(It.IsAny<ProcessOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResult(1, "", "network error"))
            .ReturnsAsync(CreateResult(0, output, ""));

        var result = await _runner.ListPrsAsync();

        result.Success.Should().BeTrue();
        result.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task CreatePrAsync_RateLimitError_IsRetryable()
    {
        _processService.SetupSequence(x => x.ExecuteAsync(It.IsAny<ProcessOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResult(1, "", "rate limit exceeded"))
            .ReturnsAsync(CreateResult(0, "https://github.com/o/r/pull/1", ""));

        var result = await _runner.CreatePrAsync("t", "b", "main", "feature");

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task CreatePrAsync_ConnectionError_IsRetryable()
    {
        _processService.SetupSequence(x => x.ExecuteAsync(It.IsAny<ProcessOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResult(1, "", "connection refused"))
            .ReturnsAsync(CreateResult(0, "https://github.com/o/r/pull/1", ""));

        var result = await _runner.CreatePrAsync("t", "b", "main", "feature");

        result.Success.Should().BeTrue();
    }

    // === ParsePrUrl / ParsePrNumber (间接测试) ===

    [Fact]
    public async Task CreatePrAsync_MultiLineOutput_ExtractsUrlFromLines()
    {
        var output = "Creating pull request...\nhttps://github.com/owner/repo/pull/999\nDone";
        _processService.Setup(x => x.ExecuteAsync(It.IsAny<ProcessOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResult(0, output, ""));

        var result = await _runner.CreatePrAsync("t", "b", "main", "feature");

        result.PrUrl.Should().Be("https://github.com/owner/repo/pull/999");
        result.PrNumber.Should().Be("999");
    }
}
