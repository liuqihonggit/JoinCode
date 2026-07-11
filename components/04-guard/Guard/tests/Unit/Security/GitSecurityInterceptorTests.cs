
namespace Core.Tests.Security;

public sealed class GitSecurityInterceptorTests
{
    private readonly Mock<IGitDiffProvider> _diffProviderMock = new();
    private readonly Mock<IGitSecretScanner> _scannerMock = new();
    private readonly GitSecurityInterceptor _interceptor;

    public GitSecurityInterceptorTests()
    {
        _interceptor = new GitSecurityInterceptor(
            _diffProviderMock.Object,
            _scannerMock.Object,
            NullLogger<GitSecurityInterceptor>.Instance);
    }

    [Fact]
    public async Task ScanBeforeCommitAsync_SafeFiles_ReturnsAllowed()
    {
        _diffProviderMock.Setup(p => p.GetStagedFileNamesAsync("repo", default))
            .ReturnsAsync(new List<string> { "Program.cs" }.AsReadOnly());
        _scannerMock.Setup(s => s.ScanFileNamesAsync(It.IsAny<IReadOnlyList<string>>(), default))
            .ReturnsAsync(ScanResult.Safe);
        _diffProviderMock.Setup(p => p.GetStagedDiffAsync("repo", default))
            .ReturnsAsync("safe diff content");
        _scannerMock.Setup(s => s.ScanContentAsync(It.IsAny<string>(), default))
            .ReturnsAsync(ScanResult.Safe);

        var result = await _interceptor.ScanBeforeCommitAsync("repo").ConfigureAwait(true);

        result.IsBlocked.Should().BeFalse();
    }

    [Fact]
    public async Task ScanBeforeCommitAsync_SensitiveFile_ReturnsBlocked()
    {
        var finding = new SecretFinding
        {
            FilePath = ".env",
            MatchedPattern = ".env",
            MatchedContent = ".env",
            Type = SecretType.SensitiveFile
        };

        _diffProviderMock.Setup(p => p.GetStagedFileNamesAsync("repo", default))
            .ReturnsAsync(new List<string> { ".env" }.AsReadOnly());
        _scannerMock.Setup(s => s.ScanFileNamesAsync(It.IsAny<IReadOnlyList<string>>(), default))
            .ReturnsAsync(ScanResult.Blocked(new List<SecretFinding> { finding }.AsReadOnly()));

        var result = await _interceptor.ScanBeforeCommitAsync("repo").ConfigureAwait(true);

        result.IsBlocked.Should().BeTrue();
        result.Findings.Should().Contain(f => f.FilePath == ".env");
    }

    [Fact]
    public async Task ScanBeforeCommitAsync_ApiKeyInContent_ReturnsBlocked()
    {
        var finding = new SecretFinding
        {
            FilePath = "config.txt",
            LineNumber = 1,
            MatchedPattern = "sk-[a-zA-Z0-9]{20,}",
            MatchedContent = "sk-abcdefghijklmnopqrstuvwxyz123456",
            Type = SecretType.ApiKey
        };

        _diffProviderMock.Setup(p => p.GetStagedFileNamesAsync("repo", default))
            .ReturnsAsync(new List<string> { "config.txt" }.AsReadOnly());
        _scannerMock.Setup(s => s.ScanFileNamesAsync(It.IsAny<IReadOnlyList<string>>(), default))
            .ReturnsAsync(ScanResult.Safe);
        _diffProviderMock.Setup(p => p.GetStagedDiffAsync("repo", default))
            .ReturnsAsync("+API_KEY=sk-abcdefghijklmnopqrstuvwxyz123456");
        _scannerMock.Setup(s => s.ScanContentAsync(It.IsAny<string>(), default))
            .ReturnsAsync(ScanResult.Blocked(new List<SecretFinding> { finding }.AsReadOnly()));

        var result = await _interceptor.ScanBeforeCommitAsync("repo").ConfigureAwait(true);

        result.IsBlocked.Should().BeTrue();
        result.Findings.Should().Contain(f => f.Type == SecretType.ApiKey);
    }

    [Fact]
    public async Task ScanBeforeCommitAsync_EmptyStaging_ReturnsAllowed()
    {
        _diffProviderMock.Setup(p => p.GetStagedFileNamesAsync("repo", default))
            .ReturnsAsync(new List<string>().AsReadOnly());

        var result = await _interceptor.ScanBeforeCommitAsync("repo").ConfigureAwait(true);

        result.IsBlocked.Should().BeFalse();
    }

    [Fact]
    public void ShouldScanTool_ReturnsTrue_ForGitCommit()
    {
        GitSecurityInterceptor.ShouldScanTool("git_commit").Should().BeTrue();
    }

    [Fact]
    public void ShouldScanTool_ReturnsTrue_ForGitAdd()
    {
        GitSecurityInterceptor.ShouldScanTool("git_add").Should().BeTrue();
    }

    [Fact]
    public void ShouldScanTool_ReturnsFalse_ForOtherTools()
    {
        GitSecurityInterceptor.ShouldScanTool(ShellToolNameConstants.ShellExecute).Should().BeFalse();
        GitSecurityInterceptor.ShouldScanTool("git_status").Should().BeFalse();
        GitSecurityInterceptor.ShouldScanTool("git_log").Should().BeFalse();
    }
}
