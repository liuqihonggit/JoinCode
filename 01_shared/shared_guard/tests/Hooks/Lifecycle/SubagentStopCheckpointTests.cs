namespace Core.Tests.Hooks.Lifecycle;


public class SubagentStopCheckpointTests
{
    private readonly Mock<IGitSecretScanner> _secretScannerMock;
    private readonly Mock<IGitDiffProvider> _diffProviderMock;
    private readonly Mock<IBuildQueueService> _buildQueueMock;
    private readonly SubagentStopCheckpoint _sut;

    public SubagentStopCheckpointTests()
    {
        _secretScannerMock = new Mock<IGitSecretScanner>();
        _diffProviderMock = new Mock<IGitDiffProvider>();
        _buildQueueMock = new Mock<IBuildQueueService>();
        _sut = new SubagentStopCheckpoint(
            _secretScannerMock.Object,
            _diffProviderMock.Object,
            _buildQueueMock.Object);
    }

    [Fact]
    public async Task ExecuteAsync_NoWorkingDir_ShouldPass()
    {
        var context = new CheckpointContext { AgentId = "a1", SessionId = "s1", WorkingDirectory = "" };

        var result = await _sut.ExecuteAsync(context);

        Assert.True(result.Passed);
    }

    [Fact]
    public async Task ExecuteAsync_NoSecretsAndBuildPass_ShouldPass()
    {
        var context = CreateContext();
        SetupSecretScan(safe: true);
        SetupBuild(exitCode: 0);

        var result = await _sut.ExecuteAsync(context);

        Assert.True(result.Passed);
        Assert.Empty(result.Violations);
    }

    [Fact]
    public async Task ExecuteAsync_SecretInFileName_ShouldFail()
    {
        var context = CreateContext();
        _diffProviderMock.Setup(d => d.GetStagedFileNamesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([".env"]);
        _secretScannerMock.Setup(s => s.ScanFileNamesAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ScanResult.Blocked([new SecretFinding { FilePath = ".env", MatchedPattern = "env-file", MatchedContent = ".env", Type = SecretType.SensitiveFile }]));
        _secretScannerMock.Setup(s => s.ScanContentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ScanResult.Safe);
        SetupBuild(exitCode: 0);

        var result = await _sut.ExecuteAsync(context);

        Assert.False(result.Passed);
        Assert.Contains(result.Violations, v => v.Rule == "no-secret-files");
    }

    [Fact]
    public async Task ExecuteAsync_SecretInDiff_ShouldFail()
    {
        var context = CreateContext();
        _diffProviderMock.Setup(d => d.GetStagedFileNamesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _secretScannerMock.Setup(s => s.ScanFileNamesAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ScanResult.Safe);
        _diffProviderMock.Setup(d => d.GetStagedDiffAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("+API_KEY=sk-xxx");
        _secretScannerMock.Setup(s => s.ScanContentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ScanResult.Blocked([new SecretFinding { FilePath = "config.cs", LineNumber = 5, MatchedPattern = "openai-key", MatchedContent = "sk-xxx", Type = SecretType.ApiKey }]));
        SetupBuild(exitCode: 0);

        var result = await _sut.ExecuteAsync(context);

        Assert.False(result.Passed);
        Assert.Contains(result.Violations, v => v.Rule == "no-secrets-in-diff");
    }

    [Fact]
    public async Task ExecuteAsync_BuildFails_ShouldFail()
    {
        var context = CreateContext();
        SetupSecretScan(safe: true);
        SetupBuild(exitCode: 1, output: "error CS0103: The name 'x' does not exist");

        var result = await _sut.ExecuteAsync(context);

        Assert.False(result.Passed);
        Assert.Contains(result.Violations, v => v.Rule == "build-must-pass");
    }

    [Fact]
    public async Task ExecuteAsync_SecretScanThrows_ShouldAddWarning()
    {
        var context = CreateContext();
        _diffProviderMock.Setup(d => d.GetStagedFileNamesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("git error"));
        SetupBuild(exitCode: 0);

        var result = await _sut.ExecuteAsync(context);

        Assert.True(result.Passed);
        Assert.Contains(result.Violations, v => v.Rule == "secret-scan-error" && v.Severity == "warning");
    }

    [Fact]
    public async Task ExecuteAsync_NullContext_ShouldThrow()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _sut.ExecuteAsync(null!));
    }

    [Fact]
    public async Task ExecuteAsync_WithWorktreePath_ShouldUseWorktreeDir()
    {
        var context = new CheckpointContext { AgentId = "a1", SessionId = "s1", WorktreePath = "/tmp/worktree", WorkingDirectory = "/main" };

        _diffProviderMock.Setup(d => d.GetStagedFileNamesAsync("/tmp/worktree", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _secretScannerMock.Setup(s => s.ScanFileNamesAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ScanResult.Safe);
        _diffProviderMock.Setup(d => d.GetStagedDiffAsync("/tmp/worktree", It.IsAny<CancellationToken>()))
            .ReturnsAsync("");
        _secretScannerMock.Setup(s => s.ScanContentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ScanResult.Safe);
        SetupBuild(exitCode: 0);

        var result = await _sut.ExecuteAsync(context);

        Assert.True(result.Passed);
        _diffProviderMock.Verify(d => d.GetStagedFileNamesAsync("/tmp/worktree", It.IsAny<CancellationToken>()), Times.Once);
    }

    private static CheckpointContext CreateContext() =>
        new() { AgentId = "agent-001", SessionId = "session-001", WorkingDirectory = "/repo" };

    private void SetupSecretScan(bool safe)
    {
        _diffProviderMock.Setup(d => d.GetStagedFileNamesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _secretScannerMock.Setup(s => s.ScanFileNamesAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(safe ? ScanResult.Safe : ScanResult.Blocked([new SecretFinding { FilePath = "secret.env", MatchedPattern = "env", MatchedContent = "secret.env", Type = SecretType.SensitiveFile }]));
        _diffProviderMock.Setup(d => d.GetStagedDiffAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("");
        _secretScannerMock.Setup(s => s.ScanContentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(safe ? ScanResult.Safe : ScanResult.Blocked([new SecretFinding { FilePath = "f.cs", LineNumber = 1, MatchedPattern = "key", MatchedContent = "sk-xxx", Type = SecretType.ApiKey }]));
    }

    private void SetupBuild(int exitCode, string output = "")
    {
        var buildResult = new BuildQueueResult
        {
            BuildId = "b1",
            ExitCode = exitCode,
            Output = output,
            ErrorOutput = "",
            WaitDuration = TimeSpan.Zero,
            BuildDuration = TimeSpan.FromSeconds(1),
            QueuePosition = 0
        };

        _buildQueueMock.Setup(b => b.SubmitAsync(It.IsAny<BuildRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("b1");
        _buildQueueMock.Setup(b => b.WaitAsync("b1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(buildResult);
    }
}
