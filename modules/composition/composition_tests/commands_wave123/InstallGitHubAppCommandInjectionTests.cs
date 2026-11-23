namespace Composition.Tests.Commands;

/// <summary>
/// InstallGitHubAppCommand 命令注入防御测试 — B3 修复验证
/// <para>覆盖: ① 恶意 git remote URL 解析后白名单校验拒绝 ② cmd.exe /c 回退路径消灭 ③ 合法 URL 仍可用</para>
/// </summary>
public sealed class InstallGitHubAppCommandInjectionTests {
    private static ProcessResult OkResult(string output) => new() {
        ExitCode = 0,
        StandardOutput = output,
        StandardError = string.Empty,
        ExecutionTime = TimeSpan.Zero,
    };

    private static (Mock<IProcessService> Mock, ProcessOptions?[] Captured) CreateCapturingProcessService(string output) {
        var captured = new ProcessOptions?[1];
        var mock = new Mock<IProcessService>();
        mock.Setup(p => p.ExecuteAsync(It.IsAny<ProcessOptions>(), It.IsAny<CancellationToken>()))
            .Callback<ProcessOptions, CancellationToken>((opt, _) => captured[0] = opt)
            .ReturnsAsync(OkResult(output));
        return (mock, captured);
    }

    private static ProcessOptions RequireCaptured(ProcessOptions?[] captured) {
        captured[0].Should().NotBeNull("应通过 IProcessService 执行并捕获 ProcessOptions");
        return captured[0] ?? throw new Xunit.Sdk.XunitException("captured ProcessOptions 为 null");
    }

    [Fact]
    public async Task GetCurrentRepo_MaliciousUrlWithShellInjection_ShouldReturnNull() {
        var (ps, _) = CreateCapturingProcessService("https://github.com/foo && start calc/bar.git");
        var cmd = new InstallGitHubAppCommand(processService: ps.Object);

        var result = await cmd.GetCurrentRepoAsync(CancellationToken.None);

        result.Should().BeNull("含 '&&' 的恶意 URL 解析后应被 GitHubRepoNameValidator 拒绝,不返回注入串");
    }

    [Theory]
    [InlineData("https://github.com/owner/repo.git", "owner/repo")]
    [InlineData("https://github.com/owner/repo", "owner/repo")]
    [InlineData("git@github.com:owner/repo.git", "owner/repo")]
    [InlineData("git@github.com:owner/repo", "owner/repo")]
    [InlineData("https://github.com/JoinCode/JoinCode.git", "JoinCode/JoinCode")]
    public async Task GetCurrentRepo_LegalUrl_ShouldReturnValidatedRepoName(string url, string expected) {
        var (ps, _) = CreateCapturingProcessService(url);
        var cmd = new InstallGitHubAppCommand(processService: ps.Object);

        var result = await cmd.GetCurrentRepoAsync(CancellationToken.None);

        result.Should().Be(expected, $"合法 URL '{url}' 应解析为 '{expected}'");
    }

    [Fact]
    public async Task GetCurrentRepo_NonGitHubUrl_ShouldReturnNull() {
        var (ps, _) = CreateCapturingProcessService("https://gitlab.com/owner/repo.git");
        var cmd = new InstallGitHubAppCommand(processService: ps.Object);

        var result = await cmd.GetCurrentRepoAsync(CancellationToken.None);

        result.Should().BeNull("非 github.com URL 应返回 null");
    }

    [Fact]
    public async Task RunShell_NeverUsesCmdExe_FallbackEliminated() {
        var (ps, captured) = CreateCapturingProcessService("https://github.com/owner/repo.git");
        var cmd = new InstallGitHubAppCommand(processService: ps.Object);

        await cmd.GetCurrentRepoAsync(CancellationToken.None);

        var opt = RequireCaptured(captured);
        opt.FileName.Should().Be("git", "git 命令应直接启动 git 进程,不走 cmd.exe /c 回退");
        opt.ArgumentList.Should().Contain("remote", "参数应通过 ArgumentList 逐个传递")
            .And.Contain("get-url")
            .And.Contain("origin");
        opt.ArgumentList.Should().NotContain("/c", "禁止 cmd.exe /c 回退路径");
    }

    [Fact]
    public async Task RunShell_GhCommand_UsesGhExeNotCmd() {
        var (ps, captured) = CreateCapturingProcessService("true");
        var cmd = new InstallGitHubAppCommand(processService: ps.Object);

        await cmd.VerifyRepoForTestAsync("owner/repo", CancellationToken.None);

        var opt = RequireCaptured(captured);
        opt.FileName.Should().Be("gh", "gh 命令应直接启动 gh 进程");
        opt.ArgumentList.Should().Contain("api")
            .And.Contain("repos/owner/repo");
        opt.FileName.Should().NotBe("cmd.exe");
    }

    [Fact]
    public async Task RunShell_MaliciousRepoName_ShouldNotReachProcess() {
        var ps = new Mock<IProcessService>();
        ps.Setup(p => p.ExecuteAsync(It.IsAny<ProcessOptions>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(OkResult("true"));
        var cmd = new InstallGitHubAppCommand(processService: ps.Object);

        await cmd.VerifyRepoForTestAsync("foo && start calc", CancellationToken.None);

        ps.Verify(p => p.ExecuteAsync(It.IsAny<ProcessOptions>(), It.IsAny<CancellationToken>()), Times.Never,
            "恶意 repoName 应在 GitHubRepoNameValidator 校验阶段被拒,不进入进程执行");
    }
}
